# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

OW Tracker is a Windows desktop companion app for Overwatch (C# / WPF / .NET 9). It scrapes
match history from the in-game UI via **screen capture + OCR + a (stubbed) self-refining hero
classifier**, tracks play-session time, and persists to local SQLite.

**Hard constraint (BattleEye safety): zero interaction with the OW process or its memory.**
Everything is OS-/screen-level only — GDI screen capture, Win32 `SendInput`, and window-title
polling. Never add anything that reads/writes OW's process memory or hooks its API.

The full original spec is `ow-tracker-design.md`. Note it says .NET 8 and puts repository
implementations in Core — both are **out of date** (see below).

## Build / run / test

```bash
dotnet build OwTracker.sln                     # build all 5 projects
dotnet test                                     # run all xUnit tests
dotnet test --filter "ScreenDetector_IdentifiesCorrectScreen"   # single test / theory by name
dotnet run --project OwTracker.App              # run from source (dev)
publish/OwTracker.App.exe --scrape 10           # HEADLESS scrape of last 10 games, then exit
publish/OwTracker.App.exe --scrape-deep         # COMPLETE back-fill: whole list, ignore dup-stop
```

**Headless CLI scrape (`--scrape [N]`)** runs a scrape with no window and exits (output → `scrape.log`
as usual, plus the parent console). Requires OW running. This is the fast iteration loop: edit →
build → `publish` → `--scrape N` → read `%APPDATA%\OwTracker\scrape.log` + `debug\` frames → repeat,
without anyone clicking the UI. It starts the watcher itself (needed to locate OW's window) and
brings OW to the foreground, so it does drive the real game UI for ~15 s/game. Navigation to the
Game Reports list retries up to 3× (a transient race when a screen is still loading).

**`--scrape-deep [N]`** is the same but ignores the 3-consecutive-duplicate stop — a COMPLETE
back-fill that walks to the end of the list (terminates gracefully when the Down-arrow runs past
the last game) and saves every Teams frame to `debug\` for calibration. Slower: the scraper
re-navigates from the top each game (`Down`×N), so per-game cost grows down the list. A single
match that fails to open (transient VIEW miss) is recovered + skipped, not fatal (bails only after
5 in a row) — so a deep run actually reaches the end. Dedup is by `ReferenceEquals` on
`UpsertAsync` (returns existing instance on dup) so it does NOT update existing rows — to
re-complete partial records (e.g. add IsMe), Delete History first, then `--scrape-deep`.

**Distribution build — use `Publish.bat`** (or the equivalent command inside it):

```bash
dotnet publish OwTracker.App/OwTracker.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=false -o publish/
```

- **Single-file publishing is intentionally disabled.** Tesseract's native DLLs
  (`x64/tesseract50.dll`, `x64/leptonica-*.dll`) get silently dropped by single-file bundling →
  OCR throws `TargetInvocationException` on first use → every screen reads as `Unknown`. The
  `publish/` folder (with its `x64/` subfolder next to the exe) is the distribution unit.
- Publishing fails with file-lock errors if `OwTracker.App.exe` is still running — close it first.
- Only the **.NET 9 SDK** is installed, so everything targets **`net9.0-windows`** (not .NET 8).

## Architecture

Five projects. The dependency-flow detail below matters because it deviates from the design doc.

| Project | Role |
|---|---|
| `OwTracker.Core` | Models, service/repository **interfaces**, and most concrete services (watcher, capture, OCR, scraper, input). No EF dependency. |
| `OwTracker.Data` | `OwTrackerDbContext` + repository **implementations** + EF migrations. |
| `OwTracker.ML` | `StubHeroClassifier` (returns Unknown/0-confidence) + `HeroRosterProvider`. Real ONNX inference is deferred. |
| `OwTracker.App` | WPF shell (5 tabs), ViewModels, DI composition root in `App.xaml.cs`. |
| `OwTracker.Tests` | xUnit. Heavily used as an **OCR calibration harness** (see below). |

**Repository implementations live in `OwTracker.Data`, not Core** (the design doc is wrong). They
need `OwTrackerDbContext`; putting them in Core would make Core→Data while Data→Core (models) — a
cycle. Interfaces stay in `OwTracker.Core/Repositories/Interfaces`.

**DI:** everything is a singleton, wired in `App.xaml.cs`. Repositories use
`IDbContextFactory<OwTrackerDbContext>` (not a scoped context) so singletons can create
short-lived contexts safely. DB + tessdata live under `%APPDATA%\OwTracker\`.

**App startup is deliberately non-blocking** (`App.xaml.cs`): migrate DB → start watcher →
`Show()` the window immediately → then download tessdata + refresh tabs in a fire-and-forget
`BootstrapAsync`. `OcrEngine`'s Tesseract engine is lazily created on first use so DI resolution
never throws when tessdata is absent. Global exception handlers log to
`%APPDATA%\OwTracker\error.log`.

### The scraping pipeline (the heart of the app)

`HistoryScraper.ScrapeAsync` orchestrates a screen-driven state machine. There is **no API** —
it literally drives the game UI like a human and reads pixels back:

1. **`OverwatchWatcher`** — polls window titles via P/Invoke. `IsOwRunning` (OW exists anywhere)
   gates scraping; `IsOwInForeground` drives session timing. Exposes `OwWindowHandle`.
2. **`InputSimulator`** (`IInputSimulator`) — Win32 `SendInput`. `BringOwToForeground` uses the
   `AttachThreadInput` trick. **Keys are sent as hardware scancodes, not virtual-key codes** —
   OW's menu arrows accept virtual keys but action keys (Space = "select") only register as
   scancodes. Includes mouse move/click/scroll and a ~45 ms key-hold.
3. **`ScreenCapturer`** — GDI `CopyFromScreen` of the OW window; crops ROIs.
4. **`ScreenDetector`** — identifies the current `GameScreen` (EscapeMenu / CareerProfile /
   GameReportsList / MatchDetail) by OCR-ing label text in fractional-coordinate regions, and
   locates click targets by **word bounding box** (`OcrEngine.FindTextCenter` → `ReadWords`).
   Also `FindSelectionBox` — pixel-scans for the **cyan highlight box** (#3BFFFF) around the
   selected match row via `LockBits`, returning the densest contiguous cyan cluster.
5. **`OcrEngine`** — Tesseract 5.2 (LSTM). `ExtractSummary`, `ExtractTeams`, `ExtractQueueRow*`.

**Navigation is OCR-driven and resolution-independent**, not hard-coded pixels: find a label
by text → click its real word position. The Game Reports list is walked by **keyboard**: hover
the top row to highlight game 0 and leave the cursor *still* (mouse *motion* steals highlight
control), `Down`×N to highlight game N, `Space` to select, then click VIEW GAME REPORT. Stops on
3 consecutive DB duplicates, on a stuck (re-read) navigation, or when no cyan box is found.

**Identifying the local player ("is-me"):** never by username — nameplate badges shift the name
and rotate every ~2 months. Instead, per match the scraper opens **PERSONAL → ALL HEROES**
(`ScrapePersonalAllHeroesAsync`) and reads my combined totals (`OcrEngine.ExtractPersonalAllHeroes`).
`IdentifyMe` matches that **DamageDealt** against the rows (the local player is **always on the
top/My team**) — DMG is a near-unique key — and tags that `PlayerRecord.IsMe`. The Personal
numbers are also authoritative for my row (`Pick(personal, teams)`: the dense Teams scoreboard
misreads my row more than the Personal cards, e.g. my elims read 0 on Teams but 18 on Personal).
All-Heroes OCR is a hybrid: right column (DMG/Healing/Mitigation) per-cell; left column (E/A/D,
tiny isolated digits) via row-band (`ReadCardRow`). Debug frames go to `AppPaths.DebugDirectory`.

**Dedup:** `MatchRepository.UpsertAsync` returns the same instance on insert, the existing
instance on duplicate (`ReferenceEquals` distinguishes them). Key = `(MapName, MatchDatetime)`.

### OCR calibration — read this before touching OCR

OCR is fragile and was tuned empirically against real 2560×1440 screenshots. Hard-won lessons:

- **Preprocessing must NOT binarise or invert** — it destroyed JPEG screenshots (all reads became
  garbage like `[ee]`). Correct pipeline = greyscale + 2× HighQualityBicubic upscale only; let
  Tesseract's LSTM binarise internally. **No `tessedit_char_whitelist`** on text regions (causes
  hallucination) — and note it's **ignored entirely in `EngineMode.LstmOnly`** anyway.
- **Trained model is `tessdata_best` (~15 MB), not `tessdata_fast`.** Scraping OCR is offline, so
  accuracy ≫ speed. The fast model misread isolated scoreboard digits (`5`→`)`, `6`→`(e)`); best
  fixes them. `TessDataManager.IsReady` size-gates the file so old fast installs auto-upgrade.
- **Teams rows are NOT evenly spaced** — nameplate badges and player titles make individual rows
  taller, and the whole table stretches per map (Control/Flashpoint ~90 px/row, Hybrid/Escort ~85),
  drifting rows out of fixed ROIs → zero reads. **Every row centre is MEASURED, not assumed**
  (`HistoryScraper` Teams block):
  1. **Primary — white role-icon clusters (`ScreenDetector.FindTeamRowCentresByIcon`).** Each player
     has a bright-white role icon (tank shield / damage ammo / support cross) in the far-left
     role-icon column, sitting at its row's vertical centre on a uniform coloured row background.
     The detector first finds the **table's left edge** (full-height brightness — the scoreboard can
     sit a few dozen px left/right if the capture window is offset), then scans the icon column for
     near-white pixels (all channels >180; cyan/red bg and portraits fail this), clusters them, and
     takes each cluster's white-weighted **centre-of-mass** as the row centre. Each row is assigned
     to a team by its **background colour** (saturation bias `B−R`: cyan ≫0 → my team, red ≪0 →
     enemy; the desaturated grey column-header bar is dropped). Colour — not the VS gap — does the
     team split, so **leavers are handled**: a short team yields fewer clusters, and a whole enemy
     team leaving yields an empty enemy list (no red rows). Team size (5v5/6v6) falls out of the
     cluster counts. Used when it returns a sane structure (`PitchSane`: per-row pitch 68–102).
  2. **Fallback — fixed grid + red-block anchor.** If icon detection can't read the structure, the
     scraper uses `Teams_MyCentres(size)` (fixed pitch 88/80) and `Teams_EnemyCentres` anchored to
     the enemy **red row background** top (`FindEnemyTeamTopY`), with team size from the queue label.
  `ExtractTeams` has two overloads: explicit `(myCentres, enemyCentres)` and grid `(vsY, enemyTopY)`.
  Centre-counts are variable now — `BuildMatchRecord`/portrait-crop indices use `myTeam.Count`, not
  a fixed team size. (Earlier separator-line/role-icon-PEAK detection was tried and dropped as too
  fragile; the white-cluster centre-of-mass approach replaced it.)
- **Team size (5v5 vs 6v6)** is primarily the icon cluster count (above). The QUEUE LABEL is the
  fallback signal: 6v6 modes render the literal "6V6" in the game-reports queue label ("UNRANKED 6V6
  QUICK PLAY") — `ParseQueue` matches `[6G0]V[6G0]` and sets `QueueRowData.TeamSize`, used to size
  the fixed-grid fallback. The queue ROI is tall enough (108 px) to catch the "6V6" line despite
  cyan-box position variance, but not so tall it bleeds into adjacent list rows.
- **Teams stat columns are read as one wide strip per row, not six tiny ROIs.** `OcrEngine.ReadStatRow`
  OCRs `Teams_StatStrip` once, then snaps each detected number to its nearest column centre
  (`Teams_StatColumnCentersX`), re-merging comma-split fragments. Lone single-cell ROIs misread
  isolated digits badly; digits sharing one baseline read far more reliably. It reads the strip at
  **3 upscales (2×/3×/4×, via `PreProcess`/`ReadWords` `scale`) and votes per column by column
  TYPE**, because the two groups fail oppositely: **E/A/D (narrow, 1–2 digits)** error by *adding* a
  digit (9→90), so they keep the 2× baseline unless ≥2 scales agree on a non-zero; **DMG/HEAL/MIT
  (wide)** error by *dropping* digits (a comma-split "13,868" fragments to "11" at 2× and 4× while
  3× reads it whole), so once ≥2 scales see a number they take the **longest (most complete)** read,
  not the majority — otherwise two scales' coincident "11" would win. (≤1 wide scale sees a number →
  treated as 0, so a lone noise read on an empty cell doesn't become a value.)
- **Hero playtimes (my heroes) need no portrait classifier, all read from the Personal tab.**
  `ScrapePersonalAllHeroesAsync` opens **ALL HEROES first** and reads the sidebar hero names there
  (`ReadHeroTabName`, snapped to `HeroRoster`) — names must be read from a frame where the hero is
  NOT the selected/blue-highlighted tab, or they OCR to garbage. The sidebar name is solid **white**
  on the bright animated menu gradient, so `ReadHeroTabName` reads it via the **white-text mask**
  (`ReadRegionWhiteText`, white-first then plain fallback) — same fix as map names; the plain LSTM
  garbles "KIRIKO"→"| '4|*d]| Co" on bright frames. The ALL HEROES button's Y gives the slot range,
  but **hero count = filled sidebar slots** (`ScreenDetector.PersonalSlotHasHero`, white-name-text
  pixels), NOT `allHeroesSlot − 1` — the blank spacer pill before ALL HEROES is inconsistent (present
  with several heroes, absent with one), so the old formula dropped the only hero of a 1-hero match.
  The combined-stat cards (`ExtractPersonalAllHeroes`) are semi-transparent over the gradient, so
  DMG/HEAL/MIT and the E/A/D band are read via the **white-text mask** too (plain read gave
  "18,033"→"[RKO KK" on a bright frame → DMG 0 → IsMe couldn't be fingerprinted by DMG).
  It then **clicks through each real-hero slot** (skipping blank spacers) and reads
  its `PLAY TIME` (`ExtractHeroPlayTime`, `Personal_HeroPlayTime` ROI) — capturing EVERY hero
  played, not just the top-3 the Summary cards show. Stored as the IsMe player's `HeroPlaytimes`; slot 0 (highest
  play-time) becomes `EndingHero`. (The Summary "Heroes Played" cards' italic names are
  unreadable, so they're no longer used for this.) The portrait classifier (`heroNames` dict) is
  still only the *other* players' ending hero (stub → Unknown). **The hero roster is user-editable**:
  `HeroRoster.Heroes` = built-in defaults ∪ `%APPDATA%\OwTracker\heroes.txt` (one hero per line,
  seeded on first run) — new heroes ship constantly, so they can be added without a rebuild. (The
  built-in list will lag real releases; don't trust it as authoritative.)
- Coordinates in `UiCoordinates.cs` are **2560×1440-specific** and were calibrated via diagnostic
  sweep tests in `OcrSmokeTests.cs` that dump readings to `*-scan.txt` / `*-output.txt` at repo
  root. Test screenshots live in `test-screenshots/` (`.jpg`/`.png`). When recalibrating, add a
  sweep test, run it, and read the dumped file — don't guess. (`Diagnostics_VerifyTeamsRows` writes
  `teams-verify-*.txt` and now also dumps the real `ExtractTeams` strip-reader output.)
- Large italic fonts (VICTORY/DEFEAT) OCR poorly → outcome uses fuzzy first-letters + score
  fallback. Multi-line info boxes are read as ONE block + regex per field. `|`→`1` digit
  normalisation in number parsers.
- Sidebar/selected items with highlight backgrounds OCR badly — match on partial tokens
  (`REPORTS` not `GAME REPORTS`) and exclude the icon column.
- **Map names** read from the Summary header (solid white bold caps, `Summary_MapName` ROI,
  top-right above the thumbnail). The ROI is wide (600 px, reaching x≈2195) so long names
  ("WATCHPOINT: GIBRALTAR", "ANTARCTIC PENINSULA") aren't clipped. **Read via the white-text mask**
  (`OcrEngine.ReadRegionWhiteText` → `PreProcessWhiteText`): the menu background animates through
  bright purple gradients, and on a bright frame the plain LSTM mis-binarises even crisp text
  ("HAVANA"→"g FAT.", "DORADO"→"(pe 7.10 0)", "ESPERANÇA"→"3"). Thresholding luminance ≥190 to
  isolate the pure-white text → clean black-on-white makes the read background-independent — this
  is a deliberate exception to the "never binarise" rule (safe *only* because the target is pure
  white well above any background luminance). `ExtractSummary` uses the masked read first and falls
  back to the plain read only if Snap can't resolve it. `MapRoster.Snap` canonicalises the read to
  the fixed OW map pool (substring containment / longest-common-substring) — this also folds
  seasonal variants ("KING'S ROW (WINTER)" → "King's Row") to keep the `(MapName, MatchDatetime)`
  dedup key stable. A still-loading Summary header occasionally OCRs to symbol soup that Snap
  can't resolve; the scraper detects this with `MapRoster.IsKnown` and re-captures once after a
  settle (mirrors the Teams weak-read retry). Severe garbles deliberately fall through Snap
  (keep the raw read) rather than mis-snap to a wrong map. Update `MapRoster.Maps` as the pool
  changes; `MapRoster_AllCanonicalNamesRoundTrip` guards against typos/omissions.

The tests double as the calibration harness; many `Diagnostics_*` tests exist only to dump
coordinates and are expected to be edited/run iteratively, not kept green forever.

### Runtime data locations (`%APPDATA%\OwTracker\`)

`owtracker.db` (SQLite), `tessdata/eng.traineddata` (auto-downloaded), `scrape.log` (rewritten
each scrape run — primary debugging tool), `debug_*.png` (frames saved when a screen is
`Unknown` or a selection box isn't found), `error.log`, `crops/`.

## Conventions

- `CommunityToolkit.Mvvm` source generators: `[ObservableProperty] private T _x;` →
  `X`; `[RelayCommand] Task FooAsync()` → `FooCommand` (the `Async` suffix is stripped).
- `BooleanToVisibilityConverter` must be defined in each `UserControl`'s own `Resources` — a
  UserControl `StaticResource` lookup can't reach a parent Window's resources (caused a startup
  crash when defined only in App.xaml). The built-in one ignores `ConverterParameter`; for
  inverse logic use `OwTracker.App.Converters.InverseBoolToVisibilityConverter`.
- **Match History is master-detail.** The `DataGrid`'s `SelectedItem` binds to
  `MatchHistoryViewModel.SelectedMatch`; selecting a row fires `GetByIdAsync` (eager-loads
  players + my hero playtimes) and splits the result into `MyTeam` / `EnemyTeam` / `MyHeroes`
  collections the right-hand detail pane binds to. `GetAllAsync` loads `AllPlayers` but NOT
  `HeroPlaytimes` (only the by-id load does), so the detail pane must use the by-id record. The
  IsMe player's row is starred + highlighted via a `DataGridRow` `DataTrigger` on `IsMe`.
- Git repo: **https://github.com/TeenI126/Overwatcher** (public). `test-screenshots/`, build output
  (`bin`/`obj`/`publish`), local runtime artifacts (`debug/`, `*.db`, `*.log`), and root-level
  diagnostic dumps (`/*.txt`, `/*.png`) are gitignored — so OCR tests that need the screenshots
  won't run on a fresh clone. The ML classifier is interface-stubbed; real ONNX training/inference,
  the calibration overlay UI, and CSV export are deferred but have interfaces in place.
