# OW Tracker — Design Document v2
**Target:** Claude Code implementation guide  
**Stack:** C# / WPF / .NET 8  
**Resolution:** 1440p, default Overwatch UI scale  

---

## 1. Project Overview

A desktop companion app that:
1. Scrapes match history from the Overwatch in-game UI via screen capture + OCR + self-refining ML hero classification
2. Tracks session time (how long OW is open and in the foreground with user activity detected)
3. Persists all data to a local SQLite database

BattleEye safety constraint: **zero interaction with the OW process or its memory**. All observation is OS-level or screen-level only.

---

## 2. Solution Structure

```
OwTracker.sln
├── OwTracker.App/                   # WPF application (startup project)
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Views/
│   │   ├── DashboardView.xaml
│   │   ├── MatchHistoryView.xaml
│   │   ├── SessionView.xaml
│   │   └── HeroReviewView.xaml      # ML review queue UI
│   └── ViewModels/
│       ├── DashboardViewModel.cs
│       ├── MatchHistoryViewModel.cs
│       ├── SessionViewModel.cs
│       └── HeroReviewViewModel.cs
├── OwTracker.Core/
│   ├── Models/
│   │   ├── MatchRecord.cs
│   │   ├── PlayerRecord.cs
│   │   ├── HeroPlaytime.cs
│   │   ├── SessionRecord.cs
│   │   └── PendingHeroLabel.cs      # Review queue item
│   ├── Services/
│   │   ├── OverwatchWatcher.cs
│   │   ├── ScreenCapturer.cs
│   │   ├── HistoryScraper.cs
│   │   ├── OcrEngine.cs
│   │   ├── HeroClassifier.cs
│   │   ├── HeroModelTrainer.cs      # Incremental retraining
│   │   └── InputSimulator.cs
│   └── Repositories/
│       ├── Interfaces/
│       │   ├── IMatchRepository.cs
│       │   ├── ISessionRepository.cs
│       │   └── IHeroLabelRepository.cs
│       └── Implementations/
│           ├── MatchRepository.cs
│           ├── SessionRepository.cs
│           └── HeroLabelRepository.cs
├── OwTracker.Data/
│   ├── OwTrackerDbContext.cs
│   └── Migrations/
├── OwTracker.ML/
│   ├── HeroClassifierTrainer.cs
│   ├── HeroClassifierModel.cs
│   ├── PseudoLabelPipeline.cs       # Self-training logic
│   └── Assets/
│       ├── HeroRoster.json          # Canonical hero list — source of truth for classes
│       ├── hero_model.onnx          # Active inference model
│       └── training/
│           ├── confirmed/           # Human-verified crops (by hero name subfolder)
│           └── auto_accepted/       # High-confidence auto-labels
└── OwTracker.Tests/
```

---

## 3. NuGet Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite persistence |
| `Tesseract` (Tesseract.NET) | OCR engine wrapper |
| `OpenCvSharp4.Windows` | Image preprocessing |
| `Microsoft.ML` + `Microsoft.ML.Vision` | Hero image classifier training |
| `Microsoft.ML.OnnxRuntime` | ONNX model inference at runtime |
| `System.Drawing.Common` | GDI screen capture |
| `CommunityToolkit.Mvvm` | WPF MVVM boilerplate |
| `System.Text.Json` | HeroRoster.json parsing, JSON column serialization |

---

## 4. Data Models

### MatchRecord
```csharp
public class MatchRecord
{
    public int Id { get; set; }
    public string MapName { get; set; }
    public DateTime MatchDatetime { get; set; }
    public TimeSpan GameLength { get; set; }
    public int MyTeamScore { get; set; }
    public int EnemyTeamScore { get; set; }
    public MatchOutcome Outcome { get; set; }   // Win / Loss / Draw
    public DateTime ScrapedAt { get; set; }

    // Navigation
    public PlayerRecord MyStats { get; set; }
    public List<PlayerRecord> AllPlayers { get; set; }  // all 10 (or 6 in 3v3 etc.)
}

public enum MatchOutcome { Win, Loss, Draw, Unknown }
```

