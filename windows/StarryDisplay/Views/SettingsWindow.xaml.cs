using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StarryDisplay.Core;
using StarryDisplay.Link;
using StarryDisplay.Services;

namespace StarryDisplay.Views;

/// <summary>Settings. Every change applies live and is saved by the app.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _isConnected;
    private bool _loading = true;

    /// <summary>Raised after any setting changes (already written into the shared settings object).</summary>
    public event Action? SettingsChanged;
    /// <summary>"connect" or "signout".</summary>
    public event Action<string>? AccountAction;

    public SettingsWindow(AppSettings settings, Func<bool> isConnected, string dataDirectory)
    {
        InitializeComponent();
        _settings = settings;
        _isConnected = isConnected;
        DataDirectory = dataDirectory;

        StageRadio.IsChecked = settings.Layout != Layouts.Strip;
        StripRadio.IsChecked = settings.Layout == Layouts.Strip;
        TopmostBox.IsChecked = settings.AlwaysOnTop;
        BackdropBox.IsChecked = settings.ArtworkBackdrop;
        AutostartBox.IsChecked = settings.StartWithWindows;
        LyricsSlider.Value = settings.LyricsScale;
        BuildAnchorGrid();
        LoadOverlay();
        VersionText.Text = $"Starry Display {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        RefreshAccount();
        _loading = false;
    }

    private string DataDirectory { get; }

    // ---- Gaming overlay ---------------------------------------------------------------------

    private Func<List<MonitorInfo>>? _monitors;
    private HotkeyService? _hotkeys;
    private readonly Dictionary<string, (TextBox Box, TextBlock Status)> _hotkeyRows = new();

    /// <summary>Connects monitor enumeration and the hotkey service (kept out of the constructor so the window stays testable by eye).</summary>
    public void AttachOverlay(Func<List<MonitorInfo>> monitors, HotkeyService hotkeys)
    {
        _monitors = monitors;
        _hotkeys = hotkeys;
        BuildHotkeyRows();
        RefreshMonitors();
        RefreshHotkeys();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            _hotkeys?.Resume(); // in case the window closed mid-recording
        };
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshMonitors);

    private void LoadOverlay()
    {
        var o = _settings.Overlay;
        var wasLoading = _loading;
        _loading = true;
        DesktopRadio.IsChecked = _settings.Presentation != Presentations.Overlay;
        OverlayRadio.IsChecked = _settings.Presentation == Presentations.Overlay;
        MinimalRadio.IsChecked = o.Density == OverlayDensities.Minimal;
        CompactRadio.IsChecked = o.Density == OverlayDensities.Compact;
        FullRadio.IsChecked = o.Density == OverlayDensities.Full;
        OverlayLyricsBox.IsChecked = o.ShowLyrics;
        OverlayArtBox.IsChecked = o.ShowArtwork;
        OverlayClickBox.IsChecked = o.ClickThrough;
        OverlayTopBox.IsChecked = o.AlwaysOnTop;
        OverlayScaleSlider.Value = o.Scale;
        OverlayOpacitySlider.Value = o.Opacity;
        HotkeysBox.IsChecked = _settings.Hotkeys.Enabled;
        foreach (RadioButton cell in AnchorGrid.Children) cell.IsChecked = (string)cell.Tag == o.Anchor;
        ResetOffsetButton.IsEnabled = o.OffsetX != 0 || o.OffsetY != 0;
        _loading = wasLoading;
    }

    private void BuildAnchorGrid()
    {
        string[] names = { "Top left", "Top", "Top right", "Left", "Centre", "Right", "Bottom left", "Bottom", "Bottom right" };
        for (var i = 0; i < OverlayAnchors.All.Length; i++)
        {
            var cell = new RadioButton { Style = (Style)FindResource("AnchorCell"), GroupName = "Anchor", Tag = OverlayAnchors.All[i], ToolTip = names[i] };
            Grid.SetColumn(cell, i % 3);
            Grid.SetRow(cell, i / 3);
            cell.Checked += (_, _) =>
            {
                if (_loading) return;
                // A new spot starts exactly there; dragging sets an offset again.
                _settings.Overlay.Anchor = (string)cell.Tag;
                _settings.Overlay.OffsetX = _settings.Overlay.OffsetY = 0;
                ResetOffsetButton.IsEnabled = false;
                SettingsChanged?.Invoke();
            };
            AnchorGrid.Children.Add(cell);
        }
    }

    private void OnResetOverlayOffset(object sender, RoutedEventArgs e)
    {
        _settings.Overlay.OffsetX = _settings.Overlay.OffsetY = 0;
        ResetOffsetButton.IsEnabled = false;
        SettingsChanged?.Invoke();
    }

    private void RefreshMonitors()
    {
        if (_monitors is null) return;
        var monitors = _monitors();
        var o = _settings.Overlay;
        var current = OverlayPlacement.Resolve(monitors, o.Monitor, o.MonitorBounds);
        MonitorPanel.Children.Clear();

        void Add(string text, string? device, PixelRect? bounds, bool isChecked, bool enabled = true)
        {
            var pill = new RadioButton { Content = text, Style = (Style)FindResource("RadioPill"), GroupName = "Monitor", IsChecked = isChecked, IsEnabled = enabled, Margin = new Thickness(0, 0, 0, 6) };
            pill.Checked += (_, _) =>
            {
                if (_loading) return;
                _settings.Overlay.Monitor = device;
                _settings.Overlay.MonitorBounds = bounds;
                SettingsChanged?.Invoke();
            };
            MonitorPanel.Children.Add(pill);
        }

        _loading = true;
        Add("Primary", null, null, o.Monitor is null);
        if (monitors.Count > 1 || o.Monitor is not null)
        {
            foreach (var m in monitors)
                Add($"{Short(m.DeviceName)} · {m.Bounds.Width}×{m.Bounds.Height}{(m.Scale != 1 ? $" · {m.Scale * 100:0}%" : "")}{(m.IsPrimary ? " (primary)" : "")}",
                    m.DeviceName, m.Bounds, o.Monitor is not null && current == m);
            if (o.Monitor is not null && current?.DeviceName != o.Monitor && !monitors.Any(m => m.Bounds == o.MonitorBounds))
                Add($"{Short(o.Monitor)} (disconnected; using the primary until it's back)", o.Monitor, o.MonitorBounds, false, enabled: false);
        }
        _loading = false;

        static string Short(string device) => device.Replace(@"\\.\DISPLAY", "Display ", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly (string Action, string Label)[] HotkeyLabels =
    {
        (HotkeyActions.Presentation, "Desktop ↔ overlay"),
        (HotkeyActions.Visibility, "Show / hide"),
        (HotkeyActions.ClickThrough, "Click-through"),
    };

    private void BuildHotkeyRows()
    {
        foreach (var (action, label) in HotkeyLabels)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var box = new TextBox
            {
                Style = (Style)FindResource("FieldBox"), Width = 160, IsReadOnly = true, IsReadOnlyCaretVisible = false,
                Cursor = Cursors.Hand, ToolTip = "Click, then press a new combination", Tag = action
            };
            // Problems ("taken by another app") go on their own line under the row, so labels never get squeezed.
            var status = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            DockPanel.SetDock(status, Dock.Bottom);
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(status);
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Label"), VerticalAlignment = VerticalAlignment.Center });
            box.GotKeyboardFocus += (_, _) => { _hotkeys?.Suspend(); box.Text = "Press keys…"; };
            box.LostKeyboardFocus += (_, _) => { _hotkeys?.Resume(); RefreshHotkeys(); };
            box.PreviewKeyDown += OnHotkeyKey;
            _hotkeyRows[action] = (box, status);
            HotkeyRows.Children.Add(row);
        }
    }

    private void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string action } box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            box.Text = Describe(mods) + "…";
            return;
        }
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }

        string gesture;
        if (key is Key.Back or Key.Delete && mods == ModifierKeys.None) gesture = "";
        else
        {
            var parsed = HotkeyGesture.Create(ToHotkeyModifiers(mods), (uint)KeyInterop.VirtualKeyFromKey(key));
            if (parsed is null)
            {
                ShowHotkeyProblem(action, "Needs Ctrl, Alt or Win plus a letter, digit or F-key");
                box.Text = "Press keys…";
                return;
            }
            gesture = parsed.ToString();
            var clash = _settings.Hotkeys.All().FirstOrDefault(h => h.Action != action && h.Gesture == gesture);
            if (clash.Action is not null)
            {
                ShowHotkeyProblem(action, "Already used for another action");
                box.Text = "Press keys…";
                return;
            }
        }
        _settings.Hotkeys.Set(action, gesture);
        SettingsChanged?.Invoke();
        Keyboard.ClearFocus(); // re-registers everything (Resume) and shows the result
    }

    private void ShowHotkeyProblem(string action, string text)
    {
        var status = _hotkeyRows[action].Status;
        status.Text = text;
        status.Visibility = Visibility.Visible;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Ctrl : 0) | (m.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
        (m.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) | (m.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Win : 0);

    private static string Describe(ModifierKeys m) => string.Join("+", new[]
    {
        m.HasFlag(ModifierKeys.Control) ? "Ctrl" : null, m.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
        m.HasFlag(ModifierKeys.Shift) ? "Shift" : null, m.HasFlag(ModifierKeys.Windows) ? "Win" : null
    }.Where(x => x is not null)) + (m == ModifierKeys.None ? "" : "+");

    /// <summary>Shows each hotkey and whether Windows gave it to us.</summary>
    public void RefreshHotkeys()
    {
        foreach (var (action, (box, status)) in _hotkeyRows)
        {
            if (box.IsKeyboardFocused) continue; // mid-recording
            var gesture = _settings.Hotkeys.Get(action);
            box.Text = string.IsNullOrEmpty(gesture) ? "Off" : gesture;
            var state = _hotkeys?.Status.GetValueOrDefault(action) ?? "";
            status.Text = state switch
            {
                "in use" => "Taken by another app; pick another",
                "invalid" => "Not a valid combination",
                "off" when _settings.Hotkeys.Enabled && !string.IsNullOrEmpty(gesture) => "",
                _ => ""
            };
            status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            status.Foreground = state == "in use" ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x9F)) : (Brush)FindResource("InkMutedBrush");
            box.IsEnabled = _settings.Hotkeys.Enabled;
        }
    }

    // ---- Mac link ---------------------------------------------------------------------------

    private Func<LinkServer?>? _link;
    private Func<bool, Task>? _setLinkEnabled;
    private Func<ContextState?>? _context;
    private readonly DispatcherTimer _linkTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool? _firewallOk;

    /// <summary>Connects the Mac link section. Kept separate so the window has no link dependency otherwise.</summary>
    public void AttachLink(Func<LinkServer?> link, Func<bool, Task> setEnabled, Func<ContextState?> context)
    {
        _link = link;
        _setLinkEnabled = setEnabled;
        _context = context;
        _linkTimer.Tick += (_, _) => RefreshLink();
        _linkTimer.Start();
        Closed += (_, _) => _linkTimer.Stop();
        _ = Task.Run(() => { var ok = LinkFirewall.RuleExists(); Dispatcher.BeginInvoke(() => { _firewallOk = ok; RefreshLink(); }); });
        RefreshLink();
    }

    /// <summary>Scrolls the gaming overlay section into view (--settings=overlay).</summary>
    public void ShowOverlaySection()
    {
        void Scroll() => DesktopRadio.BringIntoView(new Rect(0, 0, 1, 900));
        if (IsLoaded) Scroll(); else Loaded += (_, _) => Scroll();
    }

    /// <summary>Scrolls the Mac link section into view (used by --pair and "Pair a Mac" from elsewhere).</summary>
    public void ShowLinkSection()
    {
        if (IsLoaded) LinkBox.BringIntoView(new Rect(0, 0, 1, 900));
        else Loaded += (_, _) => LinkBox.BringIntoView(new Rect(0, 0, 1, 900));
    }

    private void RefreshLink()
    {
        var server = _link?.Invoke();
        var s = server?.Status;
        var enabled = _settings.LinkEnabled;
        _loading = true;
        LinkBox.IsChecked = enabled;
        _loading = false;
        LinkDetails.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        var host = Environment.MachineName.ToLowerInvariant() + ".local";
        LinkStatusText.Text = !enabled
            ? "Off. When on, a paired Mac can change the layout, visibility, overlay and theme. Spotify sign-in never leaves this PC."
            : s is null || !s.Running
                ? $"Not running. {s?.Error}"
                : (s.Endpoints.Any(e => !e.StartsWith("127."))
                      ? $"Listening on {string.Join(", ", s.Endpoints.Where(e => !e.StartsWith("127.")))} · reach it from the Mac as {host}"
                      : "Listening on this PC only")
                  + (s.Error is null ? "" : $" · {s.Error}")
                  + (s.EventClients > 0 ? $" · {s.EventClients} live connection{(s.EventClients == 1 ? "" : "s")}" : "");

        if (s?.PairingCode is { } code && s.PairingExpires is { } exp)
        {
            PairPanel.Visibility = Visibility.Visible;
            PairCommand.Text = $"starry pair {host} {code}";
            var left = exp - DateTimeOffset.UtcNow;
            PairCountdown.Text = left > TimeSpan.Zero ? $"Code {code} · expires in {(int)left.TotalMinutes}:{left.Seconds:00}" : "Expired";
        }
        else PairPanel.Visibility = Visibility.Collapsed;

        FirewallText.Text = _firewallOk switch
        {
            true => "Firewall: rule in place (this app, this port, local subnet and Tailscale only).",
            false => "Firewall: no rule yet. Your Wi-Fi is treated as Public, so the Mac can't connect until you allow it. If Portmaster is installed, allow incoming LAN connections for Starry Display there too.",
            _ => "Firewall: checking…"
        };

        DeviceList.Children.Clear();
        var devices = s?.Devices ?? Array.Empty<PairedDevice>();
        if (devices.Count == 0)
            DeviceList.Children.Add(new TextBlock { Text = "No Macs paired yet.", Style = (Style)FindResource("Hint") });
        foreach (var d in devices)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var forget = new Button { Content = "Forget", Style = (Style)FindResource("PillButton"), Height = 28, Padding = new Thickness(12, 0, 12, 0) };
            forget.Click += async (_, _) => { if (_link?.Invoke() is { } l) await l.ForgetAsync(d.KeyId); RefreshLink(); };
            DockPanel.SetDock(forget, Dock.Right);
            row.Children.Add(forget);
            var seen = d.LastSeen is { } t ? $"last seen {Ago(t)}" : "not seen since pairing";
            row.Children.Add(new TextBlock { Text = $"{d.Name}  ·  {seen}", Style = (Style)FindResource("Label"), VerticalAlignment = VerticalAlignment.Center });
            DeviceList.Children.Add(row);
        }

        var ctx = _context?.Invoke();
        ContextText.Text = ctx is null ? "" :
            $"Mac context ({ctx.Source}, expires {ctx.ExpiresAt.ToLocalTime():HH:mm}): " +
            string.Join(", ", ctx.Data.Take(6).Select(kv => $"{kv.Key}={kv.Value}"));
        ActivityText.Text = s is null || s.Recent.Count == 0 ? "Nothing yet." :
            string.Join("\n", s.Recent.Take(8).Select(a => $"{a.At:HH:mm:ss}  {a.Status}  {a.Method,-5} {a.Path,-14} {a.Peer}  {a.Note}"));
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t;
        return d.TotalSeconds < 60 ? "just now" : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} min ago" : d.TotalHours < 24 ? $"{(int)d.TotalHours} h ago" : t.ToLocalTime().ToString("MMM d");
    }

    private async void OnLinkToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _setLinkEnabled is null) return;
        LinkBox.IsEnabled = false;
        try { await _setLinkEnabled(LinkBox.IsChecked == true); }
        finally { LinkBox.IsEnabled = true; RefreshLink(); }
    }

    private void OnPair(object sender, RoutedEventArgs e)
    {
        _link?.Invoke()?.BeginPairing();
        RefreshLink();
    }

    private void OnCancelPairing(object sender, RoutedEventArgs e)
    {
        _link?.Invoke()?.CancelPairing();
        RefreshLink();
    }

    private async void OnFirewall(object sender, RoutedEventArgs e)
    {
        FirewallButton.IsEnabled = false;
        FirewallText.Text = "Waiting for the Windows permission prompt…";
        try
        {
            await LinkFirewall.CreateRuleAsync(_settings.LinkPort);
            _firewallOk = await Task.Run(LinkFirewall.RuleExists);
            if (_firewallOk == true && _link?.Invoke() is { } server) await server.RestartAsync(); // now bind the LAN
        }
        finally
        {
            FirewallButton.IsEnabled = true;
            RefreshLink();
        }
    }

    public void RefreshAccount()
    {
        var connected = _isConnected();
        AccountText.Text = connected ? "Connected" : "Not connected";
        AccountHint.Text = connected
            ? $"Client ID …{Tail(_settings.ClientId)} · read-only access to what's playing"
            : "Connect to follow what's playing on your phone or any device.";
        AccountButton.Content = connected ? "Sign out" : "Connect…";
    }

    /// <summary>Re-syncs controls after a change made elsewhere (tray, toolbar).</summary>
    public void Reload()
    {
        _loading = true;
        StageRadio.IsChecked = _settings.Layout != Layouts.Strip;
        StripRadio.IsChecked = _settings.Layout == Layouts.Strip;
        TopmostBox.IsChecked = _settings.AlwaysOnTop;
        _loading = false;
        LoadOverlay();
        RefreshMonitors();
        RefreshHotkeys();
        RefreshAccount();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.TryApplyMica(hwnd))
        {
            // Let the Mica material show through the client area.
            Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        }
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Commit();

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Commit();

    private void Commit()
    {
        if (_loading) return;
        _settings.Layout = StripRadio.IsChecked == true ? Layouts.Strip : Layouts.Stage;
        _settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        _settings.ArtworkBackdrop = BackdropBox.IsChecked == true;
        _settings.StartWithWindows = AutostartBox.IsChecked == true;
        _settings.LyricsScale = Math.Round(LyricsSlider.Value, 2);

        var o = _settings.Overlay;
        _settings.Presentation = OverlayRadio.IsChecked == true ? Presentations.Overlay : Presentations.Desktop;
        o.Density = MinimalRadio.IsChecked == true ? OverlayDensities.Minimal : FullRadio.IsChecked == true ? OverlayDensities.Full : OverlayDensities.Compact;
        o.ShowLyrics = OverlayLyricsBox.IsChecked == true;
        o.ShowArtwork = OverlayArtBox.IsChecked == true;
        o.ClickThrough = OverlayClickBox.IsChecked == true;
        o.AlwaysOnTop = OverlayTopBox.IsChecked == true;
        o.Scale = Math.Round(OverlayScaleSlider.Value, 2);
        o.Opacity = Math.Round(OverlayOpacitySlider.Value, 2);
        _settings.Hotkeys.Enabled = HotkeysBox.IsChecked == true;
        SettingsChanged?.Invoke();
        RefreshHotkeys();
    }

    private void OnAccount(object sender, RoutedEventArgs e) => AccountAction?.Invoke(_isConnected() ? "signout" : "connect");

    private void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });

    private static string Tail(string s) => s.Length <= 4 ? s : s[^4..];
}
