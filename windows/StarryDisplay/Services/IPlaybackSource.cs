using StarryDisplay.Core;

namespace StarryDisplay.Services;

/// <summary>
/// Where the display learns what is playing. Today the only implementation is
/// <see cref="SpotifyService"/> (Spotify Web API polling, which sees the iPhone).
/// This is the seam for the future Mac link: a Mac-pushed source can implement
/// this interface and be preferred over polling without touching the UI.
/// </summary>
public interface IPlaybackSource
{
    /// <returns>The current playback; <see cref="PlaybackSnapshot.None"/> when no device is active.</returns>
    /// <exception cref="RateLimitedException">The source asked us to back off.</exception>
    /// <exception cref="AuthRequiredException">There's no usable sign-in.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Network failure (treated as offline).</exception>
    Task<PlaybackSnapshot> GetPlaybackAsync(CancellationToken cancellationToken = default);
}
