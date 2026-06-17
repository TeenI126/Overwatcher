using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace OwTracker.Core.Services;

/// <summary>
/// Win32 P/Invoke surface used for OS-level observation only. No interaction with the OW
/// process or its memory (BattleEye safety constraint, design §1).
/// </summary>
internal static class NativeMethods
{
    // ── Window observation ────────────────────────────────────────────────

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Enumerates all top-level windows; return false from the callback to stop.</summary>
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // ── Window management ─────────────────────────────────────────────────

    /// <summary>
    /// Brings <paramref name="hWnd"/> to the foreground. Requires a small trick (attach
    /// input thread) to reliably work when called from a background process.
    /// BattleEye-safe: this is standard Windows window management, not process injection.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    // SystemParametersInfo — used to read/zero the foreground-lock timeout so a background process
    // is allowed to reassign foreground (the standard SetForegroundWindow reliability fix).
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    internal const int  SW_SHOW    = 5;
    internal const int  SW_RESTORE = 9;

    internal const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    internal const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    internal const uint SPIF_SENDCHANGE              = 0x0002;

    // ── Input injection (SendInput) ───────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);
    internal const int SM_CXSCREEN = 0;
    internal const int SM_CYSCREEN = 1;

    internal const uint INPUT_MOUSE    = 0;
    internal const uint INPUT_KEYBOARD = 1;

    internal const uint MOUSEEVENTF_MOVE        = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN    = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP      = 0x0004;
    internal const uint MOUSEEVENTF_WHEEL       = 0x0800;
    internal const uint MOUSEEVENTF_ABSOLUTE    = 0x8000;
    internal const int  WHEEL_DELTA            = 120;   // one notch

    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP        = 0x0002;
    internal const uint KEYEVENTF_SCANCODE     = 0x0008;
    internal const uint MAPVK_VK_TO_VSC        = 0x0000;

    // Virtual key codes used by InputSimulator.
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_DOWN   = 0x28;
    internal const ushort VK_HOME   = 0x24;  // scrolls a list to the top
    internal const ushort VK_SPACE  = 0x20;  // selects the highlighted list item

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ── Structs ───────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width  => Right - Left;
        public int Height => Bottom - Top;
    }

    // ── Helper methods ────────────────────────────────────────────────────

    /// <summary>Window title of the current foreground window (empty string if none).</summary>
    internal static string GetForegroundWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        var len = GetWindowText(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Milliseconds since the last keyboard/mouse input system-wide.</summary>
    internal static long GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    /// <summary>
    /// Finds the first visible top-level window whose title satisfies <paramref name="matches"/>.
    /// Returns <see cref="IntPtr.Zero"/> if none. The predicate is shared with the foreground
    /// check (<see cref="OverwatchWatcher.IsOwTitle"/>) so background detection and session
    /// timing agree on what counts as the OW window — notably both reject the tracker's own
    /// "Overwatcher" window, which a substring match used to treat as the game.
    /// </summary>
    internal static IntPtr FindWindow(Func<string, bool> matches)
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var len = GetWindowTextLength(hwnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (matches(sb.ToString()))
            {
                result = hwnd;
                return false; // stop enumeration
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Reliably brings a window to the foreground from a background process. Windows normally
    /// only lets the *active* process reassign foreground, so a plain SetForegroundWindow from a
    /// background app silently no-ops (the old single-attempt version "worked" intermittently for
    /// exactly this reason). To satisfy the rules we:
    ///   1. zero the foreground-lock timeout (SPI_SETFOREGROUNDLOCKTIMEOUT) so the call is allowed;
    ///   2. attach our input queue to BOTH the current-foreground and the target threads, so
    ///      Windows treats the focus change as coming from the active input context;
    ///   3. SetForegroundWindow + SetActiveWindow + BringWindowToTop, retried a few times because
    ///      the change can lag and the first call after an attach often only "primes" the next.
    /// Everything restored/detached afterwards. BattleEye-safe: standard window management only,
    /// no OW process/memory interaction.
    /// </summary>
    internal static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (GetForegroundWindow() == hwnd) return true;   // already there

        // Restore if minimised, otherwise just ensure it's shown (don't minimise/restore a
        // borderless game — that can flip its display mode).
        ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);

        // 1. Temporarily zero the foreground-lock timeout.
        uint origTimeout = 0;
        var readTimeout = SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref origTimeout, 0);
        if (readTimeout)
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, SPIF_SENDCHANGE);

        // 2. Attach our input to the foreground and target threads.
        var foreHwnd     = GetForegroundWindow();
        var foreThread   = GetWindowThreadProcessId(foreHwnd, out _);
        var ourThread    = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        var attachedFore   = foreThread   != ourThread  && AttachThreadInput(ourThread, foreThread, true);
        var attachedTarget = targetThread != ourThread  && targetThread != foreThread
                                                        && AttachThreadInput(ourThread, targetThread, true);

        // 3. Reassign foreground, retrying until it takes (or we give up).
        var ok = false;
        for (var i = 0; i < 5 && !ok; i++)
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            ok = GetForegroundWindow() == hwnd;
            if (!ok) System.Threading.Thread.Sleep(60);
        }

        // Restore state.
        if (attachedTarget) AttachThreadInput(ourThread, targetThread, false);
        if (attachedFore)   AttachThreadInput(ourThread, foreThread, false);
        if (readTimeout)
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, new IntPtr((int)origTimeout), SPIF_SENDCHANGE);

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Converts a physical pixel point to the normalised 0-65535 range that
    /// <see cref="MOUSEEVENTF_ABSOLUTE"/> expects.
    /// </summary>
    internal static (int nx, int ny) ToAbsoluteMouseCoords(Point pt)
    {
        var sw = GetSystemMetrics(SM_CXSCREEN);
        var sh = GetSystemMetrics(SM_CYSCREEN);
        var nx = (int)((pt.X + 0.5) * 65535 / sw);
        var ny = (int)((pt.Y + 0.5) * 65535 / sh);
        return (nx, ny);
    }
}
