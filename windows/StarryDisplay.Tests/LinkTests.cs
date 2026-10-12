using System.Text;
using System.Text.Json;
using StarryDisplay.Link;
using Xunit;

namespace StarryDisplay.Tests;

/// <summary>
/// Vectors were computed independently with Python's hashlib/hmac (what the Mac client uses),
/// so these tests pin C# and Python to the same bytes.
/// </summary>
public class LinkAuthTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"layout\":\"strip\"}");

    [Fact]
    public void Signature_matches_python_reference()
        => Assert.Equal("6ae9b57cda723750ec3dc0cd8a2a9a45711e51b9006b189a786341d74fd99118",
            LinkAuth.Sign(Key, "PATCH", "/v1/display", 1760000000, "abcdefghijklmnop1234", Body));

    [Fact]
    public void Verify_rejects_tampering()
    {
        var sig = LinkAuth.Sign(Key, "PATCH", "/v1/display", 1760000000, "abcdefghijklmnop1234", Body);
        Assert.True(LinkAuth.Verify(Key, "PATCH", "/v1/display", 1760000000, "abcdefghijklmnop1234", Body, sig));
        Assert.False(LinkAuth.Verify(Key, "PATCH", "/v1/display", 1760000001, "abcdefghijklmnop1234", Body, sig));
        Assert.False(LinkAuth.Verify(Key, "PATCH", "/v1/display", 1760000000, "abcdefghijklmnop1234", Encoding.UTF8.GetBytes("{\"layout\":\"stage\"}"), sig));
        Assert.False(LinkAuth.Verify(Key, "PATCH", "/v1/context", 1760000000, "abcdefghijklmnop1234", Body, sig));
        Assert.False(LinkAuth.Verify(Key, "PATCH", "/v1/display", 1760000000, "abcdefghijklmnop1234", Body, "nothex"));
    }

    [Fact]
    public void Pairing_matches_python_reference()
    {
        var salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var k = LinkAuth.DerivePairKey("abcd-1234", salt);
        Assert.Equal("191654d5c97f7ca53aba8aa6567083d34c5cd2323296ae2e284f333913596a2e", Convert.ToHexStringLower(k));
        Assert.Equal("68190075800aaa32ad9fc65f9b11b398ba08d8007ee9c35f7571a352f66dd3e6", LinkAuth.PairProof(k, "pair-test"));
        var (box, tag) = LinkAuth.SealDeviceKey(k, "pair-test", Enumerable.Repeat((byte)0xAA, 32).ToArray());
        Assert.Equal("3c5279af55432eed161b3e282a1f7e5ac3538aa67129d4010caae96b3da0e437", box);
        Assert.Equal("b25a936555475ea0270e04564b3a8d29b21df1a67758cdd28fb8eb158076d1b0", tag);
        Assert.Equal(Enumerable.Repeat((byte)0xAA, 32), LinkAuth.OpenDeviceKey(k, "pair-test", box, tag));
        Assert.Null(LinkAuth.OpenDeviceKey(k, "pair-other", box, tag));
    }

    [Theory]
    [InlineData("abcd-efgh", "ABCDEFGH")]
    [InlineData("  ab cd ef gh ", "ABCDEFGH")]
    [InlineData("IL0O-UUUU", "1100VVVV")]
    public void Codes_normalise_like_crockford(string input, string expected) => Assert.Equal(expected, LinkAuth.NormalizeCode(input));

    [Fact]
    public void New_codes_are_8_symbols_from_the_alphabet()
    {
        var code = LinkAuth.NewPairingCode();
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
    }

    [Fact]
    public void Replay_guard_accepts_once_then_forgets_after_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var guard = new ReplayGuard(() => now);
        Assert.True(guard.TryAccept("k1", "nonce-0000000001"));
        Assert.False(guard.TryAccept("k1", "nonce-0000000001"));
        Assert.True(guard.TryAccept("k2", "nonce-0000000001"));
        now = now.AddMinutes(10);
        Assert.True(guard.TryAccept("k1", "nonce-0000000001"));
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("abcdefghijklmnop", true)]
    [InlineData("abcdefghijklmnop!", false)]
    public void Nonce_format(string nonce, bool ok) => Assert.Equal(ok, LinkAuth.IsValidNonce(nonce));
}

public class LinkProtocolTests
{
    private static DisplayPatch Patch(string json) => JsonSerializer.Deserialize<DisplayPatch>(json, LinkProtocol.Json)!;

    [Fact]
    public void Valid_patch_with_unknown_fields_passes()
        => Assert.Empty(LinkProtocol.Validate(Patch("""{"layout":"strip","overlay":{"clickThrough":true},"futureThing":42,"theme":{"accent":"#ff88aa","sky":["#000000","#123456"],"lyricsScale":1.2}}""")));

    [Fact]
    public void Bad_values_are_reported_clearly()
    {
        var errors = LinkProtocol.Validate(Patch("""{"layout":"wall","mode":"party","theme":{"accent":"pink","sky":["#000000"],"lyricsScale":9}}"""));
        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("layout must be one of"));
    }

    [Fact]
    public void Auto_resets_are_allowed()
        => Assert.Empty(LinkProtocol.Validate(Patch("""{"theme":{"accent":"auto","sky":"auto"}}""")));

    [Fact]
    public void Colors_normalise() => Assert.Equal("#ABCDEF", LinkProtocol.NormalizeColor("abcdef"));

    [Fact]
    public void Context_limits()
    {
        var big = new ContextUpdate { TtlSec = 0, Data = Enumerable.Range(0, 40).ToDictionary(i => "k" + i, _ => JsonDocument.Parse("1").RootElement) };
        Assert.Equal(2, LinkProtocol.Validate(big).Count);
        Assert.Empty(LinkProtocol.Validate(new ContextUpdate { Source = "mac", Data = new() }));
    }
}
