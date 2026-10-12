using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StarryDisplay;

internal static class NativeMethods
{
    public const int HotKeyId = 0x5142;
    public const int WmHotKey = 0x0312;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    /// <summary>Ctrl+Alt+L, the click-through toggle. Returns false if another app owns it.</summary>
    public static bool RegisterToggleHotKey(WindowInteropHelper helper)
    {
        const uint modControl = 0x0002;
        const uint modAlt = 0x0001;
        const uint modNoRepeat = 0x4000;
        return RegisterHotKey(helper.Handle, HotKeyId, modControl | modAlt | modNoRepeat, 0x4C /* L */);
    }

    public static void UnregisterToggleHotKey(WindowInteropHelper helper) => UnregisterHotKey(helper.Handle, HotKeyId);

    /// <summary>Tool window (no Alt+Tab entry) + optional click-through (mouse goes to whatever is underneath).</summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongPtr64(hwnd, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(style));
    }

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