### PlayerRecord
```csharp
public class PlayerRecord
{
    public int Id { get; set; }
    public int MatchRecordId { get; set; }
    public bool IsMe { get; set; }
    public string Team { get; set; }            // "My Team" | "Enemy Team"

    // Teams tab — all players
    public string EndingHero { get; set; }      // Hero they ended the game on
    public int Eliminations { get; set; }
    public int Assists { get; set; }
    public int Deaths { get; set; }
    public int DamageDealt { get; set; }
    public int HealingDone { get; set; }
    public int DamageMitigated { get; set; }

    // Personal tab — my record only (IsMe == true)
    public List<HeroPlaytime> HeroPlaytimes { get; set; }  // null for other players
}
```

### HeroPlaytime
```csharp
public class HeroPlaytime
{
    public int Id { get; set; }
    public int PlayerRecordId { get; set; }
    public string HeroName { get; set; }
    public TimeSpan TimePlayed { get; set; }
}
```

### PendingHeroLabel (Review Queue)
```csharp
public class PendingHeroLabel
{
    public int Id { get; set; }
    public string CropPath { get; set; }        // path to saved image crop
    public string PredictedHero { get; set; }
    public float Confidence { get; set; }
    public string ConfirmedHero { get; set; }   // null until reviewed
    public bool Reviewed { get; set; }
    public DateTime CapturedAt { get; set; }
}
```

### SessionRecord
```csharp
public class SessionRecord
{
    public int Id { get; set; }
    public DateTime SessionStart { get; set; }
    public DateTime SessionEnd { get; set; }
    public TimeSpan ActiveDuration { get; set; }
    public TimeSpan TotalOpenDuration { get; set; }
}
```

---

## 5. Database Schema (EF Core / SQLite)

```sql
CREATE TABLE MatchRecords (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    MapName         TEXT NOT NULL,
    MatchDatetime   TEXT NOT NULL,
    GameLength      TEXT NOT NULL,
    MyTeamScore     INTEGER NOT NULL,
    EnemyTeamScore  INTEGER NOT NULL,
    Outcome         INTEGER NOT NULL,
    ScrapedAt       TEXT NOT NULL,
    UNIQUE(MapName, MatchDatetime)
);

CREATE TABLE PlayerRecords (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    MatchRecordId   INTEGER NOT NULL REFERENCES MatchRecords(Id),
    IsMe            INTEGER NOT NULL,   -- boolean
    Team            TEXT NOT NULL,
    EndingHero      TEXT NOT NULL,
    Eliminations    INTEGER NOT NULL,
    Assists         INTEGER NOT NULL,
    Deaths          INTEGER NOT NULL,
    DamageDealt     INTEGER NOT NULL,
    HealingDone     INTEGER NOT NULL,
    DamageMitigated INTEGER NOT NULL
);

CREATE TABLE HeroPlaytimes (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerRecordId  INTEGER NOT NULL REFERENCES PlayerRecords(Id),
    HeroName        TEXT NOT NULL,
    TimePlayed      TEXT NOT NULL
);

CREATE TABLE SessionRecords (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionStart        TEXT NOT NULL,
    SessionEnd          TEXT NOT NULL,
    ActiveDuration      TEXT NOT NULL,
    TotalOpenDuration   TEXT NOT NULL
);

CREATE TABLE PendingHeroLabels (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    CropPath        TEXT NOT NULL,
    PredictedHero   TEXT NOT NULL,
    Confidence      REAL NOT NULL,
    ConfirmedHero   TEXT,
    Reviewed        INTEGER NOT NULL DEFAULT 0,
    CapturedAt      TEXT NOT NULL
);
```

---

## 6. Module Specifications

### 6.1 OverwatchWatcher
**Purpose:** Monitor OW foreground state and user activity.

```csharp
[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
[DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
```

- Poll foreground window every 500ms via `PeriodicTimer`
- OW detected by window title containing `"Overwatch"` (verify exact string at implementation)
- User activity = system idle time < 30 seconds via `GetLastInputInfo`
- Expose `IsOwInForeground`, `IsUserActive` as observable properties
- Fire `SessionStarted` / `SessionEnded` events; `SessionEnded` persists a `SessionRecord`

### 6.2 ScreenCapturer
**Purpose:** Capture the OW window as a `Bitmap`; crop named regions.

```csharp
public Bitmap CaptureOwWindow();
public Bitmap CropRegion(Bitmap source, Rectangle roi);
public List<Bitmap> CropHeroPortraits(Bitmap source, HeroPortraitRegion region);
```

