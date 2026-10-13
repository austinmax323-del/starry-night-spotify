using System.Text.Json;
using StarryDisplay.Core;
using StarryDisplay.Link;
using Xunit;

namespace StarryDisplay.Tests;

public class OverlayPlacementTests
{
    // A 2560×1080 primary at 100% with the taskbar on the left (this PC), and a 4K monitor at 150% to its right.
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", new PixelRect(0, 0, 2560, 1080), new PixelRect(48, 0, 2512, 1080), 1.0, true);
    private static readonly MonitorInfo FourK = new(@"\\.\DISPLAY2", new PixelRect(2560, -500, 3840, 2160), new PixelRect(2560, -500, 3840, 2100), 1.5, false);
    private static readonly List<MonitorInfo> Both = new() { Primary, FourK };

    [Theory]
    [InlineData(OverlayAnchors.TopLeft, 48 + 24, 24)]
    [InlineData(OverlayAnchors.TopCenter, 48 + (2512 - 440) / 2, 24)]
    [InlineData(OverlayAnchors.TopRight, 2560 - 24 - 440, 24)]
    [InlineData(OverlayAnchors.Center, 48 + (2512 - 440) / 2, (1080 - 96) / 2)]
    [InlineData(OverlayAnchors.BottomRight, 2560 - 24 - 440, 1080 - 24 - 96)]
    public void Anchors_inside_the_work_area_with_a_margin(string anchor, int x, int y)
        => Assert.Equal((x, y), OverlayPlacement.Place(Primary.WorkArea, 440, 96, anchor, 0, 0, 1.0));

    [Fact]
    public void Offsets_and_margins_scale_with_dpi()
    {
        // 150%: margin 36 px, offset (-20, 10) DIP → (-30, 15) px.
        var (x, y) = OverlayPlacement.Place(FourK.WorkArea, 660, 144, OverlayAnchors.TopRight, -20, 10, 1.5);
        Assert.Equal(2560 + 3840 - 36 - 660 - 30, x);
        Assert.Equal(-500 + 36 + 15, y);
    }

    [Fact]
    public void Never_leaves_the_monitor_whatever_the_offset()
    {
        var (x, y) = OverlayPlacement.Place(Primary.WorkArea, 440, 96, OverlayAnchors.BottomRight, 5000, 5000, 1.0);
        Assert.Equal((2560 - 440, 1080 - 96), (x, y));
        (x, y) = OverlayPlacement.Place(Primary.WorkArea, 440, 96, OverlayAnchors.TopLeft, -5000, -5000, 1.0);
        Assert.Equal((48, 0), (x, y)); // not under the left taskbar
    }

    [Fact]
    public void Bigger_than_the_monitor_pins_to_the_top_left()
        => Assert.Equal((48, 0), OverlayPlacement.Place(Primary.WorkArea, 4000, 2000, OverlayAnchors.BottomRight, 0, 0, 1.0));

    [Fact]
    public void Dropping_picks_the_nearest_anchor_and_round_trips()
    {
        var dropped = new PixelRect(2000, 900, 440, 96); // bottom-right third
        var (anchor, dx, dy) = OverlayPlacement.FromDrop(Primary.WorkArea, dropped, 1.0);
        Assert.Equal(OverlayAnchors.BottomRight, anchor);
        Assert.Equal((dropped.X, dropped.Y), OverlayPlacement.Place(Primary.WorkArea, 440, 96, anchor, dx, dy, 1.0));
    }

    [Fact]
    public void Dropping_on_a_scaled_monitor_round_trips()
    {
        var dropped = new PixelRect(3000, 300, 660, 144); // left third, middle third
        var (anchor, dx, dy) = OverlayPlacement.FromDrop(FourK.WorkArea, dropped, 1.5);
        Assert.Equal(OverlayAnchors.MiddleLeft, anchor);
        var (x, y) = OverlayPlacement.Place(FourK.WorkArea, 660, 144, anchor, dx, dy, 1.5);
        Assert.InRange(x, dropped.X - 1, dropped.X + 1); // offsets are stored in whole DIPs
        Assert.InRange(y, dropped.Y - 1, dropped.Y + 1);
    }

    [Fact]
    public void Same_corner_survives_a_resolution_change()
    {
        // Anchored bottom-right on 2560×1080, then the monitor switches to 1920×1080: still bottom-right, still on screen.
        var smaller = new PixelRect(48, 0, 1872, 1080);
        var (x, y) = OverlayPlacement.Place(smaller, 440, 96, OverlayAnchors.BottomRight, -10, -10, 1.0);
        Assert.Equal((1920 - 24 - 440 - 10, 1080 - 24 - 96 - 10), (x, y));
    }

