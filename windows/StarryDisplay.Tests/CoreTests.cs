using System.Text.Json;
using StarryDisplay.Core;
using StarryDisplay.Lyrics;
using Xunit;

namespace StarryDisplay.Tests;

public class PlaybackClockTests
{
    private long _now;
    private PlaybackClock Clock() => new(() => _now);

    [Fact]
    public void Advances_while_running_and_holds_when_paused()
    {
        var c = Clock();
        c.Reset(10_000, 200_000, running: true);
        _now += 1_500;
        Assert.Equal(11_500, c.PositionMs);
        c.Stop();
        _now += 5_000;
        Assert.Equal(11_500, c.PositionMs);
    }

    [Fact]
    public void Never_runs_past_the_end()
    {
        var c = Clock();
        c.Reset(199_000, 200_000, running: true);
        _now += 10_000;
        Assert.Equal(200_000, c.PositionMs);
    }

    [Fact]
    public void Small_server_lag_is_ignored_so_lyrics_never_step_backwards()
    {
        var c = Clock();
        c.Reset(10_000, 200_000, running: true);
        _now += 3_000;
        var resync = c.Sync(12_700, 200_000, running: true); // server 300 ms behind
        Assert.False(resync);
        Assert.Equal(13_000, c.PositionMs);
    }

    [Fact]
    public void Server_slightly_ahead_is_caught_up_halfway()
    {
        var c = Clock();
        c.Reset(10_000, 200_000, running: true);
        _now += 3_000;
        c.Sync(13_400, 200_000, running: true);
        Assert.Equal(13_200, c.PositionMs);
    }

    [Fact]
    public void Seek_snaps_and_raises_changed()
    {
        var c = Clock();
        var changed = 0;
        c.Changed += (_, _) => changed++;
        c.Reset(10_000, 200_000, running: true);
        _now += 3_000;
        Assert.True(c.Sync(90_000, 200_000, running: true));
        Assert.Equal(90_000, c.PositionMs);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Pause_from_server_snaps_to_server_position()
    {
        var c = Clock();
        c.Reset(10_000, 200_000, running: true);
        _now += 3_000;
        Assert.True(c.Sync(12_900, 200_000, running: false));
        Assert.False(c.IsRunning);
        Assert.Equal(12_900, c.PositionMs);
    }
}

public class PollPolicyTests
{
    [Fact]
    public void Playing_polls_every_three_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(3), PollPolicy.Next(DisplayState.Playing, 60_000, TimeSpan.Zero, 0));

    [Fact]
    public void Polls_just_after_the_track_ends()
        => Assert.Equal(TimeSpan.FromMilliseconds(1_400), PollPolicy.Next(DisplayState.Playing, 1_000, TimeSpan.Zero, 0));

    [Fact]
    public void Idle_backs_off_after_five_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), PollPolicy.Next(DisplayState.NothingPlaying, 0, TimeSpan.FromMinutes(1), 0));
        Assert.Equal(TimeSpan.FromSeconds(20), PollPolicy.Next(DisplayState.NothingPlaying, 0, TimeSpan.FromMinutes(6), 0));
    }

    [Fact]
    public void Offline_backoff_caps_at_thirty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), PollPolicy.Next(DisplayState.Offline, 0, TimeSpan.Zero, 1));
        Assert.Equal(TimeSpan.FromSeconds(20), PollPolicy.Next(DisplayState.Offline, 0, TimeSpan.Zero, 3));
        Assert.Equal(TimeSpan.FromSeconds(30), PollPolicy.Next(DisplayState.Offline, 0, TimeSpan.Zero, 9));
    }
}

public class SpotifyPlayerParserTests
{
    private static PlaybackSnapshot Parse(string json) => SpotifyPlayerParser.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Parses_a_track_on_an_iphone()
    {
        var s = Parse("""
        {"device":{"name":"iPhone","type":"Smartphone"},"progress_ms":42000,"is_playing":true,
         "currently_playing_type":"track",
         "item":{"type":"track","id":"abc","name":"Song","duration_ms":200000,
                 "artists":[{"name":"A"},{"name":"B"}],
                 "album":{"name":"Album","images":[{"url":"https://i/640"},{"url":"https://i/300"}]}}}
        """);
        Assert.Equal(PlaybackKind.Track, s.Kind);
        Assert.Equal("abc", s.Key);
        Assert.Equal("A, B", s.Artist);
        Assert.Equal("A", s.FirstArtist);
        Assert.Equal("iPhone", s.DeviceName);
        Assert.Equal("https://i/640", s.ArtworkUrl);
        Assert.True(s.IsPlaying);
        Assert.Equal(42000, s.ProgressMs);
    }

    [Fact]
    public void Parses_an_episode_with_show_name_and_art()
    {
        var s = Parse("""
        {"is_playing":false,"progress_ms":1000,"currently_playing_type":"episode",
         "item":{"type":"episode","id":"ep","name":"Ep 1","duration_ms":3600000,"images":[],
                 "show":{"name":"The Show","images":[{"url":"https://show"}]}}}
        """);
        Assert.Equal(PlaybackKind.Episode, s.Kind);
        Assert.Equal("The Show", s.Artist);
        Assert.Equal("https://show", s.ArtworkUrl);
        Assert.False(s.IsPlaying);
    }

    [Fact]
    public void Ads_are_recognised_without_an_item()
    {
        var s = Parse("""{"is_playing":true,"currently_playing_type":"ad","item":null}""");
        Assert.Equal(PlaybackKind.Ad, s.Kind);
    }

    [Fact]
    public void Active_device_without_item_is_unsupported_not_a_crash()
    {
        var s = Parse("""{"device":{"name":"PC"},"is_playing":true,"currently_playing_type":"unknown","item":null}""");
        Assert.Equal(PlaybackKind.Unsupported, s.Kind);
    }

    [Fact]
    public void Local_files_without_ids_get_a_stable_key()
    {
        var a = Parse("""{"is_playing":true,"item":{"type":"track","id":null,"name":"Local","duration_ms":1000,"artists":[{"name":"Me"}],"album":{"name":""}}}""");
        Assert.Equal(PlaybackKind.Track, a.Kind);
        Assert.False(string.IsNullOrEmpty(a.Key));
        Assert.Equal(a.Key, Parse("""{"is_playing":false,"item":{"type":"track","name":"Local","duration_ms":1000,"artists":[{"name":"Me"}],"album":{}}}""").Key);
    }
}

public class LyricsTests
{
    [Fact]
    public void Parses_lrc_variants_repeats_and_offset()
    {
        var lines = LrcParser.Parse("[offset:+500]\n[00:01.5]a\n[00:02:25]b\n[00:03.123][00:10.00]chorus\n[00:04]\nnot a line");
        Assert.Equal(new[] { 1000, 1750, 2623, 3500, 9500 }, lines.Select(l => l.TimeMs).ToArray());
        Assert.Equal("chorus", lines[^1].Text);
        Assert.True(lines[3].IsBreak);
    }

    [Theory]
    [InlineData("Come Together - Remastered 2009", "Come Together")]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song - Live at Wembley", "Song")]
    [InlineData("Song [Deluxe Edition Bonus]", "Song")]
    [InlineData("Don't Stop Me Now", "Don't Stop Me Now")]
    [InlineData("Re: Stacks - Live", "Re: Stacks")]
    public void Cleans_spotify_title_decorations(string input, string expected)
        => Assert.Equal(expected, LrclibLyricsProvider.CleanTitle(input));
}
