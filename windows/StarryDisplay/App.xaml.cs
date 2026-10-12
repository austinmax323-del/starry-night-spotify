using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StarryDisplay.Core;
using StarryDisplay.Lyrics;
using StarryDisplay.Services;
using StarryDisplay.Views;

namespace StarryDisplay;

/// <summary>
/// Composition root and command hub: builds the services, owns settings, and routes
/// toolbar / tray / hotkey / settings-window commands.
/// </summary>
public partial class App : System.Windows.Application
{
    // One instance only: a second copy would fight over the tray, the Ctrl+Alt+L hotkey
    // and the OAuth callback port.
    private Mutex? _singleInstance;

    private SettingsStore _store = null!;
    private AppSettings _settings = new();
    private SpotifyService _spotify = null!;
    private PlaybackController _controller = null!;
    private NowPlayingViewModel _vm = null!;
    private MainWindow? _window;
    private TrayIcon _tray = null!;
    private SettingsWindow? _settingsWindow;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _connected;
    private bool _demo;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // --demo runs as its own instance so it can sit beside the real display.
        var demoArg = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        _singleInstance = new Mutex(initiallyOwned: true, demoArg ? @"Local\StarryDisplay.Demo" : @"Local\StarryDisplay.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Log.Write("Second instance started; exiting.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled UI exception (kept running).", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("Fatal exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        Log.Write($"Starting Starry Display {typeof(App).Assembly.GetName().Version}.");

        _store = new SettingsStore();
        _settings = await _store.ReadSettingsAsync();
        _spotify = new SpotifyService(_store);
        _vm = new NowPlayingViewModel();
        _demo = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        IPlaybackSource source = _demo ? new DemoPlaybackSource() : _spotify;
        ILyricsProvider lyrics = _demo ? new DemoLyricsProvider() : new CachedLyricsProvider(new LrclibLyricsProvider(), _store.DataDirectory);
        _controller = new PlaybackController(source, lyrics, new ArtworkService(), _vm);
        if (_demo)
        {
            Log.Write("Demo mode: scripted playback, no Spotify calls.");
            var layoutArg = e.Args.FirstOrDefault(a => a.StartsWith("--layout=", StringComparison.OrdinalIgnoreCase));
            if (layoutArg is not null) _settings.Layout = layoutArg["--layout=".Length..].ToLowerInvariant() == Layouts.Strip ? Layouts.Strip : Layouts.Stage;
        }

        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };

        _vm.StateChanged += (_, _) => OnStateChanged();
        _vm.ItemChanged += (_, _) => UpdateTooltip();

        _tray = new TrayIcon();
        _tray.CommandRequested += OnCommand;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SessionEnding += async (_, _) => await SaveAsync();

        _connected = _demo || await _spotify.HasTokenAsync();
        if (_connected) _controller.SignedIn();
        else _controller.SignedOut("");
        ShowLayout(_settings.Layout);
        UpdateTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    // ---- commands -----------------------------------------------------------------------

    private async void OnCommand(string command)
    {
        switch (command)
        {
            case "tray-click":
                if (_window!.IsLocked) SetLocked(false);
                else ToggleWindow();
                break;
            case "toggle":
                ToggleWindow();
                break;
            case "hide":
                _window!.Hide();
                break;
            case "layout":
                await SetLayoutAsync(_settings.Layout == Layouts.Strip ? Layouts.Stage : Layouts.Strip);
                break;
            case "layout:stage":
                await SetLayoutAsync(Layouts.Stage);
                break;
            case "layout:strip":
                await SetLayoutAsync(Layouts.Strip);
                break;
            case "topmost":
                _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
                _window!.ApplySettings(_settings);
                await SaveAsync();
                break;
            case "lock":
                SetLocked(!_window!.IsLocked);
                break;
            case "pause":
                if (_controller.IsPausedByUser) _controller.Resume(PlaybackController.PauseReason.User);
                else _controller.Pause(PlaybackController.PauseReason.User);
                break;
            case "settings":
                OpenSettings();
                break;
            case "quit":
                await QuitAsync();
                return;
        }
        UpdateTray();
        _settingsWindow?.Reload();
    }

    private void ToggleWindow()
    {
        if (_window!.IsVisible) _window!.Hide();
        else
        {
            _window!.Show();
            _window!.Activate();
        }
    }

    private void SetLocked(bool locked)
    {
        _window!.SetLocked(locked);
        Log.Write(locked ? "Click-through on." : "Click-through off.");
        UpdateTray();
    }

    private async Task SetLayoutAsync(string layout)
    {
        if (_settings.Layout == layout) return;
        RememberBounds();
        _settings.Layout = layout;
        ShowLayout(layout);
        await SaveAsync();
    }

