using System.Runtime.InteropServices;
using StarryDisplay.Core;

namespace StarryDisplay;

/// <summary>
/// The documented Win32 calls the display uses. Everything here acts on this app's own windows
/// (or only reads monitor layout); nothing touches another process.
/// </summary>
internal static class NativeMethods
{
    public const int WmHotKey = 0x0312;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int WmDpiChanged = 0x02E0;
    public const int WmDisplayChange = 0x007E;

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNoTopmost = new(-2);
    public const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;

    // ---- window styles --------------------------------------------------------------------------

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>Tool window (no Alt+Tab entry) + optional click-through (mouse goes to whatever is underneath).</summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled) => SetExStyle(hwnd, WsExToolWindow, enabled);

    /// <summary>
    /// The overlay: a tool window (no taskbar or Alt+Tab entry) that never activates (WS_EX_NOACTIVATE: showing
    /// it, clicking it or dragging it leaves the game focused), plus click-through when asked.
    /// </summary>
    public static void SetOverlayStyle(IntPtr hwnd, bool clickThrough) => SetExStyle(hwnd, WsExToolWindow | WsExNoActivate, clickThrough);

    private static void SetExStyle(IntPtr hwnd, long always, bool transparent)
    {
        var style = GetWindowLongPtr64(hwnd, GwlExStyle).ToInt64() | always;
        style = transparent ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(style));
    }

    // ---- position / z-order ---------------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    public static PixelRect WindowRect(IntPtr hwnd) =>
        GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : new PixelRect(0, 0, 0, 0);

    /// <summary>Re-asserts topmost without moving, sizing or activating.</summary>
    public static void KeepOnTop(IntPtr hwnd, bool topmost) =>
        SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);

    // ---- monitors --------------------------------------------------------------------------------

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>All monitors in physical pixels with their effective DPI (needs per-monitor DPI awareness, see app.manifest).</summary>
    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Rect _, IntPtr _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(h, ref info)) return true;
            var dpi = GetDpiForMonitor(h, 0 /* MDT_EFFECTIVE_DPI */, out var x, out _) == 0 && x > 0 ? x : 96u;
            list.Add(new MonitorInfo(
                info.Device,
                new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top),
                new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top),
                dpi / 96.0,
                (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // ---- foreground events (to keep the overlay above a newly focused game window) ---------------

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    public const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000, WinEventSkipOwnProcess = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    /// <summary>
    /// Notified (on this thread) when the foreground window changes. Out-of-context: Windows posts a message to
    /// us; no DLL is loaded into any other process.
    /// </summary>
    public static IntPtr HookForeground(WinEventProc proc) =>
        SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, proc, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);

    // ---- hotkeys ---------------------------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint ModNoRepeat = 0x4000;

    // ---- DWM -------------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    /// <summary>Windows 11 rounded corners + DWM shadow for a borderless opaque window.</summary>
    public static void UseRoundedCorners(IntPtr hwnd)
    {
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
    }

    /// <summary>Dark title bar + Mica backdrop (Windows 11 22H2+). Returns false when Mica isn't available.</summary>
    public static bool TryApplyMica(IntPtr hwnd)
    {
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        if (Environment.OSVersion.Version.Build < 22621) return false;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var mica = 2; // DWMSBT_MAINWINDOW
        return DwmSetWindowAttribute(hwnd, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, ref mica, sizeof(int)) == 0;
    }
}
