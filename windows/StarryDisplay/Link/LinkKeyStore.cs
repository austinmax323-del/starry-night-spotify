using System.Security.Cryptography;
using StarryDisplay.Services;

namespace StarryDisplay.Link;

public sealed record PairedDevice(string KeyId, string Name, string KeyBase64, DateTimeOffset PairedAt, DateTimeOffset? LastSeen);

/// <summary>
/// Paired devices and their 32-byte keys, stored DPAPI-encrypted (link-keys.bin) like the Spotify
/// token. Keys are per device so one Mac can be forgotten without re-pairing the others.
/// </summary>
public sealed class LinkKeyStore(SettingsStore store)
{
    private const string FileName = "link-keys.bin";
    private readonly object _gate = new();
    private List<PairedDevice> _devices = new();

    public async Task LoadAsync()
    {
        var list = await store.ReadSecretAsync<List<PairedDevice>>(FileName) ?? new();
        lock (_gate) _devices = list;
    }

    public IReadOnlyList<PairedDevice> Devices
    {
        get { lock (_gate) return _devices.ToList(); }
    }

    public byte[]? KeyFor(string keyId)
    {
        lock (_gate)
        {
            var d = _devices.FirstOrDefault(x => x.KeyId == keyId);
            return d is null ? null : Convert.FromBase64String(d.KeyBase64);
        }
    }

    public string NameFor(string keyId)
    {
        lock (_gate) return _devices.FirstOrDefault(x => x.KeyId == keyId)?.Name ?? keyId;
    }

    public async Task<(string KeyId, byte[] Key)> AddAsync(string name)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var keyId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        lock (_gate)
        {
            _devices.Add(new PairedDevice(keyId, string.IsNullOrWhiteSpace(name) ? "Mac" : name.Trim()[..Math.Min(64, name.Trim().Length)],
                Convert.ToBase64String(key), DateTimeOffset.UtcNow, null));
        }
        await SaveAsync();
        return (keyId, key);
    }

    public async Task RemoveAsync(string keyId)
    {
        lock (_gate) _devices.RemoveAll(d => d.KeyId == keyId);
        await SaveAsync();
    }

    /// <summary>In-memory only; persisted with the next add/remove (last-seen is diagnostic, not security).</summary>
    public void Touch(string keyId)
    {
        lock (_gate)
        {
            var i = _devices.FindIndex(d => d.KeyId == keyId);
            if (i >= 0) _devices[i] = _devices[i] with { LastSeen = DateTimeOffset.UtcNow };
        }
    }

    public Task SaveAsync()
    {
        List<PairedDevice> copy;
        lock (_gate) copy = _devices.ToList();
        return store.WriteSecretAsync(FileName, copy);
    }
}
