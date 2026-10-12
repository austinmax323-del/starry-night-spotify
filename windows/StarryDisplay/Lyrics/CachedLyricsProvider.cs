using System.IO;
using System.Text.Json;
using StarryDisplay.Services;

namespace StarryDisplay.Lyrics;

/// <summary>
/// Disk cache in front of any provider. Hits: 30 days. Misses: 12 hours (LRCLIB grows).
/// Failures (<see cref="LyricsKind.Unavailable"/>) are never cached.
/// </summary>
public sealed class CachedLyricsProvider(ILyricsProvider inner, string directory) : ILyricsProvider
{
    private const int MaxEntries = 600;
    private static readonly TimeSpan HitTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions Json = new();

    private readonly string _path = Path.Combine(directory, "lyrics-cache.v2.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, Entry>? _cache;

    public string Name => inner.Name;

    public async Task<LyricsDocument> GetAsync(LyricsQuery query, CancellationToken ct)
    {
        var cache = await LoadAsync();
        lock (cache)
        {
            if (cache.TryGetValue(query.Key, out var hit) && DateTimeOffset.UtcNow < hit.ExpiresAt)
            {
                return new LyricsDocument(hit.Kind, hit.Lines, hit.Source);
            }
        }

        var doc = await inner.GetAsync(query, ct);
        if (doc.Kind == LyricsKind.Unavailable) return doc;

        lock (cache)
        {
            cache[query.Key] = new Entry
            {
                Kind = doc.Kind,
                Lines = doc.Lines.ToList(),
                Source = doc.Source,
                ExpiresAt = DateTimeOffset.UtcNow + (doc.Kind == LyricsKind.NotFound ? MissTtl : HitTtl)
            };
            if (cache.Count > MaxEntries)
            {
                foreach (var key in cache.OrderBy(p => p.Value.ExpiresAt).Take(cache.Count - MaxEntries).Select(p => p.Key).ToList())
                    cache.Remove(key);
            }
        }
        _ = SaveAsync(); // fire-and-forget; a failed save only costs a refetch later
        return doc;
    }

    private async Task<Dictionary<string, Entry>> LoadAsync()
    {
        if (_cache is not null) return _cache;
        await _gate.WaitAsync();
        try
        {
            if (_cache is not null) return _cache;
            try
            {
                if (File.Exists(_path))
                {
                    await using var s = File.OpenRead(_path);
                    _cache = await JsonSerializer.DeserializeAsync<Dictionary<string, Entry>>(s, Json);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Lyrics cache unreadable; starting fresh.", ex);
            }
            return _cache ??= new();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync()
    {
        if (_cache is null) return;
        await _gate.WaitAsync();
        try
        {
            byte[] bytes;
            lock (_cache) bytes = JsonSerializer.SerializeToUtf8Bytes(_cache, Json);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write("Lyrics cache save failed.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public sealed class Entry
    {
        public LyricsKind Kind { get; set; }
        public List<LyricLine> Lines { get; set; } = new();
        public string Source { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
