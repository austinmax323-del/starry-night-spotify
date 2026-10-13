namespace StarryDisplay.Core;

/// <summary>What Spotify says is in the player right now.</summary>
public enum PlaybackKind
{
    /// <summary>No active device: Spotify closed everywhere, or the device went away.</summary>
    NoDevice,
    Track,
    Episode,
    Ad,
    /// <summary>A device is active but reports nothing we can describe (unknown type, private item).</summary>
    Unsupported
}

/// <summary>One poll's worth of playback. Immutable; compare by value.</summary>
public sealed record PlaybackSnapshot(
    PlaybackKind Kind,
    string Id,
    string Title,
    string Artist,
    string FirstArtist,
    string Album,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    string? DeviceName,
    string? DeviceType,
    string? ArtworkUrl)
{
    public static PlaybackSnapshot None { get; } = new(PlaybackKind.NoDevice, "", "", "", "", "", 0, 0, false, null, null, null);

    public bool HasItem => Kind is PlaybackKind.Track or PlaybackKind.Episode;

    /// <summary>Identity used for "did the track change": id when present, else title+artist.</summary>
    public string Key => Kind switch
    {
        PlaybackKind.Track or PlaybackKind.Episode => string.IsNullOrEmpty(Id) ? $"{Title}\u0001{Artist}\u0001{DurationMs}" : Id,
        _ => Kind.ToString()
    };
}

/// <summary>The display's overall state, which picks what the compositions show.</summary>
public enum DisplayState
{
    SignedOut,
    Connecting,
    NothingPlaying,
    Playing,
    Paused,
    Ad,
    Unsupported,
    Offline,
    RateLimited
}

public sealed class AppSettings
{
    // A Spotify Client ID is public by design under PKCE (no client secret exists),
    // so it lives in plain settings. Tokens do not — see SettingsStore.WriteSecretAsync.
    public string ClientId { get; set; } = "";

    /// <summary>"desktop" (the stage card or strip, per <see cref="Layout"/>) or "overlay" (the gaming overlay).</summary>
    public string Presentation { get; set; } = Presentations.Desktop;
    /// <summary>Desktop layout: stage card or lyrics strip.</summary>
    public string Layout { get; set; } = Layouts.Stage;
    /// <summary>Desktop display's always-on-top (the overlay has its own in <see cref="Overlay"/>).</summary>
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>1.0 = default lyric size; scales every composition's lyric type.</summary>
    public double LyricsScale { get; set; } = 1.0;
    public bool ArtworkBackdrop { get; set; } = true;
    public bool StartWithWindows { get; set; }

    // Theme set from the Mac (or later, a picker). Null = default behaviour.
    /// <summary>Name of the Mac's Starry Night colour scheme, informational.</summary>
    public string? ThemeScheme { get; set; }
    /// <summary>"#RRGGBB" overriding the artwork accent.</summary>
    public string? AccentOverride { get; set; }
    /// <summary>Card background tint, top and bottom ("#RRGGBB").</summary>
    public string? SkyTop { get; set; }
    public string? SkyBottom { get; set; }

    /// <summary>Mac link server (off until enabled in Settings).</summary>
    public bool LinkEnabled { get; set; }
    public int LinkPort { get; set; } = Link.LinkProtocol.DefaultPort;

    public WindowBounds? StageBounds { get; set; }
    public WindowBounds? StripBounds { get; set; }

    public OverlaySettings Overlay { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Fills in anything missing or out of range (older settings files have no overlay/hotkey sections).</summary>
    public void Normalize()
    {
        Presentation = Presentations.All.Contains(Presentation) ? Presentation : Presentations.Desktop;
        Layout = Layout == Layouts.Strip ? Layouts.Strip : Layouts.Stage;
        Overlay ??= new OverlaySettings();
        Overlay.Normalize();
        Hotkeys ??= new HotkeySettings();
        foreach (var (action, gesture) in Hotkeys.All().ToList())
        {
            // Keep blanks (= off); replace anything unparseable with the canonical form or blank.
            if (gesture is null) Hotkeys.Set(action, "");
            else if (gesture.Length > 0) Hotkeys.Set(action, HotkeyGesture.Parse(gesture)?.ToString() ?? "");
        }
    }
}

public static class Layouts
{
    public const string Stage = "stage";
    public const string Strip = "strip";
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);

public sealed class TokenInfo
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public long ExpiresAt { get; set; }
}

/// <summary>Thrown when Spotify answers 429; the poller waits RetryAfter before trying again.</summary>
public sealed class RateLimitedException(TimeSpan retryAfter) : Exception($"Spotify rate limit; retrying in {retryAfter.TotalSeconds:0}s")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Thrown when there is no usable sign-in (never connected, or the refresh token was revoked).</summary>
public sealed class AuthRequiredException(string message) : Exception(message);
