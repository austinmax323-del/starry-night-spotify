using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StarryDisplay.Core;
using StarryDisplay.Services;
using StarryDisplay.Views;

namespace StarryDisplay;

/// <summary>
/// The display window: borderless, transparent, resizable (8 px invisible edges), draggable anywhere,
/// optionally always-on-top and click-through. Hosts one composition at a time; switching swaps
/// content inside the same window (no window recreation). Commands go up to <see cref="App"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, IComposition> _compositions = new();
    private IComposition? _current;
    private bool _locked;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        SetupHost.Content = Setup;
        MouseEnter += (_, _) => UpdateToolbar();
        MouseLeave += (_, _) => UpdateToolbar();
    }

    /// <summary>Toolbar/hotkey commands: layout, topmost, lock, settings, hide.</summary>
    public event Action<string>? CommandRequested;

    public SetupView Setup { get; } = new();
    public IComposition? Current => _current;
    public bool IsLocked => _locked;

    public WindowBounds Bounds => WindowState == WindowState.Normal
        ? new WindowBounds(Left, Top, Width, Height)
        : new WindowBounds(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height);

    /// <summary>Shows a composition by id, moving the window to that layout's remembered (or default) bounds.</summary>
    public void ShowComposition(string id, NowPlayingViewModel vm, AppSettings settings, WindowBounds? bounds)
    {
        if (_current?.Id == id) return;
        _current?.Detach();

        if (!_compositions.TryGetValue(id, out var next))
        {
            next = CreateComposition(id);
            _compositions[id] = next;
        }
        _current = next;
        Host.Content = next.View;
        next.Attach(vm, settings);
        LayoutButton.Content = id == Layouts.Strip ? "\uE740" : "\uE73F";
        PlaceWindow(bounds, next.DefaultSize, id == Layouts.Strip);
    }

    /// <summary>Composition registry. Add new compositions here.</summary>
    private static IComposition CreateComposition(string id) => id switch
    {
        Layouts.Strip => new StripView(),
        _ => new StageView()
    };

    public void ApplySettings(AppSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        PinButton.Content = settings.AlwaysOnTop ? "\uE77A" : "\uE718";
        PinButton.ToolTip = settings.AlwaysOnTop ? "Stop staying on top" : "Always on top";
        _current?.ApplySettings(settings);
    }

    public void SetSetupVisible(bool visible)
    {
        SetupHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Host.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) NativeMethods.SetClickThrough(hwnd, locked);
        UpdateToolbar();
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        if (!NativeMethods.RegisterToggleHotKey(helper)) Log.Write("Ctrl+Alt+L is taken by another app; use the tray menu for click-through.");
        NativeMethods.SetClickThrough(helper.Handle, _locked);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // Alt+F4 or similar: hide to tray rather than exit.
            e.Cancel = true;
            CommandRequested?.Invoke("hide");
            return;
        }
        NativeMethods.UnregisterToggleHotKey(new WindowInteropHelper(this));
        base.OnClosing(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && wParam.ToInt32() == NativeMethods.HotKeyId)
        {
            CommandRequested?.Invoke("lock");
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnToolbar(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command }) CommandRequested?.Invoke(command);
    }

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ButtonState != MouseButtonState.Pressed) return;
        // Don't steal clicks meant for controls (buttons, text boxes, sliders).
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != Root; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBox or Thumb or ScrollBar) return;
        }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void UpdateToolbar()
    {
        var show = IsMouseOver && !_locked;
        Toolbar.IsHitTestVisible = show;
        Anim.To(Toolbar, OpacityProperty, show ? 1 : 0, show ? 160 : 420);
    }

    /// <summary>Restores remembered bounds if they're still on a screen; otherwise uses a sensible default.</summary>
    private void PlaceWindow(WindowBounds? b, Size defaultSize, bool bottomCentered)
    {
        var work = SystemParameters.WorkArea;
        if (b is not null && b.Width >= MinWidth && b.Height >= MinHeight && IsOnScreen(b))
        {
            Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
            return;
        }

        var w = Math.Min(defaultSize.Width, work.Width - 40);
        var h = Math.Min(defaultSize.Height, work.Height - 40);
        Width = w; Height = h;
        Left = work.Left + (work.Width - w) / 2;
        Top = bottomCentered ? work.Bottom - h - 96 : work.Top + (work.Height - h) / 2;
    }

    private static bool IsOnScreen(WindowBounds b)
    {
        // At least an 80×40 DIP piece must be inside the virtual desktop (monitors can be unplugged).
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vr = vx + SystemParameters.VirtualScreenWidth;
        var vb = vy + SystemParameters.VirtualScreenHeight;
        var ix = Math.Min(b.Left + b.Width, vr) - Math.Max(b.Left, vx);
        var iy = Math.Min(b.Top + b.Height, vb) - Math.Max(b.Top, vy);
        return ix >= 80 && iy >= 40;
    }
}
