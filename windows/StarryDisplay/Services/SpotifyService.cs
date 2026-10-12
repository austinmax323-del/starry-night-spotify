using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarryDisplay.Core;

namespace StarryDisplay.Services;

/// <summary>
/// Spotify Web API access using Authorization Code + PKCE (no client secret anywhere).
/// Reads playback from whichever device is active — normally the iPhone — so the PC
/// never needs Spotify running locally.
/// Adapted from EvansLR/spotify-lyrics-overlay-wpf (MIT); see windows/BASEPLATE.md.
/// </summary>
public sealed class SpotifyService : IPlaybackSource
{
    private const string AuthUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string ApiUrl = "https://api.spotify.com/v1";
    // Spotify requires a loopback IP literal (not "localhost") for http redirect URIs.
    public const string RedirectUri = "http://127.0.0.1:8766/callback";
    private const string ListenerPrefix = "http://127.0.0.1:8766/";
    private const string TokenFile = "token.bin"; // DPAPI-encrypted
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromMinutes(3);

    // Read-only scopes: this phase only displays playback. Add control scopes when a later phase needs them.
    private static readonly string[] Scopes = { "user-read-currently-playing", "user-read-playback-state" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SettingsStore _store;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public SpotifyService(SettingsStore store)
    {
        _store = store;
    }

    public async Task<bool> HasTokenAsync()
    {
        var token = await _store.ReadSecretAsync<TokenInfo>(TokenFile);
        return !string.IsNullOrWhiteSpace(token?.RefreshToken) || !string.IsNullOrWhiteSpace(token?.AccessToken);
    }

    public async Task StartAuthAsync(string clientId)
    {
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Client ID is required.");

        var settings = await _store.ReadSettingsAsync();
        settings.ClientId = clientId;
        await _store.WriteAsync("settings.json", settings);

        var verifier = RandomString(96);
        var state = RandomString(32);
        var challenge = Sha256Base64Url(verifier);

        using var listener = new HttpListener();
        listener.Prefixes.Add(ListenerPrefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException("Port 8766 is busy, so Spotify can't call back. Close whatever is using it and try again.", ex);
        }

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["scope"] = string.Join(' ', Scopes),
            ["redirect_uri"] = RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state
        };

        OpenExternal($"{AuthUrl}?{ToForm(query)}");

        using var timeout = new CancellationTokenSource(AuthTimeout);
        using var _ = timeout.Token.Register(() => { try { listener.Stop(); } catch { } });

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("Spotify sign-in timed out. Click Connect to try again.");
            }

            if (context.Request.Url?.AbsolutePath != "/callback")
            {
                await WriteBrowserResponse(context, 404, "Not found");
                continue; // e.g. favicon requests; keep waiting for the real callback
            }

            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];
            var returnedState = context.Request.QueryString["state"];

            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(returnedState ?? ""), Encoding.ASCII.GetBytes(state)))
            {
                // Stale or forged callback (e.g. a refreshed tab from an earlier attempt). Reject it,
                // but keep waiting for the genuine one instead of aborting the sign-in in progress.
                Log.Write("Ignored a sign-in callback with a non-matching state.");
                await WriteBrowserResponse(context, 400, "This sign-in link is out of date. Use the Spotify tab opened most recently, or click Connect again.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponse(context, 400, error ?? "Missing authorization code");
                throw new InvalidOperationException(error == "access_denied" ? "Spotify access was declined." : error ?? "Missing authorization code");
            }

            await ExchangeCodeAsync(clientId, code, verifier);
            await WriteBrowserResponse(context, 200, "Spotify connected. You can close this tab.");
            Log.Write("Spotify connected.");
            return;
        }
    }

    public void Disconnect()
    {
        _store.Delete(TokenFile);
        Log.Write("Spotify disconnected by user.");
    }

    public async Task<PlaybackSnapshot> GetPlaybackAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync("/me/player?additional_types=track,episode", forceRefresh: false, cancellationToken)
            ?? throw new AuthRequiredException("Connect Spotify to start.");

        // 204: no active device (Spotify closed everywhere, or the phone went to sleep long enough to drop out).
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return PlaybackSnapshot.None;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            Log.Write($"Spotify 429; backing off {retry.TotalSeconds:0}s.");
            throw new RateLimitedException(retry);
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // Dev-mode apps answer 403 when the signed-in user isn't on the app's user list
            // (or the owner's Premium lapsed). Signing in again won't help; say what to check.
            throw new AuthRequiredException("Spotify refused access (403). Check that this account is added under User Management in your Spotify developer app.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return SpotifyPlayerParser.Parse(doc.RootElement);
    }

    private async Task<HttpResponseMessage?> SendApiAsync(string path, bool forceRefresh, CancellationToken ct)
    {
        var token = await GetValidTokenAsync(forceRefresh);
        if (token is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var response = await Http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            if (!forceRefresh) return await SendApiAsync(path, forceRefresh: true, ct); // access token revoked early: refresh once
            Log.Write("Spotify rejected a freshly refreshed token; signing out.");
            _store.Delete(TokenFile);
            return null;
        }

        return response;
    }

    private async Task<TokenInfo?> GetValidTokenAsync(bool forceRefresh)
    {
        await _tokenLock.WaitAsync();
        try
        {
            var token = await _store.ReadSecretAsync<TokenInfo>(TokenFile);
            if (token is null) return null;
            if (!forceRefresh && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < token.ExpiresAt - 60_000) return token;

            var settings = await _store.ReadSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(token.RefreshToken)) return null;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["client_id"] = settings.ClientId
            });

            using var response = await Http.PostAsync(TokenUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                // 400 invalid_grant = refresh token revoked/expired: the user must reconnect.
                // Anything else (5xx, network) is transient: keep the token and retry next poll.
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    Log.Write($"Token refresh rejected ({(int)response.StatusCode}); signing out.");
                    _store.Delete(TokenFile);
                }
                return null;
            }

            return await StoreTokenAsync(response, token.RefreshToken);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task ExchangeCodeAsync(string clientId, string code, string verifier)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        });

        using var response = await Http.PostAsync(TokenUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Spotify token exchange failed ({(int)response.StatusCode}). Check the Client ID and that the Redirect URI is exactly {RedirectUri}");
        }
        await StoreTokenAsync(response, "");
    }

    private async Task<TokenInfo> StoreTokenAsync(HttpResponseMessage response, string previousRefreshToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? "";
        // Spotify may rotate refresh tokens; keep the old one only if no new one was issued.
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString() ?? previousRefreshToken
            : previousRefreshToken;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;

        var token = new TokenInfo
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds()
        };
        await _store.WriteSecretAsync(TokenFile, token);
        return token;
    }

    private static string RandomString(int length)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(length))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "")
            .Substring(0, length);
    }

    private static string Sha256Base64Url(string value)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(value));
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string ToForm(Dictionary<string, string> values)
    {
        return string.Join('&', values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static void OpenExternal(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static async Task WriteBrowserResponse(HttpListenerContext context, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>Starry Display</title><body style=\"font:16px system-ui;background:#0b0f1a;color:#eef\">{WebUtility.HtmlEncode(text)}</body>");
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
