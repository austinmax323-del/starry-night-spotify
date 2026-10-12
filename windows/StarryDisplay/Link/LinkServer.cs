using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StarryDisplay.Services;

namespace StarryDisplay.Link;

public sealed record LinkActivity(DateTimeOffset At, string Peer, string Method, string Path, int Status, string Note);

/// <summary>Everything the Settings window shows about the link.</summary>
public sealed record LinkStatus(
    bool Running,
    IReadOnlyList<string> Endpoints,
    string? Error,
    string? PairingCode,
    DateTimeOffset? PairingExpires,
    IReadOnlyList<PairedDevice> Devices,
    int EventClients,
    IReadOnlyList<LinkActivity> Recent);

/// <summary>
/// The Windows end of the Mac link: a small HTTP/JSON server (Kestrel) with Server-Sent Events.
/// - Off unless enabled in Settings. No internet-facing exposure: binds only to private LAN /
///   Tailscale (100.64/10) / loopback addresses, and rejects peers outside those ranges.
/// - Everything except /v1/health and pairing requires an HMAC-signed request (see LinkAuth).
/// - Stateless HTTP + state-snapshot-on-connect events make reconnection trivial: a client that
///   reconnects (PC restart, Mac sleep, Wi-Fi drop) simply gets the current state again.
/// - Rebinds automatically when the PC's network addresses change.
/// - Never carries Spotify credentials: the display keeps talking to Spotify on its own.
/// </summary>
public sealed class LinkServer : IAsyncDisposable
{
    private static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(3);
    private const int MaxPairAttempts = 5;

    private readonly IDisplayHost _host;
    private readonly LinkKeyStore _keys;
    private readonly int _port;
    private readonly ReplayGuard _replay = new();
    private readonly ConcurrentDictionary<Channel<string>, string> _subscribers = new();
    private readonly LinkedList<LinkActivity> _recent = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private WebApplication? _app;
    private List<string> _endpoints = new();
    private string? _error;
    private long _eventId;
    private System.Threading.Timer? _rebindTimer;
    private string _boundSignature = "";

    // pairing session (one at a time)
    private string? _pairCode;
    private DateTimeOffset _pairExpires;
    private int _pairAttempts;
    private readonly ConcurrentDictionary<string, (byte[] Salt, string Client)> _pairPending = new();

    public LinkServer(IDisplayHost host, LinkKeyStore keys, int port)
    {
        _host = host;
        _keys = keys;
        _port = port;
        _host.DisplayChanged += () => Broadcast("display", _host.GetDisplay());
        _host.PlaybackChanged += () => Broadcast("playback", _host.GetPlayback());
        _host.ContextChanged += () => Broadcast("context", (object?)_host.GetContext() ?? new { cleared = true });
    }

    public event Action? StatusChanged;

    public bool IsRunning => _app is not null;

    public LinkStatus Status
    {
        get
        {
            lock (_gate)
            {
                var pairing = _pairCode is not null && DateTimeOffset.UtcNow < _pairExpires;
                return new LinkStatus(_app is not null, _endpoints.ToList(), _error,
                    pairing ? _pairCode : null, pairing ? _pairExpires : null,
                    _keys.Devices, _subscribers.Count, _recent.ToList());
            }
        }
    }