    [Fact]
    public void Resolves_the_chosen_monitor_or_falls_back_to_primary()
    {
        Assert.Same(Primary, OverlayPlacement.Resolve(Both, null, null));
        Assert.Same(FourK, OverlayPlacement.Resolve(Both, @"\\.\DISPLAY2", FourK.Bounds));
        // Unplugged: primary, without the caller forgetting the choice.
        Assert.Same(Primary, OverlayPlacement.Resolve(new List<MonitorInfo> { Primary }, @"\\.\DISPLAY2", FourK.Bounds));
        Assert.Null(OverlayPlacement.Resolve(new List<MonitorInfo>(), null, null));
    }

    [Fact]
    public void Finds_a_renumbered_monitor_by_its_bounds()
    {
        var renumbered = FourK with { DeviceName = @"\\.\DISPLAY3" };
        var monitors = new List<MonitorInfo> { Primary, renumbered };
        Assert.Same(renumbered, OverlayPlacement.Resolve(monitors, @"\\.\DISPLAY2", FourK.Bounds));
    }

    [Fact]
    public void Follows_the_primary_when_it_changes()
    {
        var swapped = new List<MonitorInfo> { Primary with { IsPrimary = false }, FourK with { IsPrimary = true } };
        Assert.Equal(@"\\.\DISPLAY2", OverlayPlacement.Resolve(swapped, null, null)!.DeviceName);
    }

    [Fact]
    public void Monitor_for_a_window_is_the_one_holding_most_of_it()
    {
        Assert.Same(FourK, OverlayPlacement.MonitorFor(Both, new PixelRect(2500, 0, 440, 96)));
        Assert.Same(Primary, OverlayPlacement.MonitorFor(Both, new PixelRect(-20000, -20000, 440, 96))); // off-screen → nearest
    }

    [Fact]
    public void Presentation_state_switches_and_shows()
    {
        var hidden = new PresentationState(Presentations.Desktop, false);
        var overlay = hidden.Toggle();
        Assert.True(overlay.OverlayVisible);
        Assert.False(overlay.DesktopVisible);
        Assert.False(overlay.WithVisible(false).OverlayVisible);
        Assert.True(overlay.Toggle().DesktopVisible);
    }
}

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+O", "Ctrl+Alt+O")]
    [InlineData("alt + control + l", "Ctrl+Alt+L")]
    [InlineData("Win+Shift+F12", "Shift+Win+F12")]
    [InlineData("Ctrl+Alt+PageUp", "Ctrl+Alt+PageUp")]
    [InlineData("Ctrl+Num5", "Ctrl+Num5")]
    [InlineData("Ctrl+Alt+7", "Ctrl+Alt+7")]
    public void Parses_and_prints_canonically(string input, string expected)
        => Assert.Equal(expected, HotkeyGesture.Parse(input)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("O")]                // a bare key would be swallowed in every game
    [InlineData("Shift+O")]          // Shift alone: same problem (typing capitals)
    [InlineData("Ctrl+Alt")]         // no key
    [InlineData("Ctrl+O+P")]         // two keys
    [InlineData("Ctrl+Alt+Tab")]     // not in the allowed set
    [InlineData("Ctrl+F25")]
    public void Refuses_combinations_that_would_get_in_a_game_s_way(string input)
        => Assert.Null(HotkeyGesture.Parse(input));

    [Fact]
    public void Maps_to_register_hotkey_values()
    {
        var g = HotkeyGesture.Parse("Ctrl+Alt+L")!;
        Assert.Equal(0x0003u, (uint)g.Modifiers); // MOD_ALT | MOD_CONTROL
        Assert.Equal(0x4Cu, g.VirtualKey);
        Assert.Equal(0x70u + 11, HotkeyGesture.Parse("Ctrl+F12")!.VirtualKey);
    }

    [Fact]
    public void Created_from_a_recorded_key_press()
    {
        Assert.Equal("Ctrl+Alt+H", HotkeyGesture.Create(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'H')!.ToString());
        Assert.Null(HotkeyGesture.Create(HotkeyModifiers.Shift, 'H'));
        Assert.Null(HotkeyGesture.Create(HotkeyModifiers.Ctrl, 0x09)); // Tab
    }
}

public class OverlaySettingsTests
{
    [Fact]
    public void An_older_settings_file_gets_working_defaults()
    {
        // Phase 2 settings.json: no Presentation, Overlay or Hotkeys sections.
        var old = """{"ClientId":"abc","Layout":"strip","AlwaysOnTop":false,"LyricsScale":1.2,"LinkEnabled":true}""";
        var s = JsonSerializer.Deserialize<AppSettings>(old)!;
        s.Normalize();
        Assert.Equal(Presentations.Desktop, s.Presentation);
        Assert.Equal(Layouts.Strip, s.Layout);
        Assert.False(s.AlwaysOnTop);
        Assert.True(s.LinkEnabled);
        Assert.True(s.Overlay.ClickThrough);
        Assert.True(s.Overlay.AlwaysOnTop);
        Assert.Equal("Ctrl+Alt+L", s.Hotkeys.ClickThrough);
    }

