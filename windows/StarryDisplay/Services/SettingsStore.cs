using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarryDisplay.Core;

namespace StarryDisplay.Services;

public sealed class SettingsStore
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarryDisplay");

    // Binds DPAPI ciphertext to this app; not a secret by itself.
    private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("StarryDisplay.v1.tokens");

    private readonly string _dir = DefaultDirectory;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string DataDirectory => _dir;

    public async Task<T?> ReadAsync<T>(string name)
    {
        try
        {
            var path = Path.Combine(_dir, name);
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, _json);
        }
        catch
        {
            return default;
        }
    }

    public async Task WriteAsync<T>(string name, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        await WriteAtomicAsync(name, bytes);
    }

    /// <summary>
    /// Reads a value encrypted with Windows DPAPI (current user scope). Only this Windows
    /// account on this PC can decrypt it; a copied file is useless elsewhere.
    /// </summary>
    public async Task<T?> ReadSecretAsync<T>(string name)
    {
        try
        {
            var path = Path.Combine(_dir, name);
            if (!File.Exists(path)) return default;
            var cipher = await File.ReadAllBytesAsync(path);
            var plain = ProtectedData.Unprotect(cipher, SecretEntropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain, _json);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not read secret '{name}'; treating as signed out.", ex);
            return default;
        }
    }

    public async Task WriteSecretAsync<T>(string name, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        var cipher = ProtectedData.Protect(plain, SecretEntropy, DataProtectionScope.CurrentUser);
        Array.Clear(plain);
        await WriteAtomicAsync(name, cipher);
    }

    public async Task<AppSettings> ReadSettingsAsync()
    {
        var settings = await ReadAsync<AppSettings>("settings.json") ?? new AppSettings();
        settings.Normalize();
        return settings;
    }

    public void Delete(string name)
    {
        var path = Path.Combine(_dir, name);
        if (File.Exists(path)) File.Delete(path);
    }

    // Write-then-rename so a crash mid-write never leaves a truncated settings/token file.
    private async Task WriteAtomicAsync(string name, byte[] bytes)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
