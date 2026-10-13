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
/// toolbar / tray / hotkey / settings-window / Mac commands.
///
/// What's on screen is (presentation, visible): the desktop display (stage card or strip) or the gaming
/// overlay, shown or hidden. Every path (tray, toolbar, hotkeys, Settings, the Mac) goes through
/// <see cref="ApplyPresentation"/>, which shows the new window before hiding the old one and never activates
/// a window unless the user asked for it from the tray.
/// </summary>
public partial class App : System.Windows.Application
{
    // One instance only: a second copy would fight over the tray, the hotkeys
    // and the OAuth callback port.
    private Mutex? _singleInstance;

    private SettingsStore _store = null!;
    private AppSettings _settings = new();
    private SpotifyService _spotify = null!;
    private PlaybackController _controller = null!;
    private NowPlayingViewModel _vm = null!;
    private MainWindow? _window;
    private OverlayWindow? _overlay;
    private HotkeyService? _hotkeys;
    private TrayIcon _tray = null!;
    private SettingsWindow? _settingsWindow;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _connected;
    private bool _demo;
    /// <summary>Whether the display (in whichever presentation) should be on screen. Not persisted: it always starts shown.</summary>
    private bool _visible = true;

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
        _demo = demoArg;
        IPlaybackSource source = _demo ? new DemoPlaybackSource() : _spotify;
        ILyricsProvider lyrics = _demo ? new DemoLyricsProvider() : new CachedLyricsProvider(new LrclibLyricsProvider(), _store.DataDirectory);
        _controller = new PlaybackController(source, lyrics, new ArtworkService(), _vm);
        if (_demo)
        {
            Log.Write("Demo mode: scripted playback, no Spotify calls.");
            var layoutArg = e.Args.FirstOrDefault(a => a.StartsWith("--layout=", StringComparison.OrdinalIgnoreCase));
            if (layoutArg is not null) _settings.Layout = layoutArg["--layout=".Length..].ToLowerInvariant() == Layouts.Strip ? Layouts.Strip : Layouts.Stage;
            var presentationArg = e.Args.FirstOrDefault(a => a.StartsWith("--presentation=", StringComparison.OrdinalIgnoreCase));
            if (presentationArg is not null) _settings.Presentation = presentationArg["--presentation=".Length..].ToLowerInvariant() == Presentations.Overlay ? Presentations.Overlay : Presentations.Desktop;
            var densityArg = e.Args.FirstOrDefault(a => a.StartsWith("--density=", StringComparison.OrdinalIgnoreCase));
            if (densityArg is not null) _settings.Overlay.Density = OverlayDensities.Normalize(densityArg["--density=".Length..]);
        }

        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };

        _vm.StateChanged += (_, _) => OnStateChanged();
        _vm.ItemChanged += (_, _) => UpdateTooltip();

        _tray = new TrayIcon();
        _tray.CommandRequested += OnCommand;

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += OnCommand;
        _hotkeys.StatusChanged += () => { UpdateTray(); _settingsWindow?.RefreshHotkeys(); };
        _hotkeys.Apply(_settings.Hotkeys);

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SessionEnding += async (_, _) => await SaveAsync();

        _connected = _demo || await _spotify.HasTokenAsync();
        if (_connected) _controller.SignedIn();
        else _controller.SignedOut("");
        ShowLayout(_settings.Layout);
        ApplyPresentation();
        await InitLinkAsync();
        UpdateTray();

        // --settings[=overlay]: open Settings (scrolled to the gaming overlay section).
        var settingsArg = e.Args.FirstOrDefault(a => a.StartsWith("--settings", StringComparison.OrdinalIgnoreCase));
        if (settingsArg is not null)
        {
            OpenSettings();
            if (settingsArg.EndsWith("=overlay", StringComparison.OrdinalIgnoreCase)) _settingsWindow?.ShowOverlaySection();
        }

        // --pair: turn the link on and open Settings with a fresh pairing code.
        if (e.Args.Contains("--pair", StringComparer.OrdinalIgnoreCase) && !_demo)
        {
            if (!_settings.LinkEnabled) await SetLinkEnabledAsync(true);
            OpenSettings();
            _link?.BeginPairing();
            _settingsWindow?.ShowLinkSection();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hotkeys?.Dispose();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    // ---- what's on screen ---------------------------------------------------------------------

    /// <summary>The overlay is in use (the setup screen needs the desktop window, so not while signed out).</summary>
    private bool OverlayActive => _connected && _settings.Presentation == Presentations.Overlay;
    private string EffectivePresentation => OverlayActive ? Presentations.Overlay : Presentations.Desktop;
    private bool DisplayVisible => OverlayActive ? _overlay?.IsVisible == true : _window?.IsVisible == true;
    private bool ActiveClickThrough => OverlayActive ? _settings.Overlay.ClickThrough : _window?.IsLocked == true;
    private bool ActiveTopmost => OverlayActive ? _settings.Overlay.AlwaysOnTop : _settings.AlwaysOnTop;

    /// <summary>Shows the current presentation (if visible) and hides the other. New window first, so polling never blips.</summary>
    private void ApplyPresentation(bool activate = false)
    {
        if (_window is null) return;
        if (OverlayActive)
        {
            var overlay = EnsureOverlay();
            if (_visible) overlay.Show(); else overlay.Hide();
            _window.Hide();
        }
        else
        {
            if (_visible)
            {
                _window.Show();
                if (activate) _window.Activate();
            }
            else _window.Hide();
            _overlay?.Hide();
        }
        UpdateTray();
    }

    private OverlayWindow EnsureOverlay()
    {
        if (_overlay is not null) return _overlay;
        _overlay = new OverlayWindow();
        _overlay.CommandRequested += OnCommand;
        _overlay.PlacementChanged += () => { QueueSave(); _settingsWindow?.Reload(); };
        _overlay.IsVisibleChanged += (_, _) => OnDisplayVisibilityChanged();
        _overlay.Attach(_vm, _settings);
        return _overlay;
    }

    private void SetVisible(bool visible, bool activate = false)
    {
        _visible = visible;
        ApplyPresentation(activate);
    }

    private async Task SetPresentationAsync(string presentation)
    {
        if (_settings.Presentation == presentation && _visible) return;
        RememberBounds();
        _settings.Presentation = presentation;
        _visible = true;
        ApplyPresentation();
        Log.Write($"Presentation: {EffectivePresentation}.");
        await SaveAsync();
    }

    private void OnDisplayVisibilityChanged()
    {
        // Nobody is looking: stop polling Spotify until something is shown again.
        if (DisplayVisible) _controller.Resume(PlaybackController.PauseReason.Hidden);
        else _controller.Pause(PlaybackController.PauseReason.Hidden);
        UpdateTray();
    }

    // ---- commands -----------------------------------------------------------------------

    private async void OnCommand(string command)
    {
        switch (command)
        {
            case "tray-click":
                if (DisplayVisible && ActiveClickThrough) SetLocked(false);
                else SetVisible(!DisplayVisible, activate: true);
                break;
            case "toggle": // tray menu: the user is at the desktop, so bring it forward
                SetVisible(!DisplayVisible, activate: true);
                break;
            case HotkeyActions.Visibility: // hotkey: maybe mid-game, so never take focus
                SetVisible(!DisplayVisible);
                break;
            case "hide":
                SetVisible(false);
                break;
            case HotkeyActions.Presentation:
                await SetPresentationAsync(OverlayActive ? Presentations.Desktop : Presentations.Overlay);
                break;
            case "presentation:desktop":
                await SetPresentationAsync(Presentations.Desktop);
                break;
            case "presentation:overlay":
                await SetPresentationAsync(Presentations.Overlay);
                break;
            case "layout":
                await SetLayoutAsync(_settings.Layout == Layouts.Strip ? Layouts.Stage : Layouts.Strip);
                break;
            case "layout:stage":
                await SetLayoutAsync(Layouts.Stage);
                await SetPresentationAsync(Presentations.Desktop); // picking a desktop layout means showing it
                break;
            case "layout:strip":
                await SetLayoutAsync(Layouts.Strip);
                await SetPresentationAsync(Presentations.Desktop);
                break;
            case "topmost":
                SetTopmost(!ActiveTopmost);
                await SaveAsync();
                break;
            case HotkeyActions.ClickThrough:
                SetLocked(!ActiveClickThrough);
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

    /// <summary>Click-through for whatever is showing. The overlay's is remembered; the desktop's resets on restart (as before).</summary>
    private void SetLocked(bool locked)
    {
        if (OverlayActive)
        {
            _settings.Overlay.ClickThrough = locked;
            _overlay?.SetClickThrough(locked);
            QueueSave();
        }
        else _window!.SetLocked(locked);
        Log.Write($"Click-through {(locked ? "on" : "off")} ({EffectivePresentation}).");
        UpdateTray();
    }

    private void SetTopmost(bool topmost)
    {
        if (OverlayActive)
        {
            _settings.Overlay.AlwaysOnTop = topmost;
            _overlay?.ApplySettings(_settings);
        }
        else
        {
            _settings.AlwaysOnTop = topmost;
            _window!.ApplySettings(_settings);
        }
    }

    private async Task SetLayoutAsync(string layout)
    {
        if (_settings.Layout == layout) return;
        RememberBounds();
        _settings.Layout = layout;
        ShowLayout(layout);
        await SaveAsync();
    }

    /// <summary>Puts the desktop layout into the desktop window (swapping the window only for transparent ↔ opaque).</summary>
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
            var visible = old?.IsVisible ?? false;
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
            OnDisplayVisibilityChanged();
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
            ApplyPresentation(); // signed out: back to the desktop window for the sign-in screen
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
        ApplyPresentation();
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
        var shownPresentation = _settings.Presentation;
        _settingsWindow.SettingsChanged += async () =>
        {
            // Choosing a presentation in Settings means wanting to see it.
            if (_settings.Presentation != shownPresentation) { shownPresentation = _settings.Presentation; _visible = true; }
            if (_window!.Current?.Id != (_connected ? _settings.Layout : Layouts.Stage))
            {
                RememberBounds(_window!.Current?.Id);
                ShowLayout(_settings.Layout);
            }
            _window!.ApplySettings(_settings);
            _overlay?.ApplySettings(_settings);
            ApplyPresentation();
            _hotkeys?.Apply(_settings.Hotkeys);
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
            SetVisible(true, activate: true);
            _settingsWindow?.RefreshAccount();
        };
        _settingsWindow.AttachLink(() => _link, SetLinkEnabledAsync, GetContext);
        _settingsWindow.AttachOverlay(NativeMethods.Monitors, _hotkeys!);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private async Task QuitAsync()
    {
        await SaveAsync();
        await StopLinkAsync();
        _tray.Dispose();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _settingsWindow?.Close();
        _overlay?.CloseForReal();
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
        if (_window is null || !_window.IsLoaded && _overlay?.IsLoaded != true) return;
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

    private void UpdateTray()
    {
        if (_window is null) return;
        _tray?.Update(DisplayVisible, EffectivePresentation, _settings.Layout, ActiveTopmost, ActiveClickThrough, _controller.IsPausedByUser, _settings.Hotkeys, _hotkeys?.Status);
        PublishDisplay(); // tell a connected Mac about any change, whatever its source
    }

    private void UpdateTooltip()
    {
        _tray?.SetTooltip(_vm.HasItem && _vm.State is DisplayState.Playing or DisplayState.Paused
            ? $"{_vm.Title} — {_vm.Artist}"
            : $"Starry Display · {_vm.StatusTitle}");
    }
}
