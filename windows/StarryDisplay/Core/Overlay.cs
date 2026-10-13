namespace StarryDisplay.Core;

/// <summary>How the display is presented. Hidden is separate (a visibility flag), so hiding never forgets the mode.</summary>
public static class Presentations
{
    /// <summary>The normal desktop display: the stage card or the lyrics strip (see <see cref="Layouts"/>).</summary>
    public const string Desktop = "desktop";
    /// <summary>The compact gaming overlay: small, transparent, on top, click-through, never takes focus.</summary>
    public const string Overlay = "overlay";
    public static readonly string[] All = { Desktop, Overlay };
}

/// <summary>Where the overlay sits on its monitor. Persisted as camelCase strings.</summary>
public static class OverlayAnchors
{
    public const string TopLeft = "topLeft", TopCenter = "topCenter", TopRight = "topRight";
    public const string MiddleLeft = "middleLeft", Center = "center", MiddleRight = "middleRight";
    public const string BottomLeft = "bottomLeft", BottomCenter = "bottomCenter", BottomRight = "bottomRight";

    /// <summary>Row-major 3×3, so index = row * 3 + column.</summary>
    public static readonly string[] All = { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight };

    public static string Normalize(string? anchor) => All.FirstOrDefault(a => string.Equals(a, anchor, StringComparison.OrdinalIgnoreCase)) ?? TopRight;

    /// <summary>Column 0..2 (left, center, right) and row 0..2 (top, middle, bottom).</summary>
    public static (int Column, int Row) Cell(string anchor)
    {
        var i = Array.IndexOf(All, Normalize(anchor));
        return (i % 3, i / 3);
    }

    public static string FromCell(int column, int row) => All[Math.Clamp(row, 0, 2) * 3 + Math.Clamp(column, 0, 2)];
}

/// <summary>How much the overlay shows.</summary>
public static class OverlayDensities
{
    /// <summary>The current lyric line only (or the track when there are no synced lyrics).</summary>
    public const string Minimal = "minimal";
    /// <summary>Artwork, track and the current line.</summary>
    public const string Compact = "compact";
    /// <summary>Compact plus the next line and a progress bar.</summary>
    public const string Full = "full";
    public static readonly string[] All = { Minimal, Compact, Full };

    public static string Normalize(string? density) => All.FirstOrDefault(d => string.Equals(d, density, StringComparison.OrdinalIgnoreCase)) ?? Compact;
}

/// <summary>Gaming-overlay preferences (persisted in settings.json).</summary>
public sealed class OverlaySettings
{
    public const double MinScale = 0.6, MaxScale = 1.6, MinOpacity = 0.3, MaxOpacity = 1.0;

    /// <summary>Monitor device name (e.g. \\.\DISPLAY2); null = whichever monitor is primary.</summary>
    public string? Monitor { get; set; }
    /// <summary>That monitor's bounds when chosen: finds it again if Windows renumbers the displays.</summary>
    public PixelRect? MonitorBounds { get; set; }
    public string Anchor { get; set; } = OverlayAnchors.TopRight;
    /// <summary>Offset from the anchor in DIPs (scaled by the monitor's DPI), set by dragging the overlay.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.92;
    public bool ShowLyrics { get; set; } = true;
    public bool ShowArtwork { get; set; } = true;
    public string Density { get; set; } = OverlayDensities.Compact;
    /// <summary>On by default: an overlay over a game should never catch the mouse.</summary>
    public bool ClickThrough { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Repairs out-of-range values from a hand-edited or older settings file.</summary>
    public void Normalize()
    {
        Anchor = OverlayAnchors.Normalize(Anchor);
        Density = OverlayDensities.Normalize(Density);
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, MinScale, MaxScale) : 1.0;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, MaxOpacity) : 0.92;
        OffsetX = double.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -10_000, 10_000) : 0;
        OffsetY = double.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -10_000, 10_000) : 0;
    }
}

/// <summary>A rectangle in physical (device) pixels, as Windows reports monitors and windows.</summary>
public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public long IntersectionArea(PixelRect o)
    {
        var w = Math.Min(Right, o.Right) - Math.Max(X, o.X);
        var h = Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}

/// <param name="Scale">DPI scale (1.0 = 96 DPI, 1.5 = 144 DPI).</param>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);

