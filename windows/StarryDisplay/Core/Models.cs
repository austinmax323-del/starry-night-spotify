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

    public string Layout { get; set; } = Layouts.Stage;
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>1.0 = default lyric size; scales every composition's lyric type.</summary>
    public double LyricsScale { get; set; } = 1.0;
    /// <summary>Opacity of the Stage card background (0.55–1).</summary>
    public double BackgroundOpacity { get; set; } = 0.92;
    public bool ArtworkBackdrop { get; set; } = true;
    public bool StartWithWindows { get; set; }

    public WindowBounds? StageBounds { get; set; }
    public WindowBounds? StripBounds { get; set; }
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
