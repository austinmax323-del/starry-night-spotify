namespace StarryDisplay.Models;

// DeviceName / AlbumArtUrl added for the multi-device setup: playback normally
// happens on the iPhone, so the PC shows *where* it is playing.
public sealed record TrackInfo(
    string Id,
    string Name,
    string Artist,
    string FirstArtist,
    string Album,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    string? DeviceName = null,
    string? AlbumArtUrl = null);

public sealed record LyricLine(int Time, string Text);

public sealed class LyricsResult
{
    public List<LyricLine> Lines { get; set; } = new();
    public List<string> Plain { get; set; } = new();
    public string Source { get; set; } = "none";
}

public sealed class AppSettings
{
    // A Spotify Client ID is public by design under PKCE (no client secret exists),
    // so it lives in plain settings. Tokens do not — see SettingsStore.WriteSecretAsync.
    public string ClientId { get; set; } = "";
    public string LineMode { get; set; } = "two";
    public double FontSize { get; set; } = 38;
    public string TextColor { get; set; } = "#f6fff8";
}

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
