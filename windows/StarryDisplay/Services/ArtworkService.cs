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
            var art = url.StartsWith("demo:", StringComparison.Ordinal)
                ? await Task.Run(() => Demo(url), ct)
                : await Task.Run(async () => Decode(url, await Http.GetByteArrayAsync(url, ct)), ct);
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

    /// <summary>Procedural cover for <c>--demo</c>: a two-tone gradient with a soft glow.</summary>
    private static Artwork Demo(string url)
    {
        var (a, b) = url switch
        {
            "demo:teal" => (Color.FromRgb(0x18, 0xB2, 0xA8), Color.FromRgb(0x0B, 0x24, 0x47)),
            "demo:amber" => (Color.FromRgb(0xF2, 0xA5, 0x41), Color.FromRgb(0x4B, 0x1D, 0x6E)),
            _ => (Color.FromRgb(0x8E, 0x6C, 0xFF), Color.FromRgb(0x14, 0x12, 0x3A))
        };
        const int n = 256;
        var px = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var t = (x + y) / (2.0 * n);
            var dx = x - n * 0.68; var dy = y - n * 0.3;
            var glow = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy) / (n * 0.42));
            var i = (y * n + x) * 4;
            px[i] = (byte)Math.Min(255, a.B + (b.B - a.B) * t + 120 * glow * glow);
            px[i + 1] = (byte)Math.Min(255, a.G + (b.G - a.G) * t + 110 * glow * glow);
            px[i + 2] = (byte)Math.Min(255, a.R + (b.R - a.R) * t + 100 * glow * glow);
            px[i + 3] = 255;
        }
        var image = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        image.Freeze();
        var tiny = new TransformedBitmap(image, new ScaleTransform(BackdropPixels / (double)n, BackdropPixels / (double)n));
        tiny.Freeze();
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
