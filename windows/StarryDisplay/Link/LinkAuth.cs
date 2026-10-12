using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace StarryDisplay.Link;

/// <summary>
/// Request signing and short-code pairing for the link. Standard primitives only
/// (HMAC-SHA256, PBKDF2-SHA256, CSPRNG) so the Mac side needs nothing beyond Python's stdlib.
///
/// Signed request headers:
///   X-Starry-Key:       key id (from pairing)
///   X-Starry-Time:      unix seconds
///   X-Starry-Nonce:     16–64 chars [A-Za-z0-9_-], unique per request
///   X-Starry-Signature: hex HMAC-SHA256(key, METHOD \n PATH?QUERY \n TIME \n NONCE \n hex(SHA256(body)))
/// Requests are accepted within ±<see cref="MaxSkewSeconds"/> and each nonce only once.
/// </summary>
public static class LinkAuth
{
    public const int MaxSkewSeconds = 120;
    public const int PairIterations = 200_000;
    public const string PairAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32: no I, L, O, U

    public static string CanonicalString(string method, string pathAndQuery, long unixTime, string nonce, ReadOnlySpan<byte> body) =>
        $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{unixTime}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";

    public static string Sign(byte[] key, string method, string pathAndQuery, long unixTime, string nonce, ReadOnlySpan<byte> body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(CanonicalString(method, pathAndQuery, unixTime, nonce, body))));

    public static bool Verify(byte[] key, string method, string pathAndQuery, long unixTime, string nonce, ReadOnlySpan<byte> body, string signatureHex)
    {
        byte[] given;
        try { given = Convert.FromHexString(signatureHex); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(CanonicalString(method, pathAndQuery, unixTime, nonce, body)));
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    public static bool IsValidNonce(string? nonce) =>
        nonce is { Length: >= 16 and <= 64 } && nonce.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // ---- pairing --------------------------------------------------------------------------

    /// <summary>8 Crockford-base32 characters (40 bits), shown as "ABCD-EFGH".</summary>
    public static string NewPairingCode()
    {
        Span<char> c = stackalloc char[8];
        for (var i = 0; i < c.Length; i++) c[i] = PairAlphabet[RandomNumberGenerator.GetInt32(PairAlphabet.Length)];
        return $"{new string(c[..4])}-{new string(c[4..])}";
    }

    /// <summary>Upper-case, drop separators, and map look-alikes the way Crockford base32 does.</summary>
    public static string NormalizeCode(string code)
    {
        var sb = new StringBuilder(8);
        foreach (var ch in code.ToUpperInvariant())
        {
            var c = ch switch { 'I' or 'L' => '1', 'O' => '0', 'U' => 'V', _ => ch };
            if (PairAlphabet.Contains(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Pairing key K = PBKDF2-SHA256(normalized code, salt, iterations, 32 bytes).</summary>
    public static byte[] DerivePairKey(string code, byte[] salt, int iterations = PairIterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(NormalizeCode(code)), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Client's proof that it knows the code: hex HMAC(K, "starry-pair-proof\n" + pairId).</summary>
    public static string PairProof(byte[] pairKey, string pairId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(pairKey, Encoding.UTF8.GetBytes("starry-pair-proof\n" + pairId)));

    /// <summary>
    /// Wraps the 32-byte device key for the client: box = key XOR HMAC(K, "starry-pair-box\n"+pairId),
    /// tag = HMAC(K, "starry-pair-tag\n"+pairId+"\n"+hex(box)). One-time use per pairing session.
    /// </summary>
    public static (string BoxHex, string TagHex) SealDeviceKey(byte[] pairKey, string pairId, byte[] deviceKey)
    {
        if (deviceKey.Length != 32) throw new ArgumentException("Device key must be 32 bytes.", nameof(deviceKey));
        var stream = HMACSHA256.HashData(pairKey, Encoding.UTF8.GetBytes("starry-pair-box\n" + pairId));
        var box = new byte[32];
        for (var i = 0; i < 32; i++) box[i] = (byte)(deviceKey[i] ^ stream[i]);
        var boxHex = Convert.ToHexStringLower(box);
        var tag = Convert.ToHexStringLower(HMACSHA256.HashData(pairKey, Encoding.UTF8.GetBytes("starry-pair-tag\n" + pairId + "\n" + boxHex)));
        return (boxHex, tag);
    }

    /// <summary>The client side of <see cref="SealDeviceKey"/> (used by tests; the Mac does the same in Python).</summary>
    public static byte[]? OpenDeviceKey(byte[] pairKey, string pairId, string boxHex, string tagHex)
    {
        var tag = HMACSHA256.HashData(pairKey, Encoding.UTF8.GetBytes("starry-pair-tag\n" + pairId + "\n" + boxHex));
        if (!CryptographicOperations.FixedTimeEquals(tag, Convert.FromHexString(tagHex))) return null;
        var stream = HMACSHA256.HashData(pairKey, Encoding.UTF8.GetBytes("starry-pair-box\n" + pairId));
        var box = Convert.FromHexString(boxHex);
        var key = new byte[32];
        for (var i = 0; i < 32; i++) key[i] = (byte)(box[i] ^ stream[i]);
        return key;
    }
}

/// <summary>Remembers nonces for twice the allowed skew so a captured request can't be replayed.</summary>
public sealed class ReplayGuard(Func<DateTimeOffset>? clock = null)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    /// <summary>True the first time a (key, nonce) pair is seen; false for a replay.</summary>
    public bool TryAccept(string keyId, string nonce)
    {
        var now = _now();
        if (now - _lastSweep > TimeSpan.FromSeconds(30))
        {
            _lastSweep = now;
            foreach (var (k, exp) in _seen) if (exp < now) _seen.TryRemove(k, out _);
        }
        return _seen.TryAdd(keyId + ":" + nonce, now.AddSeconds(LinkAuth.MaxSkewSeconds * 2));
    }
}
