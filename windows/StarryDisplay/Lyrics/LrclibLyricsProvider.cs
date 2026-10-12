using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StarryDisplay.Lyrics;

/// <summary>
/// Lyrics from LRCLIB (https://lrclib.net — free, no key). Strategy, cheapest first:
/// 1. <c>/api/get</c> exact signature (title, artist, album, duration).
/// 2. <c>/api/get</c> without album (Spotify album names often differ).
/// 3. <c>/api/search</c> with a cleaned title, choosing the closest duration (±3 s).
/// Requests run sequentially and stop at the first synced hit, so most tracks cost one request.
/// Search-fallback idea adapted from The-MuffinDev/LyricsOnTheGo (MIT).
/// </summary>
public sealed partial class LrclibLyricsProvider : ILyricsProvider
{
    private const double MaxDurationDeltaSeconds = 3.0;
    private static readonly HttpClient Http = CreateClient();

    public string Name => "LRCLIB";

    public async Task<LyricsDocument> GetAsync(LyricsQuery query, CancellationToken ct)
    {
        try
        {
            var seconds = Math.Round(query.DurationMs / 1000d);
            LrclibEntry? plainFallback = null;

            var exact = await GetEntryAsync(BuildGet(query.Title, query.Artist, query.Album, seconds), ct);
            if (Usable(exact, query) is { } e1) { if (e1.HasSynced || e1.Instrumental) return ToDocument(e1); plainFallback ??= e1; }

            if (!string.IsNullOrWhiteSpace(query.Album))
            {
                var noAlbum = await GetEntryAsync(BuildGet(query.Title, query.Artist, null, seconds), ct);
                if (Usable(noAlbum, query) is { } e2) { if (e2.HasSynced || e2.Instrumental) return ToDocument(e2); plainFallback ??= e2; }
            }

            var cleaned = CleanTitle(query.Title);
            var results = await SearchAsync(cleaned, query.Artist, ct);
            var best = results
                .Where(r => DurationOk(r, query))
                .OrderByDescending(r => r.HasSynced)
                .ThenBy(r => Math.Abs((r.Duration ?? 0) - query.DurationMs / 1000d))
                .FirstOrDefault();
            if (best is not null && (best.HasSynced || best.Instrumental || plainFallback is null)) return ToDocument(best);

            return plainFallback is not null ? ToDocument(plainFallback) : LyricsDocument.NotFound(Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Services.Log.Write($"LRCLIB lookup failed: {ex.GetType().Name}: {ex.Message}");
            return LyricsDocument.Unavailable(Name);
        }
    }

    /// <summary>
    /// Strips Spotify title decorations that LRCLIB entries usually lack:
    /// " - Remastered 2011", " - Live at …", " (feat. X)", " - Radio Edit", " [Deluxe]".
    /// </summary>
    public static string CleanTitle(string title)
    {
        var s = DashSuffix().Replace(title, "");
        s = Feat().Replace(s, "");
        s = DecorBrackets().Replace(s, "");
        s = Spaces().Replace(s, " ").Trim();
        return s.Length > 0 ? s : title;
    }

    [GeneratedRegex(@"\s+-\s+.*\b(remaster\w*|live|version|edit|mix|mono|stereo|acoustic|demo|session|recorded|bonus|anniversary|deluxe)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DashSuffix();

    [GeneratedRegex(@"\s*[\(\[]\s*(feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Feat();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(remaster\w*|deluxe|bonus|mono|stereo|explicit|clean)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex DecorBrackets();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    private static bool DurationOk(LrclibEntry e, LyricsQuery q) =>
        e.Duration is null || q.DurationMs <= 0 || Math.Abs(e.Duration.Value - q.DurationMs / 1000d) <= MaxDurationDeltaSeconds;

    private static LrclibEntry? Usable(LrclibEntry? e, LyricsQuery q) =>
        e is not null && (e.HasSynced || e.HasPlain || e.Instrumental) && DurationOk(e, q) ? e : null;

    private LyricsDocument ToDocument(LrclibEntry e)
    {
        if (e.Instrumental) return LyricsDocument.Instrumental(Name);
        if (e.HasSynced)
        {
            var lines = LrcParser.Parse(e.SyncedLyrics);
            if (lines.Count > 0) return new LyricsDocument(LyricsKind.Synced, lines, Name);
        }
        if (e.HasPlain)
        {
            var plain = e.PlainLyrics!.Replace("\r", "").Split('\n').Select(t => new LyricLine(-1, t.Trim())).ToList();
            // Trim leading/trailing blank lines; keep inner blanks as stanza breaks.
            while (plain.Count > 0 && plain[0].IsBreak) plain.RemoveAt(0);
            while (plain.Count > 0 && plain[^1].IsBreak) plain.RemoveAt(plain.Count - 1);
            return new LyricsDocument(LyricsKind.Unsynced, plain, Name);
        }
        return LyricsDocument.NotFound(Name);
    }

    private static string BuildGet(string title, string artist, string? album, double seconds)
    {
        var q = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}&duration={seconds:0}";
        if (!string.IsNullOrWhiteSpace(album)) q += $"&album_name={Uri.EscapeDataString(album)}";
        return $"https://lrclib.net/api/get?{q}";
    }

    private static async Task<LrclibEntry?> GetEntryAsync(string url, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<LrclibEntry>(stream, JsonOptions, ct);
    }

    private static async Task<List<LrclibEntry>> SearchAsync(string title, string artist, CancellationToken ct)
    {
        var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return new();
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<List<LrclibEntry>>(stream, JsonOptions, ct) ?? new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient CreateClient()
    {
        // LRCLIB asks clients to identify themselves with a User-Agent.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StarryDisplay/0.2 (+https://github.com/austinmax323-del/starry-night-spotify)");
        return client;
    }

    private sealed class LrclibEntry
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
        public bool HasSynced => !string.IsNullOrWhiteSpace(SyncedLyrics);
        public bool HasPlain => !string.IsNullOrWhiteSpace(PlainLyrics);
    }
}
