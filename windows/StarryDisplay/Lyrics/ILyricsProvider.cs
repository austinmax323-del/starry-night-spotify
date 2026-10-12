namespace StarryDisplay.Lyrics;

/// <summary>What we know about the item when asking for lyrics. Provider-neutral.</summary>
public sealed record LyricsQuery(string Key, string Title, string Artist, string Album, int DurationMs);

public enum LyricsKind
{
    Synced,
    Unsynced,
    Instrumental,
    NotFound,
    /// <summary>Lookup failed (network). Not cached, retried on the next track change.</summary>
    Unavailable
}

public sealed record LyricLine(int TimeMs, string Text)
{
    /// <summary>LRC uses empty timed lines to mark instrumental breaks.</summary>
    public bool IsBreak => string.IsNullOrWhiteSpace(Text);
}

/// <summary>The only lyrics type the UI ever sees.</summary>
public sealed record LyricsDocument(LyricsKind Kind, IReadOnlyList<LyricLine> Lines, string Source)
{
    public static LyricsDocument NotFound(string source) => new(LyricsKind.NotFound, Array.Empty<LyricLine>(), source);
    public static LyricsDocument Unavailable(string source) => new(LyricsKind.Unavailable, Array.Empty<LyricLine>(), source);
    public static LyricsDocument Instrumental(string source) => new(LyricsKind.Instrumental, Array.Empty<LyricLine>(), source);
}

/// <summary>
/// A lyrics source. Implementations must be thread-safe and honour cancellation
/// (the track may change mid-lookup). Swap or chain providers without touching the UI.
/// </summary>
public interface ILyricsProvider
{
    string Name { get; }
    Task<LyricsDocument> GetAsync(LyricsQuery query, CancellationToken cancellationToken);
}
