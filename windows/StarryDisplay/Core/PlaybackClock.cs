using System.Diagnostics;

namespace StarryDisplay.Core;

/// <summary>
/// Local estimate of the playback head between polls. Spotify reports progress every few
/// seconds; in between we advance with a monotonic clock. Small disagreements with the
/// server are absorbed (no visible jitter), real seeks snap immediately.
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>Server/local disagreement below this is treated as network jitter, not a seek.</summary>
    public const int SnapThresholdMs = 900;

    private readonly Func<long> _nowMs;
    private long _anchorLocalMs;
    private int _anchorPositionMs;

    public PlaybackClock() : this(() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency) { }

    /// <summary>Testing hook: supply the monotonic time source.</summary>
    public PlaybackClock(Func<long> nowMs)
    {
        _nowMs = nowMs;
        _anchorLocalMs = nowMs();
    }

    public bool IsRunning { get; private set; }
    public int DurationMs { get; private set; }

    /// <summary>Raised when the position jumps (seek, track change, pause/resume) so views can resync.</summary>
    public event EventHandler? Changed;

    public int PositionMs
    {
        get
        {
            var pos = _anchorPositionMs + (IsRunning ? (int)(_nowMs() - _anchorLocalMs) : 0);
            if (DurationMs > 0) pos = Math.Min(pos, DurationMs);
            return Math.Max(0, pos);
        }
    }

    /// <summary>Resets for a new item.</summary>
    public void Reset(int positionMs, int durationMs, bool running)
    {
        DurationMs = durationMs;
        Anchor(positionMs, running);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Feeds a server report for the current item. Returns true if the view should resync.</summary>
    public bool Sync(int serverPositionMs, int durationMs, bool running)
    {
        DurationMs = durationMs;
        var local = PositionMs;
        var stateChanged = running != IsRunning;
        var drift = Math.Abs(serverPositionMs - local);

        if (stateChanged || drift >= SnapThresholdMs)
        {
            Anchor(serverPositionMs, running);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (running && serverPositionMs > local)
        {
            // Server slightly ahead: catch up halfway. Server slightly behind is expected (its value
            // was measured before the response travelled to us), so we never step backwards for it.
            Anchor(local + (serverPositionMs - local) / 2, running: true);
        }
        return false;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        Anchor(PositionMs, running: false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Anchor(int positionMs, bool running)
    {
        _anchorPositionMs = Math.Max(0, positionMs);
        _anchorLocalMs = _nowMs();
        IsRunning = running;
    }
}
