using System.Text.Json;

namespace StarryDisplay.Core;

/// <summary>Turns a <c>GET /me/player</c> body into a <see cref="PlaybackSnapshot"/>. Defensive: Spotify omits fields freely.</summary>
public static class SpotifyPlayerParser
{
    public static PlaybackSnapshot Parse(JsonElement player)
    {
        if (player.ValueKind != JsonValueKind.Object) return PlaybackSnapshot.None;

        string? deviceName = null, deviceType = null;
        if (player.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.Object)
        {
            deviceName = Str(device, "name");
            deviceType = Str(device, "type");
        }

        var progress = Int(player, "progress_ms");
        var playing = player.TryGetProperty("is_playing", out var p) && p.ValueKind == JsonValueKind.True;
        var type = Str(player, "currently_playing_type") ?? "";

        if (type == "ad")
        {
            return new PlaybackSnapshot(PlaybackKind.Ad, "", "Advertisement", "Spotify", "", "", 0, progress, playing, deviceName, deviceType, null);
        }

        if (!player.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
        {
            // A device is active but there's no item: unknown content, or a private/local item Spotify hides.
            return new PlaybackSnapshot(PlaybackKind.Unsupported, "", "", "", "", "", 0, progress, playing, deviceName, deviceType, null);
        }

        var itemType = Str(item, "type") ?? type;
        var id = Str(item, "id") ?? Str(item, "uri") ?? "";
        var name = Str(item, "name") ?? "";
        var duration = Int(item, "duration_ms");

        if (itemType == "episode")
        {
            string show = "", publisherArt = "";
            if (item.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
            {
                show = Str(showEl, "name") ?? "";
                publisherArt = FirstImage(showEl) ?? "";
            }
            var art = FirstImage(item) ?? (publisherArt.Length > 0 ? publisherArt : null);
            return new PlaybackSnapshot(PlaybackKind.Episode, id, name, show, show, "Podcast", duration, progress, playing, deviceName, deviceType, art);
        }

        if (itemType != "track")
        {
            return new PlaybackSnapshot(PlaybackKind.Unsupported, id, name, "", "", "", duration, progress, playing, deviceName, deviceType, null);
        }

        var artists = new List<string>();
        if (item.TryGetProperty("artists", out var artistsEl) && artistsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in artistsEl.EnumerateArray())
            {
                var n = Str(a, "name");
                if (!string.IsNullOrWhiteSpace(n)) artists.Add(n);
            }
        }

        string album = "";
        string? artUrl = null;
        if (item.TryGetProperty("album", out var albumEl) && albumEl.ValueKind == JsonValueKind.Object)
        {
            album = Str(albumEl, "name") ?? "";
            artUrl = FirstImage(albumEl);
        }

        return new PlaybackSnapshot(
            PlaybackKind.Track, id, name,
            string.Join(", ", artists), artists.FirstOrDefault() ?? "",
            album, duration, progress, playing, deviceName, deviceType, artUrl);
    }

    /// <summary>Spotify lists images largest first; take the first with a URL.</summary>
    private static string? FirstImage(JsonElement owner)
    {
        if (!owner.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return null;
        foreach (var img in images.EnumerateArray())
        {
            var url = Str(img, "url");
            if (!string.IsNullOrWhiteSpace(url)) return url;
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
