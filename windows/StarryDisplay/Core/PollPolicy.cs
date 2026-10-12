namespace StarryDisplay.Core;

/// <summary>
/// How long to wait before the next Spotify poll. Pure function so it can be unit-tested.
/// Goal: catch track changes and seeks quickly while music plays, and stay quiet otherwise
/// (each poll is one HTTPS request; ~1 req / 3 s while playing, far less when idle).
/// </summary>
public static class PollPolicy
{
    public static readonly TimeSpan Playing = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Paused = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan IdleLong = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan EndOfTrackSlack = TimeSpan.FromMilliseconds(400);

    /// <param name="state">State after the last poll.</param>
    /// <param name="remainingMs">Time left in the current item, if playing.</param>
    /// <param name="idleFor">How long nothing has been playing.</param>
    /// <param name="consecutiveFailures">Network failures in a row (for backoff).</param>
    public static TimeSpan Next(DisplayState state, int remainingMs, TimeSpan idleFor, int consecutiveFailures)
    {
        switch (state)
        {
            case DisplayState.Playing:
            case DisplayState.Ad:
            {
                // Poll right after the track should end so the next track appears without a gap.
                if (remainingMs > 0)
                {
                    var untilEnd = TimeSpan.FromMilliseconds(remainingMs) + EndOfTrackSlack;
                    if (untilEnd < Playing) return untilEnd < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : untilEnd;
                }
                return Playing;
            }
            case DisplayState.Paused:
                return Paused;
            case DisplayState.NothingPlaying:
            case DisplayState.Unsupported:
                return idleFor > TimeSpan.FromMinutes(5) ? IdleLong : Idle;
            case DisplayState.Offline:
            {
                // 5, 10, 20, 30, 30… seconds
                var seconds = Math.Min(30, 5 * Math.Pow(2, Math.Max(0, consecutiveFailures - 1)));
                return TimeSpan.FromSeconds(seconds);
            }
            default:
                return Idle;
        }
    }
}
