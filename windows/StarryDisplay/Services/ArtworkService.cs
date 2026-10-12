using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarryDisplay.Services;

/// <summary>Decoded artwork plus what the display derives from it. All bitmaps are frozen (thread-safe, cheap to share).</summary>
public sealed record Artwork(string Url, BitmapSource Image, BitmapSource Backdrop, Color Accent);

/// <summary>
/// Downloads album/episode art once, decodes off the UI thread, and derives:
/// - a tiny (28 px) copy that, stretched with high-quality scaling, becomes a soft "blurred" backdrop
///   with zero per-frame cost (no BlurEffect);
/// - an accent colour: the most saturated swatch that is neither too dark nor near-white
///   (selection heuristic from WhySoDk/spotify-lyrics-overlay AlbumColor.cs, WTFPL).
/// Keeps a small in-memory LRU; tracks on the same album hit it instantly.
/// </summary>
public sealed class ArtworkService
{
    private const int DisplayPixels = 512;
    private const int BackdropPixels = 28;
    private const int MaxCached = 16;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly LinkedList<Artwork> _lru = new();
    private readonly object _gate = new();

    public static Color DefaultAccent { get; } = Color.FromRgb(0x9F, 0xB4, 0xFF);

    public Artwork? TryGetCached(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        lock (_gate)
        {
            for (var node = _lru.First; node is not null; node = node.Next)
            {
                if (node.Value.Url != url) continue;
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value;
            }
        }
        return null;
    }

    public async Task<Artwork?> GetAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (TryGetCached(url) is { } hit) return hit;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            var art = await Task.Run(() => Decode(url, bytes), ct);
            lock (_gate)
            {
                _lru.AddFirst(art);
                while (_lru.Count > MaxCached) _lru.RemoveLast();
            }
            return art;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write($"Artwork load failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static Artwork Decode(string url, byte[] bytes)
    {
        var image = Load(bytes, DisplayPixels);
        var tiny = Load(bytes, BackdropPixels);
        var bgra = new FormatConvertedBitmap(tiny, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        return new Artwork(url, image, bgra, PickAccent(bgra));
    }

    private static BitmapSource Load(byte[] bytes, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = width; // WIC scales during decode: sharp at display size, soft at 28 px
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Coarse 4-bit-per-channel histogram over the tiny bitmap, then score swatches.</summary>
    public static Color PickAccent(BitmapSource bgra)
    {
        var w = bgra.PixelWidth;
        var h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);

        var buckets = new Dictionary<int, (long r, long g, long b, int n)>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            var key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            buckets.TryGetValue(key, out var acc);
            buckets[key] = (acc.r + r, acc.g + g, acc.b + b, acc.n + 1);
        }

        var total = w * h;
        Color? best = null;
        var bestScore = 0.0;
        foreach (var (_, acc) in buckets)
        {
            if (acc.n < Math.Max(2, total / 120)) continue; // ignore specks
            var c = Color.FromRgb((byte)(acc.r / acc.n), (byte)(acc.g / acc.n), (byte)(acc.b / acc.n));
            var (s, v) = SaturationValue(c);
            var luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (luma < 70 || luma > 225) continue;   // too dark to glow, or near-white
            var score = s * v * Math.Sqrt(acc.n);    // vivid and present, not just vivid
            if (score > bestScore) { bestScore = score; best = c; }
        }

        if (best is not { } chosen || SaturationValue(chosen).s < 0.18) return DefaultAccent;
        return Lift(chosen);
    }

    private static (double s, double v) SaturationValue(Color c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
        var min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
        return (max <= 0 ? 0 : (max - min) / max, max);
    }

    /// <summary>Accents sit on a dark card; make sure they read as light.</summary>
    private static Color Lift(Color c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max >= 200) return c;
        var k = 200.0 / Math.Max(1, (int)max);
        return Color.FromRgb((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));
    }
}