All ROI rectangles defined in `UiLayout1440p` static class. Overridable via `calibration.json`.

### 6.3 InputSimulator
**Purpose:** Navigate OW match history UI via `SendInput`.

- Only fires when OW is foreground AND a scrape is active
- Jitter delay: random 80–160ms between inputs
- `SendKey(VirtualKey key)` and `SendKeyWithDelay(VirtualKey key)`
- Key sequences needed: advance to next match entry, open detail view, switch tabs (Summary → Teams → Personal), navigate Personal hero subtabs

### 6.4 OcrEngine
**Purpose:** Extract text from cropped screenshots.

- Tesseract.NET, `eng.traineddata`, `PSM_SINGLE_LINE` per field
- Pre-process: grayscale → threshold → 2x upscale

**Regions to extract (calibrate pixel coords at 1440p):**

| Tab | Field | Notes |
|---|---|---|
| Summary | Map name | Single line |
| Summary | DateTime | Parse to `DateTime` |
| Summary | Game length | Parse to `TimeSpan` |
| Summary | My team score | Integer |
| Summary | Enemy team score | Integer |
| Summary | Outcome | "VICTORY" / "DEFEAT" / "DRAW" |
| Teams | Elims, assists, deaths, damage, healing, mitigation | Per player row × 10 |
| Personal | Hero subtab labels | Each hero name tab |
| Personal | Time played per hero | Per subtab |

### 6.5 HeroClassifier
**Purpose:** Identify heroes from portrait crops. Used for Teams tab ending-hero icons.

- Input: 64×64 RGB crop
- Output: `(HeroName: string, Confidence: float)`
- Inference via ONNX at runtime
- Hero class list sourced from `HeroRoster.json` — never hardcoded

**Note:** Personal tab hero identification is done via OCR on tab labels, not the classifier. The classifier is used only where hero identity must be inferred from an icon (Teams tab).

### 6.6 PseudoLabelPipeline (Self-Refining Model)

**Thresholds (configurable in app settings):**
- `AutoAcceptThreshold`: default 0.97 — predictions above this are added to `auto_accepted/` and flagged for next retrain
- `ReviewThreshold`: 0.70–0.97 — predictions in this range are saved to `PendingHeroLabels` for manual review
- Below `ReviewThreshold`: crop is saved but discarded from training until reviewed

**Self-training loop:**
```
OnPrediction(crop, predictedHero, confidence):
  Save crop to disk under %APPDATA%\OwTracker\crops\

  if confidence >= AutoAcceptThreshold:
    Copy to auto_accepted/{heroName}/
    Increment pending retrain counter

  elif confidence >= ReviewThreshold:
    Insert PendingHeroLabel(crop, predictedHero, confidence)
    Notify UI: review queue has new items

  if pendingRetrainCounter >= RetrainBatchSize (default 50):
    Trigger background incremental retrain
    Hot-swap hero_model.onnx on completion
    Reset counter
```

**Manual review flow (HeroReviewView):**
- Shows crop image + predicted hero + confidence
- User confirms or corrects the hero name
- On confirm: moves crop to `confirmed/{heroName}/`, marks `PendingHeroLabel.Reviewed = true`
- Confirmed items are included in the next retrain

**Hero roster expansion:**
- Add new hero name to `HeroRoster.json`
- Drop seed images into `training/confirmed/{newHeroName}/`
- Trigger manual retrain from settings UI
- No code changes required

**Retrain trigger (HeroModelTrainer):**
- Runs on background thread
- Loads all images from `confirmed/` + `auto_accepted/`
- Fine-tunes from current model checkpoint (not from scratch)
- Exports new ONNX to a temp path, validates accuracy, then atomically replaces `hero_model.onnx`
- Logs retrain summary (class count, sample count, accuracy) to DB

---

## 7. HistoryScraper — Full Navigation Flow

