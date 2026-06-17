using System.Drawing;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.Core.Services;

/// <summary>
/// Drives OW UI navigation via Win32 SendInput (mouse clicks + key presses).
/// BattleEye-safe: all input is injected at the OS level exactly as physical hardware would
/// produce it. No interaction with the OW process or its memory.
///
/// Rules (design §6.3):
///  • Only used while OW is the foreground window (enforced by <see cref="BringOwToForeground"/>).
///  • Random jitter delay of 80–160 ms between inputs to avoid triggering any input-rate detection.
/// </summary>
public sealed class InputSimulator : IInputSimulator
{
    private readonly OverwatchWatcher _watcher;
    private readonly Random _rng = new();

    public InputSimulator(OverwatchWatcher watcher) => _watcher = watcher;

    // ── Foreground management ─────────────────────────────────────────────

    public bool BringOwToForeground()
    {
        var hwnd = _watcher.OwWindowHandle;
        if (hwnd == IntPtr.Zero) return false;

        // Retry a few times: focus reassignment from a background process can need a couple of
        // tries (OW may still be settling, or the first attempt only "primes" the next). Each
        // ForceForeground call already retries internally; this adds a longer-spaced outer loop.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (NativeMethods.ForceForeground(hwnd)) return true;
            System.Threading.Thread.Sleep(150);
        }
        return false;
    }

    // ── High-level helpers ────────────────────────────────────────────────

    public async Task PressEscapeAsync(CancellationToken ct = default) =>
        await SendKeyAsync(NativeMethods.VK_ESCAPE, ct);

    // ── Core primitives ───────────────────────────────────────────────────

    public async Task ClickAsync(Point screenPoint, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (nx, ny) = NativeMethods.ToAbsoluteMouseCoords(screenPoint);

        var inputs = new NativeMethods.INPUT[]
        {
            // Move to position
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    dx = nx, dy = ny,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                }}
            },
            // Button down
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    dwFlags = NativeMethods.MOUSEEVENTF_LEFTDOWN
                }}
            },
            // Button up
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP
                }}
            },
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        await JitterDelayAsync(ct);
    }

    public async Task ScrollAsync(Point screenPoint, int notches, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (nx, ny) = NativeMethods.ToAbsoluteMouseCoords(screenPoint);

        var inputs = new NativeMethods.INPUT[]
        {
            // Move the cursor over the scrollable control first.
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    dx = nx, dy = ny,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                }}
            },
            // Wheel scroll. mouseData is signed notches × WHEEL_DELTA (negative = down).
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    mouseData = unchecked((uint)(notches * NativeMethods.WHEEL_DELTA)),
                    dwFlags   = NativeMethods.MOUSEEVENTF_WHEEL
                }}
            },
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        await JitterDelayAsync(ct);
    }

    public async Task MoveMouseAsync(Point screenPoint, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (nx, ny) = NativeMethods.ToAbsoluteMouseCoords(screenPoint);

        var inputs = new NativeMethods.INPUT[]
        {
            new() {
                type = NativeMethods.INPUT_MOUSE,
                U = new() { mi = new() {
                    dx = nx, dy = ny,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                }}
            },
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        await JitterDelayAsync(ct);
    }

    public async Task SendKeyAsync(ushort vk, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Send as a hardware SCANCODE rather than a virtual key. OW's menu navigation responds
        // to virtual-key input, but action keys like SPACE ("select") are read at a lower level
        // and ignore virtual-key SendInput — scancodes are seen by both paths.
        var scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        var extended = vk is NativeMethods.VK_DOWN or NativeMethods.VK_HOME; // extended-key set
        var baseFlags = NativeMethods.KEYEVENTF_SCANCODE |
                        (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0);

        var down = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new() { ki = new() { wVk = 0, wScan = scan, dwFlags = baseFlags } }
        };
        var up = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new() { ki = new() { wVk = 0, wScan = scan,
                                     dwFlags = baseFlags | NativeMethods.KEYEVENTF_KEYUP } }
        };

        var size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
        NativeMethods.SendInput(1, new[] { down }, size);
        await Task.Delay(45, ct);   // brief hold so the key registers as a real press
        NativeMethods.SendInput(1, new[] { up }, size);
        await JitterDelayAsync(ct);
    }

    // ── Delay ─────────────────────────────────────────────────────────────

    private Task JitterDelayAsync(CancellationToken ct)
    {
        var ms = _rng.Next(80, 161); // 80–160 ms inclusive
        return Task.Delay(ms, ct);
    }
}