    private void ShowLayout(string layout)
    {
        // Sign-in needs room: show the stage card while signed out, whatever layout is chosen.
        var effective = !_connected ? Layouts.Stage : layout;
        var transparent = effective == Layouts.Strip;
        var bounds = effective == Layouts.Strip ? _settings.StripBounds : _settings.StageBounds;

        if (_window is null || _window!.IsTransparent != transparent)
        {
            // The strip needs a per-pixel-transparent window, the card a normal one; WPF can't switch
            // a live window between the two, so this (and only this) swaps the window.
            var old = _window;
            var visible = old?.IsVisible ?? true;
            var locked = old?.IsLocked ?? false;
            _window = CreateWindow(transparent);
            _window!.ShowComposition(effective, _vm, _settings, bounds);
            _window!.ApplySettings(_settings);
            _window!.SetSetupVisible(!_connected);
            if (visible) _window!.Show();
            _window!.SetLocked(locked);
            old?.CloseForReal();
            return;
        }

        _window!.ShowComposition(effective, _vm, _settings, bounds);
        _window!.SetSetupVisible(!_connected);
    }

    private MainWindow CreateWindow(bool transparent)
    {
        var window = new MainWindow(transparent);
        window.CommandRequested += OnCommand;
        window.Setup.ClientId = _settings.ClientId;
        window.Setup.ConnectRequested += ConnectAsync;
        window.IsVisibleChanged += (_, _) =>
        {
            if (!ReferenceEquals(window, _window)) return; // a window being replaced
            if (window.IsVisible) _controller.Resume(PlaybackController.PauseReason.Hidden);
            else _controller.Pause(PlaybackController.PauseReason.Hidden);
            UpdateTray();
        };
        window.LocationChanged += (_, _) => QueueSave();
        window.SizeChanged += (_, _) => QueueSave();
        return window;
    }

    private void OnStateChanged()
    {
        var connected = _vm.State != DisplayState.SignedOut;
        if (connected != _connected)
        {
            _connected = connected;
            if (!connected) _window!.Setup.SetStatus(_vm.StatusDetail);
            ShowLayout(_settings.Layout);
            _settingsWindow?.RefreshAccount();
        }
        UpdateTooltip();
    }

    private async Task ConnectAsync(string clientId)
    {
        await _spotify.StartAuthAsync(clientId);
        _settings.ClientId = clientId.Trim();
        await SaveAsync();
        _connected = true;
        ShowLayout(_settings.Layout);
        _controller.SignedIn();
        _settingsWindow?.RefreshAccount();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, () => _connected, _store.DataDirectory);
        _settingsWindow.SettingsChanged += async () =>
        {
            if (_window!.Current?.Id != (_connected ? _settings.Layout : Layouts.Stage))
            {
                RememberBounds(_window!.Current?.Id);
                ShowLayout(_settings.Layout);
            }
            _window!.ApplySettings(_settings);
            Autostart.Apply(_settings.StartWithWindows);
            UpdateTray();
            await SaveAsync();
        };
        _settingsWindow.AccountAction += action =>
        {
            if (action == "signout")
            {
                _spotify.Disconnect();
                _controller.SignedOut("Signed out. Paste a Client ID to connect again.");
            }
            _window!.Show();
            _window!.Activate();
            _settingsWindow?.RefreshAccount();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private async Task QuitAsync()
    {
        await SaveAsync();
        _tray.Dispose();
        _settingsWindow?.Close();
        _window!.CloseForReal();
        Shutdown();
    }

    // ---- system events ------------------------------------------------------------------

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Nobody is looking at a locked screen: stop polling until they're back.
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Dispatcher.Invoke(() => _controller.Pause(PlaybackController.PauseReason.Locked));
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Dispatcher.Invoke(() => _controller.Resume(PlaybackController.PauseReason.Locked));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.Invoke(() => _controller.PollNow());
    }

    // ---- persistence / tray -------------------------------------------------------------

    private void QueueSave()
    {
        if (!_window!.IsLoaded) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void RememberBounds(string? layoutId = null)
    {
        if (_window!.Current is null || !_window!.IsLoaded) return;
        var id = layoutId ?? _window!.Current.Id;
        if (id == Layouts.Strip) _settings.StripBounds = _window!.Bounds;
        else _settings.StageBounds = _window!.Bounds;
    }

    private async Task SaveAsync()
    {
        if (_demo) return; // the demo never touches the real settings
        try
        {
            RememberBounds();
            await _store.WriteAsync("settings.json", _settings);
        }
        catch (Exception ex)
        {
            Log.Write("Settings save failed.", ex);
        }
    }

    private void UpdateTray() =>
        _tray?.Update(_window!.IsVisible, _settings.Layout, _settings.AlwaysOnTop, _window!.IsLocked, _controller.IsPausedByUser);

    private void UpdateTooltip()
    {
        _tray?.SetTooltip(_vm.HasItem && _vm.State is DisplayState.Playing or DisplayState.Paused
            ? $"{_vm.Title} — {_vm.Artist}"
            : $"Starry Display · {_vm.StatusTitle}");
    }
}