```
ScrapeAsync(CancellationToken ct):
  Assert OW is foreground

  For each match entry (navigate with Down arrow):
    // --- Summary Tab (default) ---
    screenshot = Capture()
    summary = OcrEngine.ExtractSummary(screenshot)
    // map, datetime, game length, scores, outcome

    // --- Teams Tab ---
    SendKey(TeamsTab)
    screenshot = Capture()
    players = OcrEngine.ExtractTeamsRows(screenshot)   // 10 rows
    For each player row:
      endingHeroCrop = CropHeroPortrait(screenshot, row)
      (hero, confidence) = HeroClassifier.Predict(endingHeroCrop)
      PseudoLabelPipeline.Process(endingHeroCrop, hero, confidence)
      player.EndingHero = hero

    // --- Personal Tab ---
    SendKey(PersonalTab)
    screenshot = Capture()
    heroTabs = OcrEngine.ExtractPersonalHeroTabs(screenshot)
    For each heroTab:
      SendKey(heroTab)
      screenshot = Capture()
      timePlayed = OcrEngine.ExtractTimePlayed(screenshot)
      Record HeroPlaytime(heroTab.Name, timePlayed)

    // --- Persist ---
    record = BuildMatchRecord(summary, players, heroPlaytimes)
    MatchRepository.Upsert(record)   // deduplicate on (MapName, MatchDatetime)

    if DetectEndOfHistory(currentScreenshot): break
    SendKey(Back)
    SendKeyWithDelay(Down)

  RaiseScrapeCompleted(recordsFound)
```

---

## 8. WPF UI Structure

### MainWindow
Tab-based: **Dashboard** | **Match History** | **Sessions** | **Hero Review** | **Settings**  
Status bar: OW detected indicator, live session timer, review queue badge count.

### Dashboard Tab
- Live session timer (active vs. total)
- Quick stats: matches scraped, win rate, most played heroes, avg game length
- **"Start Scrape"** button — enabled only when OW is foreground
- Scrape progress + per-match log output

### Match History Tab
- Sortable `DataGrid`: Date, Map, Outcome, Score, Game Length, Heroes Played (time-weighted), Elims/Deaths/Damage
- Filter: date range, map, outcome, hero played
- Expandable row: full team stats breakdown
- Export to CSV

### Sessions Tab
- Session list: date, active duration, total duration

### Hero Review Tab
- Queue of `PendingHeroLabel` items needing review
- Shows: hero portrait crop, predicted hero name, confidence bar
- Confirm / Correct dropdown → submit
- Badge on tab shows unreviewed count
- "Trigger Retrain" button (manual override)
- Retrain history log

### Settings Tab
- `AutoAcceptThreshold` slider
- `ReviewThreshold` slider
- `RetrainBatchSize` input
- "Open Training Data Folder" button
- Calibration mode launcher

---

## 9. Calibration Mode

Overlay transparent WPF window on OW. User clicks to define corners of:
- Map name region (Summary)
- DateTime region (Summary)
- Score regions (Summary)
- Game length region (Summary)
- Each player row on Teams tab (10 rows)
- Hero portrait position within a Teams row
- Personal hero tab strip
- Time played region within a Personal subtab

Saved to `%APPDATA%\OwTracker\calibration.json`.  
`UiLayout` class loads from file at startup, falls back to hardcoded 1440p defaults.

---

## 10. Build Notes

- Target: `.NET 8`, `<UseWPF>true</UseWPF>`
- `tessdata/` folder: `CopyToOutputDirectory = Always`
- `hero_model.onnx`: embedded resource in `OwTracker.ML`, extracted to `%APPDATA%\OwTracker\` on first run
- `HeroRoster.json`: embedded resource, user-overridable via app data copy
- SQLite DB: `%APPDATA%\OwTracker\owtracker.db`
- Crop storage: `%APPDATA%\OwTracker\crops\`

---

## 11. Implementation Order

1. **OwTracker.Data** — DbContext, all models, migrations
2. **OverwatchWatcher** — window detection + session timing
3. **ScreenCapturer** — GDI capture, verify window bounds at 1440p
4. **OcrEngine** — test against static screenshots of each tab
5. **ML seed pipeline** — collect seed images, train initial classifier, export ONNX
6. **HeroClassifier** — ONNX inference wrapper
7. **PseudoLabelPipeline** — thresholding, crop saving, retrain trigger
8. **InputSimulator** — test against a non-game window first
9. **HistoryScraper** — integrate all above, full tab navigation loop
10. **WPF UI** — Dashboard, Match History, Sessions views
11. **Hero Review UI** — review queue, confirm/correct flow
12. **Calibration mode** — once layout is confirmed stable
13. **HeroModelTrainer** — incremental retrain + hot-swap