/// <summary>
/// Overlay placement in physical pixels. Pure, so the multi-monitor/DPI rules are unit-tested:
/// - the overlay is anchored to one of nine points of a monitor's work area, plus a DPI-scaled offset;
/// - it is always clamped fully onto that monitor, so it can't end up off-screen;
/// - the monitor is remembered by name and by its bounds, and falls back to the primary when it's gone
///   (without forgetting the choice, so it returns when the monitor does).
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Gap between the overlay and the monitor edge at offset 0, in DIPs.</summary>
    public const double MarginDip = 24;

    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? device, PixelRect? lastBounds)
    {
        if (monitors.Count == 0) return null;
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        if (string.IsNullOrEmpty(device)) return primary;

        // Same name and same place: the common case.
        var byName = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, device, StringComparison.OrdinalIgnoreCase));
        if (byName is not null && (lastBounds is null || byName.Bounds == lastBounds)) return byName;
        // Windows renumbered the displays (DISPLAY2 → DISPLAY3) but the monitor is where it was.
        var byBounds = lastBounds is null ? null : monitors.FirstOrDefault(m => m.Bounds == lastBounds);
        if (byBounds is not null) return byBounds;
        // Same name, rearranged or resolution changed.
        return byName ?? primary;
    }

    /// <summary>Top-left of an overlay of <paramref name="width"/>×<paramref name="height"/> pixels.</summary>
    public static (int X, int Y) Place(PixelRect area, int width, int height, string anchor, double offsetXDip, double offsetYDip, double scale)
    {
        var (column, row) = OverlayAnchors.Cell(anchor);
        var margin = MarginDip * scale;
        var x = AlignedStart(area.X, area.Width, width, column, margin) + offsetXDip * scale;
        var y = AlignedStart(area.Y, area.Height, height, row, margin) + offsetYDip * scale;
        return (Clamp(x, area.X, area.Right - width), Clamp(y, area.Y, area.Bottom - height));
    }

    /// <summary>
    /// After the user drags the overlay: the anchor is the third of the monitor its centre is in, and the
    /// offset is whatever reproduces the dropped position from that anchor. Keeping the nearest anchor
    /// means a resolution change or a different monitor keeps it in the same corner.
    /// </summary>
    public static (string Anchor, double OffsetXDip, double OffsetYDip) FromDrop(PixelRect area, PixelRect window, double scale)
    {
        var cx = window.X + window.Width / 2.0 - area.X;
        var cy = window.Y + window.Height / 2.0 - area.Y;
        var column = Math.Clamp((int)(cx * 3 / Math.Max(1, area.Width)), 0, 2);
        var row = Math.Clamp((int)(cy * 3 / Math.Max(1, area.Height)), 0, 2);
        var anchor = OverlayAnchors.FromCell(column, row);
        var margin = MarginDip * scale;
        var dx = (window.X - AlignedStart(area.X, area.Width, window.Width, column, margin)) / scale;
        var dy = (window.Y - AlignedStart(area.Y, area.Height, window.Height, row, margin)) / scale;
        return (anchor, Math.Round(dx), Math.Round(dy));
    }

    /// <summary>The monitor holding most of <paramref name="window"/> (nearest-centre if none overlaps).</summary>
    public static MonitorInfo? MonitorFor(IReadOnlyList<MonitorInfo> monitors, PixelRect window)
    {
        if (monitors.Count == 0) return null;
        var best = monitors.MaxBy(m => m.Bounds.IntersectionArea(window))!;
        if (best.Bounds.IntersectionArea(window) > 0) return best;
        var cx = window.X + window.Width / 2.0;
        var cy = window.Y + window.Height / 2.0;
        return monitors.MinBy(m => Math.Pow(m.Bounds.X + m.Bounds.Width / 2.0 - cx, 2) + Math.Pow(m.Bounds.Y + m.Bounds.Height / 2.0 - cy, 2));
    }

    private static double AlignedStart(int start, int length, int size, int cell, double margin) => cell switch
    {
        0 => start + margin,
        1 => start + (length - size) / 2.0,
        _ => start + length - size - margin
    };

    /// <summary>Clamps to [min, max]; if the overlay is bigger than the area, pins it to the start.</summary>
    private static int Clamp(double value, int min, int max) => (int)Math.Round(max < min ? min : Math.Clamp(value, min, max));
}

/// <summary>What's on screen: the presentation mode plus whether the display is shown at all.</summary>
public sealed record PresentationState(string Presentation, bool Visible)
{
    public bool DesktopVisible => Visible && Presentation == Presentations.Desktop;
    public bool OverlayVisible => Visible && Presentation == Presentations.Overlay;

    /// <summary>Switching presentation always shows the result: asking for the overlay means wanting to see it.</summary>
    public PresentationState Switch(string presentation) => new(presentation, true);
    public PresentationState Toggle() => Switch(Presentation == Presentations.Overlay ? Presentations.Desktop : Presentations.Overlay);
    public PresentationState WithVisible(bool visible) => this with { Visible = visible };
}
