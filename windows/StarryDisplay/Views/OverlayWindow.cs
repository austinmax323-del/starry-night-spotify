using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StarryDisplay.Core;
using StarryDisplay.Services;

namespace StarryDisplay.Views;

/// <summary>
/// The gaming overlay window. Ordinary documented Win32 window behaviour only, nothing inside any game:
/// - per-pixel transparent (WPF layered window) around a small card;
/// - WS_EX_NOACTIVATE + ShowActivated=false + WM_MOUSEACTIVATE→MA_NOACTIVATE: appearing, clicking or dragging
///   it never takes focus from the game;
/// - WS_EX_TOOLWINDOW: no taskbar button, no Alt+Tab entry;
/// - WS_EX_TRANSPARENT when click-through (the default): the mouse goes straight to the game;
/// - topmost, re-asserted when another window comes to the foreground (an out-of-context WinEvent, the pattern
///   PowerToys' Always On Top uses), because borderless games sometimes raise themselves above topmost windows;
/// - placed by monitor + anchor + DPI-scaled offset, clamped onto the monitor (see <see cref="OverlayPlacement"/>),
///   and re-placed when displays change.
/// Exclusive-fullscreen games bypass the desktop compositor, so no normal window can appear over them;
/// borderless/windowed games are what this supports.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly OverlayView _view = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private OverlaySettings _settings = new();
    private IntPtr _hwnd;
    private bool _clickThrough = true;
    private bool _placing;
    private bool _allowClose;
    private IntPtr _foregroundHook;
    private NativeMethods.WinEventProc? _foregroundProc; // kept alive while hooked

    public OverlayWindow()
    {
        Title = "Starry overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        Left = Top = -20000; // off-screen until the first placement
        Topmost = true;

        _view.LayoutTransform = _scale;
        Content = _view;
        _view.CommandRequested += c => CommandRequested?.Invoke(c);

        MouseLeftButtonDown += OnMouseDown;
        SizeChanged += (_, _) => Place();
        IsVisibleChanged += (_, _) => UpdateForegroundHook();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Same command strings as the toolbar/tray (from the hover controls).</summary>
    public event Action<string>? CommandRequested;
    /// <summary>The user dragged the overlay; monitor/anchor/offset in the settings have been updated.</summary>
    public event Action? PlacementChanged;

    public bool ClickThrough => _clickThrough;

    public void Attach(NowPlayingViewModel vm, AppSettings settings)
    {
        _view.Attach(vm, settings);
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Overlay;
        _scale.ScaleX = _scale.ScaleY = _settings.Scale;
        Opacity = _settings.Opacity;
        if (Topmost != _settings.AlwaysOnTop) Topmost = _settings.AlwaysOnTop;
        _view.ApplySettings(settings);
        SetClickThrough(_settings.ClickThrough);
        UpdateForegroundHook();
        Place();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        if (_hwnd != IntPtr.Zero) NativeMethods.SetOverlayStyle(_hwnd, enabled);
        _view.SetInteractive(!enabled);
        Cursor = enabled ? null : Cursors.SizeAll;
    }

    public void CloseForReal()
    {
        _allowClose = true;
        _view.Detach();
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetOverlayStyle(_hwnd, _clickThrough);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Place();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            CommandRequested?.Invoke("hide");
            return;
        }
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Unhook();
        base.OnClosing(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WmMouseActivate:
                // A click (to drag, or on a hover control) must not pull focus from the game.
                handled = true;
                return new IntPtr(NativeMethods.MaNoActivate);
            case NativeMethods.WmDpiChanged:
            case NativeMethods.WmDisplayChange:
                // Let WPF rescale first, then put the overlay back where it belongs.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
                break;
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place); // monitor added/removed, resolution, primary changed

    // ---- placement --------------------------------------------------------------------------------

    /// <summary>Moves the overlay to its anchor on its monitor (falls back to the primary monitor if that one is gone).</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero || _placing) return;
        var monitors = NativeMethods.Monitors();
        var target = OverlayPlacement.Resolve(monitors, _settings.Monitor, _settings.MonitorBounds);
        if (target is null) return;

        _placing = true;
        try
        {
            const uint flags = NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate;
            var current = NativeMethods.WindowRect(_hwnd);
            if (OverlayPlacement.MonitorFor(monitors, current)?.DeviceName != target.DeviceName)
            {
                // Hop onto the target monitor first, so a DPI change (and WPF's rescale) happens before we
                // measure the size we anchor with.
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, target.WorkArea.X, target.WorkArea.Y, 0, 0, flags);
                current = NativeMethods.WindowRect(_hwnd);
            }
            var (x, y) = OverlayPlacement.Place(target.WorkArea, current.Width, current.Height,
                _settings.Anchor, _settings.OffsetX, _settings.OffsetY, target.Scale);
            if (x != current.X || y != current.Y) NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, flags);
        }
        finally
        {
            _placing = false;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough || e.ButtonState != MouseButtonState.Pressed) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase) return; // a hover control
        }
        try { DragMove(); } catch (InvalidOperationException) { return; }
        OnDropped();
    }

    /// <summary>Turns where the user dropped it into monitor + nearest anchor + offset, so it survives resolution and layout changes.</summary>
    private void OnDropped()
    {
        var monitors = NativeMethods.Monitors();
        var rect = NativeMethods.WindowRect(_hwnd);
        if (OverlayPlacement.MonitorFor(monitors, rect) is not { } monitor) return;
        var (anchor, dx, dy) = OverlayPlacement.FromDrop(monitor.WorkArea, rect, monitor.Scale);
        _settings.Monitor = monitor.IsPrimary ? null : monitor.DeviceName; // primary = "follow the primary"
        _settings.MonitorBounds = monitor.IsPrimary ? null : monitor.Bounds;
        _settings.Anchor = anchor;
        _settings.OffsetX = dx;
        _settings.OffsetY = dy;
        Log.Write($"Overlay moved: {monitor.DeviceName} {anchor} {dx:+0;-0;0},{dy:+0;-0;0}");
        Place();
        PlacementChanged?.Invoke();
    }

    // ---- staying on top ---------------------------------------------------------------------------

    private void UpdateForegroundHook()
    {
        var need = IsVisible && Topmost && _hwnd != IntPtr.Zero;
        if (need && _foregroundHook == IntPtr.Zero)
        {
            _foregroundProc = OnForegroundChanged;
            _foregroundHook = NativeMethods.HookForeground(_foregroundProc);
        }
        else if (!need) Unhook();
    }

    private void Unhook()
    {
        if (_foregroundHook == IntPtr.Zero) return;
        NativeMethods.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        _foregroundProc = null;
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Cheap (one SetWindowPos, a few times a minute at most) and never activates anything.
        if (IsVisible && Topmost) NativeMethods.KeepOnTop(_hwnd, true);
    }
}
