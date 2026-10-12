using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using StarryDisplay.Core;
using StarryDisplay.Link;
using StarryDisplay.Services;
using CoreState = StarryDisplay.Core.DisplayState;
using DisplayState = StarryDisplay.Link.DisplayState;

namespace StarryDisplay;

/// <summary>
/// The app's side of the Mac link: implements <see cref="IDisplayHost"/> over the same commands the
/// tray and toolbar use, so a change from the Mac is indistinguishable from a local one (and is saved
/// the same way). Snapshots are cached for thread-safe reads by the server.
/// </summary>
public partial class App : IDisplayHost
{
    private LinkServer? _link;
    private LinkKeyStore? _linkKeys;
    private DisplayState? _displaySnapshot;
    private PlaybackSummary _playbackSnapshot = new("signedOut", null, null, null, null, 0, 0);
    private ContextState? _context;
    private DispatcherTimer? _contextExpiry;

    public event Action? DisplayChanged;
    public event Action? PlaybackChanged;
    public event Action? ContextChanged;

    public LinkServer? MacLink => _link;

    // ---- lifecycle ------------------------------------------------------------------------

    private async Task InitLinkAsync()
    {
        _linkKeys = new LinkKeyStore(_store);
        await _linkKeys.LoadAsync();
        _vm.StateChanged += (_, _) => PublishPlayback();
        _vm.ItemChanged += (_, _) => PublishPlayback();
        _vm.Clock.Changed += (_, _) => PublishPlayback();
        ApplyThemeToViewModel();
        PublishDisplay();
        if (_settings.LinkEnabled && !_demo) await SetLinkEnabledAsync(true, interactive: false);
    }

    public Task SetLinkEnabledAsync(bool enabled) => SetLinkEnabledAsync(enabled, interactive: true);

    /// <param name="interactive">True when the user just asked for it (may show one UAC prompt); false at startup.</param>
    private async Task SetLinkEnabledAsync(bool enabled, bool interactive)
    {
        _settings.LinkEnabled = enabled;
        if (enabled && interactive && !await Task.Run(LinkFirewall.RuleExists))
        {
            // Listening on the LAN without a rule makes Windows show its broad "allow on public and
            // private networks?" prompt. Put the narrow rule in place first (one UAC prompt).
            await LinkFirewall.CreateRuleAsync(_settings.LinkPort);
            if (_link?.IsRunning == true) await _link.RestartAsync();
        }
        if (enabled)
        {
            _link ??= new LinkServer(this, _linkKeys!, _settings.LinkPort);
            await _link.StartAsync();
        }
        else if (_link is not null)
        {
            await _link.StopAsync();
        }
        await SaveAsync();
    }

    private async Task StopLinkAsync()
    {
        if (_link is not null) await _link.StopAsync();
    }

    // ---- IDisplayHost ---------------------------------------------------------------------

    public DisplayState GetDisplay() => _displaySnapshot ?? BuildDisplay();
    public PlaybackSummary GetPlayback() => _playbackSnapshot;
    public ContextState? GetContext() => _context is { } c && c.ExpiresAt > DateTimeOffset.UtcNow ? c : null;

    public Task<DisplayState> ApplyAsync(DisplayPatch patch, string actor) => Dispatcher.InvokeAsync(async () =>
    {
        var changes = new List<string>();
        var saveNeeded = false;

        if (patch.Layout is { } layout && layout != _settings.Layout)
        {
            await SetLayoutAsync(layout == LinkProtocol.Layouts.Strip ? Layouts.Strip : Layouts.Stage);
            changes.Add($"layout={layout}");
        }
        if (patch.Visible is { } visible && visible != _window!.IsVisible)
        {
            if (visible) { _window.Show(); } else { _window.Hide(); }
            changes.Add($"visible={visible}");
        }
        if (patch.Mode is { } mode && (mode == LinkProtocol.Modes.Paused) != _controller.IsPausedByUser)
        {
            if (mode == LinkProtocol.Modes.Paused) _controller.Pause(PlaybackController.PauseReason.User);
            else _controller.Resume(PlaybackController.PauseReason.User);
            changes.Add($"mode={mode}");
        }
        if (patch.Overlay is { } o)
        {
            if (o.AlwaysOnTop is { } top && top != _settings.AlwaysOnTop)
            {
                _settings.AlwaysOnTop = top;
                _window!.ApplySettings(_settings);
                saveNeeded = true;
                changes.Add($"alwaysOnTop={top}");
            }
            if (o.ClickThrough is { } through && through != _window!.IsLocked)
            {
                SetLocked(through);
                changes.Add($"clickThrough={through}");
            }
        }
        if (patch.Theme is { } t)
        {
            if (t.Scheme is not null) { _settings.ThemeScheme = t.Scheme.Length == 0 ? null : t.Scheme; saveNeeded = true; }
            if (t.Accent is not null)
            {
                _settings.AccentOverride = t.Accent == "auto" ? null : LinkProtocol.NormalizeColor(t.Accent);
                saveNeeded = true;
                changes.Add($"accent={t.Accent}");
            }
            if (t.Sky is { } sky)
            {
                if (sky.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var c = sky.EnumerateArray().Select(e => LinkProtocol.NormalizeColor(e.GetString())).ToArray();
                    (_settings.SkyTop, _settings.SkyBottom) = (c[0], c[1]);
                }
                else (_settings.SkyTop, _settings.SkyBottom) = (null, null);
                saveNeeded = true;
                changes.Add("sky");
            }
            if (t.LyricsScale is { } scale) { _settings.LyricsScale = Math.Round(scale, 2); saveNeeded = true; changes.Add($"lyricsScale={scale:0.##}"); }
            if (t.ArtworkGlow is { } glow) { _settings.ArtworkBackdrop = glow; saveNeeded = true; changes.Add($"artworkGlow={glow}"); }
            ApplyThemeToViewModel();
            _window!.ApplySettings(_settings);
        }

        if (saveNeeded) await SaveAsync();
        Log.Write($"[link] {actor}: {(changes.Count == 0 ? "no change" : string.Join(", ", changes))}");
        UpdateTray();
        _settingsWindow?.Reload();
        return GetDisplay();
    }).Task.Unwrap();

