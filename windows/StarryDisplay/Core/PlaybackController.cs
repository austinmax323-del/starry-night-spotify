using System.Windows.Threading;
using StarryDisplay.Lyrics;
using StarryDisplay.Services;

namespace StarryDisplay.Core;

/// <summary>
/// The display's engine: polls the playback source on an adaptive schedule, turns results into
/// <see cref="DisplayState"/>, and fetches artwork + lyrics for each new item with cancellation
/// (a track change mid-lookup can never show the previous song's lyrics).
/// Runs on the UI thread; network work is awaited, never blocking.
/// </summary>
public sealed class PlaybackController
{
    /// <summary>Why polling is paused. Polling runs only when none apply.</summary>
    [Flags]
    public enum PauseReason { None = 0, Hidden = 1, Locked = 2, User = 4, SignedOut = 8 }

    private readonly IPlaybackSource _source;
    private readonly ILyricsProvider _lyrics;
    private readonly ArtworkService _artwork;
    private readonly NowPlayingViewModel _vm;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);

    private PauseReason _paused = PauseReason.Hidden;
    private bool _inFlight;
    private int _failures;
    private DateTime _idleSince = DateTime.UtcNow;
    private CancellationTokenSource? _itemCts;
    private string _itemKey = "";

    public PlaybackController(IPlaybackSource source, ILyricsProvider lyrics, ArtworkService artwork, NowPlayingViewModel vm)
    {
        _source = source;
        _lyrics = lyrics;
        _artwork = artwork;
        _vm = vm;
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await PollAsync();
        };
    }

    public NowPlayingViewModel ViewModel => _vm;
    public bool IsPausedByUser => _paused.HasFlag(PauseReason.User);

    public void Pause(PauseReason reason)
    {
        _paused |= reason;
        _timer.Stop();
    }

    public void Resume(PauseReason reason)
    {
        var wasPaused = _paused != PauseReason.None;
        _paused &= ~reason;
        if (wasPaused && _paused == PauseReason.None) Schedule(TimeSpan.Zero);
    }

    /// <summary>Poll as soon as possible (after sign-in, wake from sleep, etc.).</summary>
    public void PollNow()
    {
        if (_paused == PauseReason.None) Schedule(TimeSpan.Zero);
    }

    public void SignedIn()
    {
        _vm.SetState(DisplayState.Connecting);
        Resume(PauseReason.SignedOut);
        PollNow();
    }

    public void SignedOut(string message)
    {
        Pause(PauseReason.SignedOut);
        CancelItemWork();
        _itemKey = "";
        _vm.SetItem(PlaybackSnapshot.None);
        _vm.SetArtwork(null);
        _vm.SetLyrics(null, false);
        _vm.Clock.Reset(0, 0, false);
        _vm.SetState(DisplayState.SignedOut, message);
    }

    private void Schedule(TimeSpan delay)
    {
        if (_paused != PauseReason.None) return;
        _timer.Stop();
        _timer.Interval = delay < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : delay;
        _timer.Start();
    }

    private async Task PollAsync()
    {
        if (_inFlight || _paused != PauseReason.None) return;
        _inFlight = true;
        TimeSpan next;
        try
        {
            var snap = await _source.GetPlaybackAsync();
            _failures = 0;
            Apply(snap);
            next = PollPolicy.Next(_vm.State, Remaining(), DateTime.UtcNow - _idleSince, 0);
        }
        catch (RateLimitedException ex)
        {
            _vm.SetState(DisplayState.RateLimited);
            next = ex.RetryAfter + TimeSpan.FromSeconds(1);
        }
        catch (AuthRequiredException ex)
        {
            Log.Write($"Auth required: {ex.Message}");
            SignedOut(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _failures++;
            if (_failures == 1 || _failures % 10 == 0) Log.Write($"Playback poll failed ({_failures}x): {ex.GetType().Name}: {ex.Message}");
            // Keep showing the last item; the clock keeps running so lyrics carry on through short blips.
            if (_failures >= 2) _vm.SetState(DisplayState.Offline);
            next = PollPolicy.Next(DisplayState.Offline, 0, TimeSpan.Zero, _failures);
        }
        finally
        {
            _inFlight = false;
        }
        Schedule(next);
    }

    private int Remaining() =>
        _vm.Clock.IsRunning && _vm.Clock.DurationMs > 0 ? _vm.Clock.DurationMs - _vm.Clock.PositionMs : 0;

    private void Apply(PlaybackSnapshot snap)
    {
        switch (snap.Kind)
        {
            case PlaybackKind.NoDevice:
                if (_vm.State != DisplayState.NothingPlaying) _idleSince = DateTime.UtcNow;
                _vm.Clock.Stop();
                _vm.SetState(DisplayState.NothingPlaying);
                return;

            case PlaybackKind.Unsupported:
            case PlaybackKind.Ad:
                if (_itemKey != snap.Key)
                {
                    CancelItemWork();
                    _itemKey = snap.Key;
                    _vm.SetItem(snap);
                    _vm.SetArtwork(null);
                    _vm.SetLyrics(null, false);
                    _vm.Clock.Reset(snap.ProgressMs, snap.DurationMs, snap.IsPlaying);
                    Log.Write($"Now: {snap.Kind}.");
                }
                if (_vm.State is not (DisplayState.Ad or DisplayState.Unsupported)) _idleSince = DateTime.UtcNow;
                _vm.SetState(snap.Kind == PlaybackKind.Ad ? DisplayState.Ad : DisplayState.Unsupported);
                return;
        }

        // Track or episode.
        if (_itemKey != snap.Key)
        {
            NewItem(snap);
        }
        else
        {
            _vm.Clock.Sync(snap.ProgressMs, snap.DurationMs, snap.IsPlaying);
            _vm.RefreshItem(snap);
        }
        _vm.SetState(snap.IsPlaying ? DisplayState.Playing : DisplayState.Paused);
    }

    private void NewItem(PlaybackSnapshot snap)
    {
        CancelItemWork();
        _itemKey = snap.Key;
        var cts = _itemCts = new CancellationTokenSource();
        Log.Write($"Now: {snap.Kind} \"{snap.Title}\" — {snap.Artist} on {snap.DeviceName ?? "unknown device"}.");

        // Cached artwork (same album) applies in the same frame as the metadata: no flash.
        var cachedArt = _artwork.TryGetCached(snap.ArtworkUrl);
        _vm.Clock.Reset(snap.ProgressMs, snap.DurationMs, snap.IsPlaying);
        _vm.SetItem(snap);
        if (cachedArt is not null || snap.ArtworkUrl is null) _vm.SetArtwork(cachedArt);
        else _ = LoadArtworkAsync(snap, cts.Token);

        if (snap.Kind == PlaybackKind.Episode)
        {
            _vm.SetLyrics(LyricsDocument.NotFound("podcast"), false);
        }
        else
        {
            _vm.SetLyrics(null, loading: true);
            _ = LoadLyricsAsync(snap, cts.Token);
        }
    }

    private async Task LoadArtworkAsync(PlaybackSnapshot snap, CancellationToken ct)
    {
        try
        {
            var art = await _artwork.GetAsync(snap.ArtworkUrl, ct);
            if (!ct.IsCancellationRequested && _itemKey == snap.Key) _vm.SetArtwork(art);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadLyricsAsync(PlaybackSnapshot snap, CancellationToken ct)
    {
        var query = new LyricsQuery(snap.Key, snap.Title, snap.FirstArtist.Length > 0 ? snap.FirstArtist : snap.Artist, snap.Album, snap.DurationMs);
        try
        {
            var doc = await _lyrics.GetAsync(query, ct);
            if (doc.Kind == LyricsKind.Unavailable)
            {
                // Network blip: show the state, then retry once for this same track.
                if (_itemKey == snap.Key) _vm.SetLyrics(doc, loading: false);
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                doc = await _lyrics.GetAsync(query, ct);
            }
            if (!ct.IsCancellationRequested && _itemKey == snap.Key) _vm.SetLyrics(doc, loading: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Write("Lyrics lookup crashed.", ex);
            if (!ct.IsCancellationRequested && _itemKey == snap.Key) _vm.SetLyrics(LyricsDocument.Unavailable(_lyrics.Name), false);
        }
    }

    private void CancelItemWork()
    {
        _itemCts?.Cancel();
        _itemCts?.Dispose();
        _itemCts = null;
    }
}
