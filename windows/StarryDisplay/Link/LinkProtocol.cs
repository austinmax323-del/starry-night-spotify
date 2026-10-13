using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StarryDisplay.Link;

/// <summary>
/// The Mac ↔ Windows link contract (protocol v1). Pure data + validation, no UI or networking,
/// so it can be unit-tested and read as the spec. Human-readable version: /protocol/PROTOCOL.md.
///
/// Forward compatibility rules:
/// - Unknown JSON fields are ignored (never an error).
/// - Every field in a patch is optional; omitted = unchanged.
/// - New features are announced in <see cref="Capabilities"/>; clients check capabilities, not versions.
/// - Breaking changes get a new path prefix (/v2), never a silent change to /v1.
/// </summary>
public static partial class LinkProtocol
{
    public const int Version = 1;
    public const int DefaultPort = 47836;
    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxContextKeys = 32;
    public const int DefaultContextTtlSec = 600;
    public const int MaxContextTtlSec = 86_400;

    public static readonly string[] Capabilities =
    {
        "display.layout", "display.visible", "display.mode", "display.presentation",
        "overlay.alwaysOnTop", "overlay.clickThrough",
        "theme.accent", "theme.sky", "theme.scheme", "theme.lyricsScale", "theme.artworkGlow",
        "context", "events"
    };

    public static class Layouts { public const string Stage = "stage", Strip = "strip"; public static readonly string[] All = { Stage, Strip }; }
    /// <summary>live = follow Spotify; paused = stop polling (same as the tray's "Pause updates").</summary>
    public static class Modes { public const string Live = "live", Paused = "paused"; public static readonly string[] All = { Live, Paused }; }
    /// <summary>desktop = the stage card or strip (per layout); overlay = the compact gaming overlay. Added in v1 (capability "display.presentation").</summary>
    public static class Presentations { public const string Desktop = "desktop", Overlay = "overlay"; public static readonly string[] All = { Desktop, Overlay }; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // readable quotes in curl output; never rendered as HTML
    };