    public Task SetContextAsync(ContextState? context) => Dispatcher.InvokeAsync(() =>
    {
        _context = context;
        _contextExpiry?.Stop();
        if (context is not null)
        {
            // When the Mac goes away its context simply lapses; nothing on Windows depends on it.
            _contextExpiry = new DispatcherTimer { Interval = context.ExpiresAt - DateTimeOffset.UtcNow };
            _contextExpiry.Tick += (_, _) =>
            {
                _contextExpiry?.Stop();
                _context = null;
                Log.Write("[link] Mac context expired");
                ContextChanged?.Invoke();
            };
            _contextExpiry.Start();
        }
        ContextChanged?.Invoke();
    }).Task;

    // ---- snapshots ------------------------------------------------------------------------

    /// <summary>Called after anything that might change the display config (tray, toolbar, Mac, settings).</summary>
    private void PublishDisplay()
    {
        if (_window is null) return;
        var next = BuildDisplay();
        if (_displaySnapshot is not null && Same(_displaySnapshot, next)) return;
        _displaySnapshot = next;
        DisplayChanged?.Invoke();
    }

    private DisplayState BuildDisplay() => new(
        _settings.Layout == Layouts.Strip ? LinkProtocol.Layouts.Strip : LinkProtocol.Layouts.Stage,
        _window?.IsVisible ?? false,
        _controller.IsPausedByUser ? LinkProtocol.Modes.Paused : LinkProtocol.Modes.Live,
        new OverlayState(_settings.AlwaysOnTop, _window?.IsLocked ?? false),
        new ThemeState(_settings.ThemeScheme, _settings.AccentOverride,
            _settings.SkyTop is not null && _settings.SkyBottom is not null ? new[] { _settings.SkyTop, _settings.SkyBottom } : null,
            _settings.LyricsScale, _settings.ArtworkBackdrop));

    private static bool Same(DisplayState a, DisplayState b) =>
        System.Text.Json.JsonSerializer.Serialize(a, LinkProtocol.Json) == System.Text.Json.JsonSerializer.Serialize(b, LinkProtocol.Json);

    private void PublishPlayback()
    {
        var item = _vm.Item;
        var state = _vm.State switch
        {
            CoreState.Playing => "playing",
            CoreState.Paused => "paused",
            CoreState.NothingPlaying => "nothingPlaying",
            CoreState.Ad => "ad",
            CoreState.Unsupported => "unsupported",
            CoreState.Offline => "offline",
            CoreState.RateLimited => "rateLimited",
            CoreState.SignedOut => "signedOut",
            _ => "connecting"
        };
        var has = _vm.HasItem;
        var next = new PlaybackSummary(state, has ? item.Kind.ToString().ToLowerInvariant() : null,
            has ? _vm.Title : null, has ? _vm.Artist : null, string.IsNullOrEmpty(_vm.DeviceName) ? null : _vm.DeviceName,
            _vm.Clock.PositionMs, _vm.Clock.DurationMs);
        _playbackSnapshot = next;
        PlaybackChanged?.Invoke();
    }

    private void ApplyThemeToViewModel()
    {
        var hex = LinkProtocol.NormalizeColor(_settings.AccentOverride);
        _vm.SetAccentOverride(hex is null ? null : (Color)ColorConverter.ConvertFromString(hex));
    }
}
