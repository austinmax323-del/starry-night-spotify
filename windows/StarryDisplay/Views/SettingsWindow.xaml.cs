using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
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
        VersionText.Text = $"Starry Display {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        RefreshAccount();
        _loading = false;
    }

    private string DataDirectory { get; }

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
                : $"Listening on {string.Join(", ", s.Endpoints.Where(e => !e.StartsWith("127.")))} · reach it from the Mac as {host}"
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
        SettingsChanged?.Invoke();
    }

    private void OnAccount(object sender, RoutedEventArgs e) => AccountAction?.Invoke(_isConnected() ? "signout" : "connect");

    private void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });

    private static string Tail(string s) => s.Length <= 4 ? s : s[^4..];
}