    [GeneratedRegex("^#?[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    /// <summary>Normalises "#abc123"/"ABC123" to "#ABC123"; null if invalid.</summary>
    public static string? NormalizeColor(string? s) =>
        s is not null && HexColor().IsMatch(s.Trim()) ? "#" + s.Trim().TrimStart('#').ToUpperInvariant() : null;

    /// <summary>Validates a patch. Returns human-readable problems; empty = OK.</summary>
    public static List<string> Validate(DisplayPatch p)
    {
        var errors = new List<string>();
        if (p.Layout is not null && !Layouts.All.Contains(p.Layout)) errors.Add($"layout must be one of: {string.Join(", ", Layouts.All)}");
        if (p.Mode is not null && !Modes.All.Contains(p.Mode)) errors.Add($"mode must be one of: {string.Join(", ", Modes.All)}");
        if (p.Presentation is not null && !Presentations.All.Contains(p.Presentation)) errors.Add($"presentation must be one of: {string.Join(", ", Presentations.All)}");
        if (p.Theme is { } t)
        {
            if (t.Accent is not null && t.Accent != "auto" && NormalizeColor(t.Accent) is null) errors.Add("theme.accent must be \"#RRGGBB\" or \"auto\"");
            if (t.Sky is { } sky)
            {
                var ok = sky.ValueKind == JsonValueKind.String && sky.GetString() == "auto"
                         || sky.ValueKind == JsonValueKind.Array && sky.GetArrayLength() == 2 && sky.EnumerateArray().All(e => NormalizeColor(e.GetString()) is not null);
                if (!ok) errors.Add("theme.sky must be [\"#RRGGBB\", \"#RRGGBB\"] (top, bottom) or \"auto\"");
            }
            if (t.LyricsScale is { } s && (double.IsNaN(s) || s < 0.7 || s > 1.6)) errors.Add("theme.lyricsScale must be between 0.7 and 1.6");
            if (t.Scheme is { Length: > 64 }) errors.Add("theme.scheme is limited to 64 characters");
        }
        return errors;
    }

    public static List<string> Validate(ContextUpdate c)
    {
        var errors = new List<string>();
        if (c.Data is null) errors.Add("data is required (use {} to clear)");
        else if (c.Data.Count > MaxContextKeys) errors.Add($"data is limited to {MaxContextKeys} keys");
        if (c.TtlSec is { } ttl && (ttl < 1 || ttl > MaxContextTtlSec)) errors.Add($"ttlSec must be between 1 and {MaxContextTtlSec}");
        if (c.Source is { Length: > 64 }) errors.Add("source is limited to 64 characters");
        return errors;
    }
}

// ---- What Windows reports -----------------------------------------------------------------

/// <summary>Current display configuration (GET /v1/state, "display" events, PATCH response).</summary>
/// <param name="Layout">Desktop layout (used when <paramref name="Presentation"/> is "desktop").</param>
/// <param name="Visible">Whether the current presentation is on screen.</param>
/// <param name="Overlay">Window behaviour of whatever is showing (desktop display or gaming overlay).</param>
/// <param name="Presentation">"desktop" or "overlay" (added later in v1; older clients ignore it).</param>
public sealed record DisplayState(string Layout, bool Visible, string Mode, OverlayState Overlay, ThemeState Theme, string Presentation = LinkProtocol.Presentations.Desktop);

/// <summary>Always-on-top and click-through of the window that's currently showing.</summary>
public sealed record OverlayState(bool AlwaysOnTop, bool ClickThrough);

/// <param name="Accent">Effective override ("#RRGGBB") or null when the accent follows the artwork.</param>
/// <param name="Sky">[top, bottom] card background tint, or null for the default night sky.</param>
public sealed record ThemeState(string? Scheme, string? Accent, string[]? Sky, double LyricsScale, bool ArtworkGlow);

/// <summary>What's playing, as far as the display knows (read-only; Spotify stays the source of truth).</summary>
public sealed record PlaybackSummary(string State, string? Kind, string? Title, string? Artist, string? Device, int PositionMs, int DurationMs);

public sealed record ContextState(string Source, Dictionary<string, JsonElement> Data, DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt);

public sealed record HealthInfo(string Name, string App, string Version, int Protocol, string[] Capabilities, bool Paired, bool Pairing, long ServerTime);

// ---- What the Mac sends -------------------------------------------------------------------

/// <summary>PATCH /v1/display. Every field optional.</summary>
public sealed class DisplayPatch
{
    public string? Layout { get; set; }
    /// <summary>"desktop" or "overlay". When omitted but <see cref="Layout"/> is set, the display returns to the desktop.</summary>
    public string? Presentation { get; set; }
    public bool? Visible { get; set; }
    public string? Mode { get; set; }
    public OverlayPatch? Overlay { get; set; }
    public ThemePatch? Theme { get; set; }
}

public sealed class OverlayPatch
{
    public bool? AlwaysOnTop { get; set; }
    public bool? ClickThrough { get; set; }
}

public sealed class ThemePatch
{
    public string? Scheme { get; set; }
    /// <summary>"#RRGGBB" or "auto" (follow artwork).</summary>
    public string? Accent { get; set; }
    /// <summary>["#top", "#bottom"] or "auto".</summary>
    public JsonElement? Sky { get; set; }
    public double? LyricsScale { get; set; }
    public bool? ArtworkGlow { get; set; }
}

/// <summary>PUT /v1/context. Free-form, small, expiring hints from the Mac (stored and surfaced; not acted on yet).</summary>
public sealed class ContextUpdate
{
    public string? Source { get; set; }
    public int? TtlSec { get; set; }
    public Dictionary<string, JsonElement>? Data { get; set; }
}

// ---- Pairing ------------------------------------------------------------------------------

public sealed class PairStartRequest { public string? Client { get; set; } }
public sealed record PairStartResponse(string PairId, string Salt, int Iterations);
public sealed class PairFinishRequest { public string? PairId { get; set; } public string? Proof { get; set; } }
public sealed record PairFinishResponse(string KeyId, string Box, string Tag, string Name);

public sealed record ErrorResponse(string Error, List<string>? Details = null);
