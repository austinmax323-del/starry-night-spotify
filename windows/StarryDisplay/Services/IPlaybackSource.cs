using StarryDisplay.Models;

namespace StarryDisplay.Services;

/// <summary>
/// Where the display learns what is playing. Today the only implementation is
/// <see cref="SpotifyService"/> (Spotify Web API polling, which sees the iPhone).
/// This is the seam for the future Mac link: a Mac-pushed source can implement
/// this interface and be preferred over polling without touching the UI.
/// </summary>
public interface IPlaybackSource
{
    /// <returns>The current track, or null when nothing is playing.</returns>
    /// <exception cref="RateLimitedException">The source asked us to back off.</exception>
    Task<TrackInfo?> GetPlaybackAsync(CancellationToken cancellationToken = default);
}
