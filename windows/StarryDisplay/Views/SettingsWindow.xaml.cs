using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using StarryDisplay.Core;
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