    [Fact]
    public void Repairs_out_of_range_and_unknown_values()
    {
        var s = new AppSettings { Presentation = "fullscreen" };
        s.Overlay.Scale = 9;
        s.Overlay.Opacity = 0;
        s.Overlay.Anchor = "somewhere";
        s.Overlay.Density = "huge";
        s.Overlay.OffsetX = double.NaN;
        s.Hotkeys.Presentation = "alt + control + o";
        s.Hotkeys.Visibility = "O";
        s.Normalize();
        Assert.Equal(Presentations.Desktop, s.Presentation);
        Assert.Equal(OverlaySettings.MaxScale, s.Overlay.Scale);
        Assert.Equal(OverlaySettings.MinOpacity, s.Overlay.Opacity);
        Assert.Equal(OverlayAnchors.TopRight, s.Overlay.Anchor);
        Assert.Equal(OverlayDensities.Compact, s.Overlay.Density);
        Assert.Equal(0, s.Overlay.OffsetX);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys.Presentation);
        Assert.Equal("", s.Hotkeys.Visibility); // invalid → off rather than a surprise binding
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var s = new AppSettings { Presentation = Presentations.Overlay };
        s.Overlay.Monitor = @"\\.\DISPLAY2";
        s.Overlay.MonitorBounds = new PixelRect(2560, -500, 3840, 2160);
        s.Overlay.Anchor = OverlayAnchors.BottomLeft;
        s.Hotkeys.Visibility = "";
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        back.Normalize();
        Assert.Equal(Presentations.Overlay, back.Presentation);
        Assert.Equal(s.Overlay.MonitorBounds, back.Overlay.MonitorBounds);
        Assert.Equal(OverlayAnchors.BottomLeft, back.Overlay.Anchor);
        Assert.Equal("", back.Hotkeys.Visibility);
    }
}

public class PresentationProtocolTests
{
    [Fact]
    public void Presentation_is_validated()
    {
        Assert.Empty(LinkProtocol.Validate(new DisplayPatch { Presentation = "overlay" }));
        Assert.Empty(LinkProtocol.Validate(new DisplayPatch { Presentation = "desktop", Layout = "strip" }));
        var errors = LinkProtocol.Validate(new DisplayPatch { Presentation = "fullscreen" });
        Assert.Contains(errors, e => e.Contains("presentation must be one of: desktop, overlay"));
    }

    [Fact]
    public void State_carries_presentation_and_older_patches_still_parse()
    {
        var state = new StarryDisplay.Link.DisplayState("stage", true, "live", new OverlayState(true, true), new ThemeState(null, null, null, 1.0, true), "overlay");
        var json = JsonSerializer.Serialize(state, LinkProtocol.Json);
        Assert.Contains("\"presentation\":\"overlay\"", json);
        Assert.Contains("\"layout\":\"stage\"", json); // every Phase 2 field is still there

        var old = JsonSerializer.Deserialize<DisplayPatch>("""{"layout":"strip","overlay":{"clickThrough":true}}""", LinkProtocol.Json)!;
        Assert.Null(old.Presentation);
        Assert.Empty(LinkProtocol.Validate(old));
        Assert.Contains("display.presentation", LinkProtocol.Capabilities);
    }
}

public class FirewallRuleTests
{
    private const string Release = @"C:\Users\alexa\source\starry-night-spotify\windows\StarryDisplay\bin\Release\net10.0-windows\StarryDisplay.exe";
    private const string Debug = @"C:\Users\alexa\source\starry-night-spotify\windows\StarryDisplay\bin\Debug\net10.0-windows\StarryDisplay.exe";

    private static readonly string Verbose = $"""

        Rule Name:                            Starry Display (Mac link)
        ----------------------------------------------------------------------
        Enabled:                              Yes
        Direction:                            In
        LocalPort:                            47836
        RemoteIP:                             LocalSubnet,100.64.0.0/255.192.0.0
        Action:                               Allow
        Program:                              {Release}
        Ok.

        """;

    [Fact]
    public void Rule_counts_only_for_the_exe_it_names()
    {
        // A second copy of the app (e.g. a dev build) must not listen on the LAN on the strength of another copy's rule,
        // or Windows shows its broad "allow this app?" prompt.
        Assert.True(LinkFirewall.CoversProgram(Verbose, Release));
        Assert.True(LinkFirewall.CoversProgram(Verbose, Release.ToUpperInvariant()));
        Assert.False(LinkFirewall.CoversProgram(Verbose, Debug));
        Assert.False(LinkFirewall.CoversProgram(Verbose, null));
        Assert.False(LinkFirewall.CoversProgram("No rules match the specified criteria.", Release));
    }
}
