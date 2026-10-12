using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using StarryDisplay.Lyrics;
using StarryDisplay.Services;

namespace StarryDisplay.Core;

/// <summary>
/// Everything a composition needs, in display terms. Compositions bind to the simple
/// properties and subscribe to the *Changed events to run transitions.
/// Only <see cref="PlaybackController"/> writes to it, always on the UI thread.
/// </summary>
public sealed class NowPlayingViewModel : INotifyPropertyChanged
{
    public PlaybackClock Clock { get; } = new();

    private DisplayState _state = DisplayState.Connecting;
    private PlaybackSnapshot _item = PlaybackSnapshot.None;
    private Artwork? _artwork;
    private LyricsDocument? _lyrics;
    private bool _lyricsLoading;
    private string _deviceName = "";
    private string _signedOutMessage = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>A different track/episode/ad is now current (fires once per change, before artwork/lyrics arrive).</summary>
    public event EventHandler? ItemChanged;
    public event EventHandler? ArtworkChanged;
    public event EventHandler? LyricsChanged;
    public event EventHandler? StateChanged;

    public DisplayState State => _state;
    public PlaybackSnapshot Item => _item;
    public Artwork? Artwork => _artwork;
    public LyricsDocument? Lyrics => _lyrics;
    public bool LyricsLoading => _lyricsLoading;

    public string Title => _item.Kind == PlaybackKind.Ad ? "Advertisement" : _item.Title;
    public string Artist => _item.Kind == PlaybackKind.Ad ? "Spotify" : _item.Artist;
    public string Album => _item.Album;
    public bool IsPlaying => _state == DisplayState.Playing || (_state == DisplayState.Ad && _item.IsPlaying);
    public bool HasItem => _item.HasItem || _item.Kind == PlaybackKind.Ad;
    public Color Accent => _accentOverride ?? _artwork?.Accent ?? ArtworkService.DefaultAccent;

    private Color? _accentOverride;

    /// <summary>Theme accent from the Mac/settings; null = follow the artwork. Re-raises ArtworkChanged so views recolour.</summary>
    internal void SetAccentOverride(Color? accent)
    {
        if (_accentOverride == accent) return;
        _accentOverride = accent;
        Raise(nameof(Accent));
        ArtworkChanged?.Invoke(this, EventArgs.Empty);
    }

    public string DeviceName => _deviceName;
    public string DeviceGlyph => DeviceGlyphFor(_item.DeviceType);

    public string StatusTitle => _state switch
    {
        DisplayState.SignedOut => "Connect Spotify",
        DisplayState.Connecting => "Connecting to Spotify",
        DisplayState.NothingPlaying => "Nothing playing",
        DisplayState.Paused => "Paused",
        DisplayState.Ad => "Advertisement",
        DisplayState.Unsupported => "Playing something private",
        DisplayState.Offline => "Offline",
        DisplayState.RateLimited => "Taking a breather",
        _ => ""
    };

    public string StatusDetail => _state switch
    {
        DisplayState.SignedOut => _signedOutMessage,
        DisplayState.Connecting => "One moment…",
        DisplayState.NothingPlaying => "Start Spotify on your phone or any device.",
        DisplayState.Ad => "Spotify will be right back.",
        DisplayState.Unsupported => "Spotify isn't sharing details for this (private session or local file).",
        DisplayState.Offline => "Can't reach Spotify. Retrying…",
        DisplayState.RateLimited => "Spotify asked us to slow down. Resuming shortly.",
        _ => ""
    };

    /// <summary>True when the current state is a "message" state rather than live playback.</summary>
    public bool ShowsStatus => _state is not (DisplayState.Playing or DisplayState.Paused);

    internal void SetState(DisplayState state, string? signedOutMessage = null)
    {
        if (signedOutMessage is not null) _signedOutMessage = signedOutMessage;
        if (_state == state && signedOutMessage is null) return;
        _state = state;
        Raise(nameof(State), nameof(StatusTitle), nameof(StatusDetail), nameof(ShowsStatus), nameof(IsPlaying));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetItem(PlaybackSnapshot item)
    {
        _item = item;
        _deviceName = item.DeviceName ?? "";
        Raise(nameof(Item), nameof(Title), nameof(Artist), nameof(Album), nameof(HasItem), nameof(DeviceName), nameof(DeviceGlyph), nameof(IsPlaying));
        ItemChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Same item, fresher details (play state, device).</summary>
    internal void RefreshItem(PlaybackSnapshot item)
    {
        var deviceChanged = item.DeviceName != _item.DeviceName || item.DeviceType != _item.DeviceType;
        _item = item;
        if (deviceChanged)
        {
            _deviceName = item.DeviceName ?? "";
            Raise(nameof(DeviceName), nameof(DeviceGlyph));
        }
        Raise(nameof(IsPlaying));
    }

    internal void SetArtwork(Artwork? artwork)
    {
        if (ReferenceEquals(_artwork, artwork)) return;
        _artwork = artwork;
        Raise(nameof(Artwork), nameof(Accent));
        ArtworkChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetLyrics(LyricsDocument? lyrics, bool loading)
    {
        _lyrics = lyrics;
        _lyricsLoading = loading;
        Raise(nameof(Lyrics), nameof(LyricsLoading));
        LyricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DeviceGlyphFor(string? type) => type?.ToLowerInvariant() switch
    {
        "smartphone" => "\uE8EA",
        "computer" => "\uE7F8",
        "speaker" or "avr" or "stb" => "\uE7F5",
        "tv" or "castvideo" => "\uE7F4",
        "tablet" => "\uE70A",
        _ => "\uE8D6"
    };

    private void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