    // ---- lifecycle --------------------------------------------------------------------------

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_app is not null) return;
            // Without our narrow firewall rule, listening on the LAN makes Windows pop its broad
            // "allow on public and private networks?" prompt. So until the rule exists: loopback only.
            var firewallReady = await Task.Run(LinkFirewall.RuleExists);
            var addresses = firewallReady ? LocalAddresses() : new List<IPAddress> { IPAddress.Loopback };
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.AddServerHeader = false;
                o.Limits.MaxRequestBodySize = LinkProtocol.MaxBodyBytes;
                o.Limits.MaxConcurrentConnections = 32;
                foreach (var ip in addresses) o.Listen(ip, _port);
            });
            var app = builder.Build();
            app.Use(PeerFilter);
            app.Use(Authenticate);
            MapEndpoints(app);

            try
            {
                await app.StartAsync();
            }
            catch (Exception ex)
            {
                await app.DisposeAsync();
                lock (_gate) _error = ex is IOException or SocketException || ex.InnerException is SocketException
                    ? $"Port {_port} is in use by another program."
                    : ex.Message;
                Log.Write($"[link] start failed: {ex.GetType().Name}: {ex.Message}");
                StatusChanged?.Invoke();
                return;
            }

            lock (_gate)
            {
                _app = app;
                _error = !firewallReady ? "Firewall rule needed before the Mac can connect (listening on this PC only)."
                    : addresses.Any(a => !IPAddress.IsLoopback(a)) ? null : "No local network connection; listening on this PC only.";
                _endpoints = addresses.Select(a => $"{a}:{_port}").ToList();
                _boundSignature = Signature(addresses);
            }
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            Log.Write($"[link] listening on {string.Join(", ", _endpoints)}");
        }
        finally
        {
            _lifecycle.Release();
        }
        StatusChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            var app = _app;
            _app = null;
            foreach (var ch in _subscribers.Keys) ch.Writer.TryComplete();
            if (app is not null)
            {
                using (var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2))) await app.StopAsync(stopTimeout.Token);
                await app.DisposeAsync();
                Log.Write("[link] stopped");
            }
            lock (_gate) _endpoints = new();
        }
        finally
        {
            _lifecycle.Release();
        }
        StatusChanged?.Invoke();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Rebind (e.g. after the firewall rule was created).</summary>
    public async Task RestartAsync()
    {
        if (_app is null) return;
        await StopAsync();
        await StartAsync();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        // Debounce: Wi-Fi reconnects fire several events in a row.
        _rebindTimer?.Dispose();
        _rebindTimer = new System.Threading.Timer(async _ =>
        {
            if (_app is null || Signature(LocalAddresses()) == _boundSignature) return;
            Log.Write("[link] network addresses changed; rebinding");
            await StopAsync();
            await StartAsync();
        }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Loopback plus every private IPv4 address of an up interface (LAN, Tailscale). Never 0.0.0.0.</summary>
    public static List<IPAddress> LocalAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Loopback };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(ua.Address) && !list.Contains(ua.Address)) list.Add(ua.Address);
            }
        }
        return list;
    }

    private static string Signature(IEnumerable<IPAddress> a) => string.Join(",", a.Select(x => x.ToString()).Order());

    /// <summary>RFC1918, CGNAT/Tailscale 100.64/10, loopback; IPv6 loopback/link-local/ULA. (Not 169.254: idle adapters.)</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    // ---- pairing (started from the Settings window) -------------------------------------------

    public string BeginPairing()
    {
        lock (_gate)
        {
            _pairCode = LinkAuth.NewPairingCode();
            _pairExpires = DateTimeOffset.UtcNow + PairingWindow;
            _pairAttempts = 0;
            _pairPending.Clear();
        }
        Log.Write("[link] pairing window opened (3 min)");
        StatusChanged?.Invoke();
        return _pairCode;
    }

    public void CancelPairing()
    {
        lock (_gate) { _pairCode = null; _pairPending.Clear(); }
        StatusChanged?.Invoke();
    }

    public async Task ForgetAsync(string keyId)
    {
        await _keys.RemoveAsync(keyId);
        Log.Write($"[link] forgot device {keyId}");
        StatusChanged?.Invoke();
    }

    // ---- middleware -------------------------------------------------------------------------

    private async Task PeerFilter(HttpContext ctx, RequestDelegate next)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null || !IsPrivate(remote))
        {
            ctx.Connection.RequestClose();
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            Record(ctx, 403, "non-local peer refused");
            return;
        }
        await next(ctx);
    }

    private async Task Authenticate(HttpContext ctx, RequestDelegate next)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path == "/v1/health" || path.StartsWith("/v1/pair/", StringComparison.Ordinal))
        {
            await next(ctx);
            return;
        }

        var h = ctx.Request.Headers;
        string keyId = h["X-Starry-Key"].ToString(), nonce = h["X-Starry-Nonce"].ToString(), sig = h["X-Starry-Signature"].ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ctx.Response.Headers["X-Starry-Server-Time"] = now.ToString();

        string? problem = null;
        byte[]? key = null;
        if (keyId.Length == 0 || sig.Length == 0) problem = "missing signature headers";
        else if ((key = _keys.KeyFor(keyId)) is null) problem = "unknown key (pair again)";
        else if (!long.TryParse(h["X-Starry-Time"], out var t) || Math.Abs(now - t) > LinkAuth.MaxSkewSeconds) problem = "clock skew too large or bad X-Starry-Time";
        else if (!LinkAuth.IsValidNonce(nonce)) problem = "bad nonce";

        byte[] body = Array.Empty<byte>();
        if (problem is null)
        {
            body = await ReadBodyAsync(ctx);
            var pathAndQuery = ctx.Request.Path + ctx.Request.QueryString;
            if (!LinkAuth.Verify(key!, ctx.Request.Method, pathAndQuery, long.Parse(h["X-Starry-Time"]!), nonce, body, sig)) problem = "bad signature";
            else if (!_replay.TryAccept(keyId, nonce)) problem = "replayed request";
        }

        if (problem is not null)
        {
            await WriteJson(ctx, 401, new ErrorResponse(problem));
            Record(ctx, 401, problem);
            return;
        }

        _keys.Touch(keyId);
        ctx.Items["body"] = body;
        ctx.Items["actor"] = _keys.NameFor(keyId);
        await next(ctx);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContext ctx)
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    // ---- endpoints --------------------------------------------------------------------------

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/v1/health", ctx =>
        {
            var s = Status;
            return WriteJson(ctx, 200, new HealthInfo(Environment.MachineName, "Starry Display",
                typeof(LinkServer).Assembly.GetName().Version?.ToString(3) ?? "?", LinkProtocol.Version, LinkProtocol.Capabilities,
                s.Devices.Count > 0, s.PairingCode is not null, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        });

        app.MapGet("/v1/state", ctx => WriteJson(ctx, 200, new
        {
            display = _host.GetDisplay(),
            playback = _host.GetPlayback(),
            context = _host.GetContext()
        }));

        app.MapMethods("/v1/display", new[] { "PATCH" }, async ctx =>
        {
            var patch = Parse<DisplayPatch>(ctx, out var parseError);
            if (patch is null) { await Reject(ctx, 400, parseError!); return; }
            var errors = LinkProtocol.Validate(patch);
            if (errors.Count > 0) { await Reject(ctx, 422, "invalid display patch", errors); return; }
            var state = await _host.ApplyAsync(patch, Actor(ctx));
            await WriteJson(ctx, 200, state);
            Record(ctx, 200, Summarize(ctx));
        });

        app.MapPut("/v1/context", async ctx =>
        {
            var update = Parse<ContextUpdate>(ctx, out var parseError);
            if (update is null) { await Reject(ctx, 400, parseError!); return; }
            var errors = LinkProtocol.Validate(update);
            if (errors.Count > 0) { await Reject(ctx, 422, "invalid context", errors); return; }
            var now = DateTimeOffset.UtcNow;
            var ctxState = new ContextState(update.Source ?? Actor(ctx), update.Data!, now, now.AddSeconds(update.TtlSec ?? LinkProtocol.DefaultContextTtlSec));
            await _host.SetContextAsync(ctxState);
            await WriteJson(ctx, 200, ctxState);
            Record(ctx, 200, $"context ({update.Data!.Count} keys, ttl {update.TtlSec ?? LinkProtocol.DefaultContextTtlSec}s)");
        });

        app.MapDelete("/v1/context", async ctx =>
        {
            await _host.SetContextAsync(null);
            ctx.Response.StatusCode = 204;
            Record(ctx, 204, "context cleared");
        });

        app.MapGet("/v1/events", Events);

        app.MapPost("/v1/pair/start", PairStart);
        app.MapPost("/v1/pair/finish", PairFinish);

        app.MapFallback(async ctx =>
        {
            await WriteJson(ctx, 404, new ErrorResponse($"no such endpoint: {ctx.Request.Method} {ctx.Request.Path}"));
            Record(ctx, 404, "unknown endpoint");
        });
    }

    private async Task PairStart(HttpContext ctx)
    {
        var req = Parse<PairStartRequest>(ctx, out _, await ReadBodyAsync(ctx));
        string? code;
        lock (_gate) code = _pairCode is not null && DateTimeOffset.UtcNow < _pairExpires ? _pairCode : null;
        if (code is null)
        {
            await Reject(ctx, 409, "pairing is not open; click \"Pair a Mac\" in Starry Display settings first");
            return;
        }
        var pairId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var salt = RandomNumberGenerator.GetBytes(16);
        _pairPending[pairId] = (salt, (req?.Client ?? "Mac").Trim());
        await WriteJson(ctx, 200, new PairStartResponse(pairId, Convert.ToHexStringLower(salt), LinkAuth.PairIterations));
        Record(ctx, 200, $"pairing started by {req?.Client ?? "Mac"}");
    }

    private async Task PairFinish(HttpContext ctx)
    {
        var req = Parse<PairFinishRequest>(ctx, out _, await ReadBodyAsync(ctx));
        string? code;
        lock (_gate)
        {
            code = _pairCode is not null && DateTimeOffset.UtcNow < _pairExpires ? _pairCode : null;
            if (code is not null) _pairAttempts++;
            if (_pairAttempts > MaxPairAttempts) { _pairCode = null; code = null; }
        }
        if (code is null || req?.PairId is null || req.Proof is null || !_pairPending.TryRemove(req.PairId, out var pending))
        {
            await Reject(ctx, 409, "pairing is not open or this attempt expired; start again");
            return;
        }

        var pairKey = LinkAuth.DerivePairKey(code, pending.Salt);
        var expected = Encoding.ASCII.GetBytes(LinkAuth.PairProof(pairKey, req.PairId));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(req.Proof.ToLowerInvariant())))
        {
            await Reject(ctx, 403, "wrong pairing code");
            StatusChanged?.Invoke();
            return;
        }

        var (keyId, deviceKey) = await _keys.AddAsync(pending.Client);
        var (box, tag) = LinkAuth.SealDeviceKey(pairKey, req.PairId, deviceKey);
        CryptographicOperations.ZeroMemory(deviceKey);
        lock (_gate) { _pairCode = null; _pairPending.Clear(); }
        await WriteJson(ctx, 200, new PairFinishResponse(keyId, box, tag, Environment.MachineName));
        Record(ctx, 200, $"paired \"{pending.Client}\" as {keyId}");
        Log.Write($"[link] paired device \"{pending.Client}\" ({keyId})");
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Server-Sent Events. On (re)connect the client immediately gets the full current state
    /// (display, playback, context), then changes as they happen, plus a heartbeat every 20 s.
    /// No replay of missed events is needed because every event carries complete state.
    /// </summary>
    private async Task Events(HttpContext ctx)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        var peer = $"{Actor(ctx)} ({ctx.Connection.RemoteIpAddress})";
        _subscribers[ch] = peer;
        Record(ctx, 200, "event stream opened");
        StatusChanged?.Invoke();
        var ct = ctx.RequestAborted;
        try
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            await ctx.Response.WriteAsync("retry: 3000\n\n", ct);
            await ctx.Response.WriteAsync(Format("display", _host.GetDisplay()), ct);
            await ctx.Response.WriteAsync(Format("playback", _host.GetPlayback()), ct);
            await ctx.Response.WriteAsync(Format("context", (object?)_host.GetContext() ?? new { cleared = true }), ct);
            await ctx.Response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    if (!await ch.Reader.WaitToReadAsync(heartbeat.Token)) break; // server stopping
                    while (ch.Reader.TryRead(out var msg)) await ctx.Response.WriteAsync(msg, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await ctx.Response.WriteAsync(": ping\n\n", ct);
                }
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            _subscribers.TryRemove(ch, out _);
            Log.Write($"[link] event stream closed: {peer}");
            StatusChanged?.Invoke();
        }
    }

    private void Broadcast(string name, object payload)
    {
        if (_subscribers.IsEmpty) return;
        var msg = Format(name, payload);
        foreach (var ch in _subscribers.Keys) ch.Writer.TryWrite(msg);
    }

    private string Format(string name, object payload) =>
        $"id: {Interlocked.Increment(ref _eventId)}\nevent: {name}\ndata: {JsonSerializer.Serialize(payload, payload.GetType(), LinkProtocol.Json)}\n\n";

    // ---- helpers ----------------------------------------------------------------------------

    private static T? Parse<T>(HttpContext ctx, out string? error, byte[]? body = null) where T : class
    {
        body ??= ctx.Items["body"] as byte[] ?? Array.Empty<byte>();
        error = null;
        if (body.Length == 0) { error = "request body is empty"; return null; }
        try
        {
            return JsonSerializer.Deserialize<T>(body, LinkProtocol.Json) ?? throw new JsonException("null");
        }
        catch (JsonException ex)
        {
            error = $"malformed JSON: {ex.Message}";
            return null;
        }
    }

    private static string Actor(HttpContext ctx) => ctx.Items["actor"] as string ?? "unknown";

    private static string Summarize(HttpContext ctx)
    {
        var body = ctx.Items["body"] as byte[];
        var text = body is null ? "" : Encoding.UTF8.GetString(body);
        return text.Length > 120 ? text[..117] + "…" : text;
    }

    private async Task Reject(HttpContext ctx, int status, string error, List<string>? details = null)
    {
        await WriteJson(ctx, status, new ErrorResponse(error, details));
        Record(ctx, status, details is null ? error : $"{error}: {string.Join("; ", details)}");
    }

    private static Task WriteJson(HttpContext ctx, int status, object payload)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, payload.GetType(), LinkProtocol.Json));
    }

    private void Record(HttpContext ctx, int status, string note)
    {
        var entry = new LinkActivity(DateTimeOffset.Now, ctx.Connection.RemoteIpAddress?.ToString() ?? "?", ctx.Request.Method, ctx.Request.Path, status, note);
        lock (_gate)
        {
            _recent.AddFirst(entry);
            while (_recent.Count > 30) _recent.RemoveLast();
        }
        // Successful reads are frequent and boring; log mutations and every failure.
        if (status >= 400 || ctx.Request.Method != "GET" || ctx.Request.Path == "/v1/events")
            Log.Write($"[link] {entry.Method} {entry.Path} from {entry.Peer} → {status} {note}");
        StatusChanged?.Invoke();
    }
}
