using System.Diagnostics;
using StarryDisplay.Core;
using StarryDisplay.Lyrics;

namespace StarryDisplay.Services;

/// <summary>
/// <c>StarryDisplay.exe --demo</c>: a scripted playback source for checking the display without
/// Spotify (design work, screenshots, edge cases). Cycles: a song with synced lyrics → a podcast →
/// an ad → a song without lyrics → nothing playing. Lyrics are original placeholder text.
/// </summary>
public sealed class DemoPlaybackSource : IPlaybackSource
{
    private sealed record Step(PlaybackSnapshot Snap, int LengthMs, bool PausedHalfway = false);

    private static readonly Step[] Script =
    {
        new(new PlaybackSnapshot(PlaybackKind.Track, "demo-1", "Lanterns Over the Lake", "The Night Cartographers", "The Night Cartographers",
            "Maps of Small Lights", 48_000, 0, true, "iPhone", "Smartphone", "demo:violet"), 48_000, PausedHalfway: true),
        new(new PlaybackSnapshot(PlaybackKind.Episode, "demo-2", "Episode 12: Why Night Skies Fade", "Field Notes on Light", "Field Notes on Light",
            "Podcast", 3_600_000, 61_000, true, "iPhone", "Smartphone", "demo:teal"), 14_000),
        new(new PlaybackSnapshot(PlaybackKind.Ad, "", "Advertisement", "Spotify", "", "", 0, 0, true, "iPhone", "Smartphone", null), 9_000),
        new(new PlaybackSnapshot(PlaybackKind.Track, "demo-4", "Quiet Engine", "Paper Comets", "Paper Comets",
            "Instruments for Sleeping", 22_000, 0, true, "Living Room", "Speaker", "demo:amber"), 22_000),
        new(PlaybackSnapshot.None, 10_000),
    };

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public Task<PlaybackSnapshot> GetPlaybackAsync(CancellationToken cancellationToken = default)
    {
        var total = Script.Sum(s => s.LengthMs);
        var t = (int)(_clock.ElapsedMilliseconds % total);
        foreach (var step in Script)
        {
            if (t >= step.LengthMs) { t -= step.LengthMs; continue; }
            var snap = step.Snap;
            if (snap.Kind == PlaybackKind.NoDevice) return Task.FromResult(snap);

            // Optional 4-second pause in the middle of the first song to exercise the paused state.
            var progress = snap.ProgressMs + t;
            var playing = true;
            var pauseAt = step.LengthMs / 2;
            if (step.PausedHalfway && t >= pauseAt)
            {
                playing = t >= pauseAt + 4_000;
                progress = snap.ProgressMs + (playing ? t - 4_000 : pauseAt);
            }
            return Task.FromResult(snap with { ProgressMs = progress, IsPlaying = playing });
        }
        return Task.FromResult(PlaybackSnapshot.None);
    }
}

public sealed class DemoLyricsProvider : ILyricsProvider
{
    public string Name => "demo";

    private const string Lanterns = """
        [00:03.00]We folded paper into tiny boats
        [00:07.20]and lit a candle in every one
        [00:11.40]the water carried all our little notes
        [00:15.60]out past the reeds before the night was done
        [00:19.80]
        [00:24.00]Count them drifting, one by one
        [00:27.50]a constellation on the run
        [00:31.20]if the lake remembers anything at all
        [00:35.40]let it be the light and not the fall
        [00:40.00]Lanterns over the lake
        [00:43.50]lanterns over the lake
        """;

    public async Task<LyricsDocument> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        await Task.Delay(350, cancellationToken); // feel like a network lookup
        return query.Key switch
        {
            "demo-1" => new LyricsDocument(LyricsKind.Synced, LrcParser.Parse(Lanterns), Name),
            "demo-4" => LyricsDocument.Instrumental(Name),
            _ => LyricsDocument.NotFound(Name)
        };
    }
}
