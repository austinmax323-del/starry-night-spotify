# Starry link protocol v1 (Mac ↔ Windows)

The smallest channel that lets the **Mac (controller/context)** steer the **Windows display**.
The **iPhone stays the player**. Spotify state never goes through this link: the display reads
Spotify itself, so it keeps working when the Mac is asleep, gone, or was never set up.

```
iPhone ──plays──▶ Spotify ◀──Web API (PKCE, tokens stay on the PC)── Windows: Starry Display
                                                                          ▲  HTTP/JSON + SSE, LAN only
Mac: `starry` CLI, theme-cycle.sh ─── signed requests ────────────────────┘  port 47836
```

Code: `windows/StarryDisplay/Link/` (contract `LinkProtocol.cs`, crypto `LinkAuth.cs`, server
`LinkServer.cs`) and `mac/starry` (client). The contract and crypto files are pure and unit-tested,
with cross-language vectors that pin C# and Python to the same bytes.

## Why this transport

| Option | Verdict |
| --- | --- |
| **HTTP/JSON** (Kestrel, ships with .NET) | **Chosen** for commands and reads. Stateless, so reconnecting is free. Inspectable with curl. The Mac needs only Python's standard library. |
| **Server-Sent Events** | **Chosen** for push (display → Mac). Plain HTTP, built-in retry semantics, `curl -N`-able. One-way is all we need. |
| WebSocket | Bidirectional isn't needed, and there's no WebSocket client in Python's stdlib. |
| Raw TCP | Custom framing; not inspectable. |
| gRPC / SignalR | Frameworks with code generation; far more than a dozen fields need. |
| MQTT / NATS | Need a broker (excluded). |
| OSC over UDP | Established in music tools, but has no auth or acknowledgement. Possible later for low-latency hints. |
| SSH / remote shell | Excluded: no arbitrary command execution. |
| Mac as server, PC as client | Would need an always-on Mac daemon. The display is the always-on side, so the PC serves. |
| Spicetify extension as client | Spotify's UI is served over HTTPS, so mixed-content and private-network rules block calls to an `http://` LAN host. The terminal path (`theme-cycle.sh` → `starry theme`) carries the theme instead. |

## Exposure

- **Off by default.** Turned on in Settings → Mac link.
- **Binds explicit addresses only:** loopback plus the PC's private IPv4 addresses (10/8, 172.16/12, 192.168/16, Tailscale 100.64/10). Never `0.0.0.0`.
- **Rebinds** when the network changes.
- **Loopback only until the firewall rule exists.** The rule covers this exe, TCP 47836, and the local subnet plus 100.64/10, and is created with one UAC prompt from Settings. Listening on the LAN without it would make Windows show its own broad "allow on public and private networks?" prompt.
- **Peer filter:** connections from outside those private ranges get a 403 even if routing lets them in.
- Request bodies are limited to 16 KB, and connections to 32.

## Pairing (once per Mac)

1. PC: Settings → Mac link → **Pair a Mac**. This shows an 8-character code (Crockford base32, about 40 bits) valid for 3 minutes, with 5 attempts.
2. Mac: `starry pair <pc>.local ABCD-EFGH`
   - `POST /v1/pair/start {"client":"<mac name>"}` → `{pairId, salt (hex), iterations}`
   - Both sides derive `K = PBKDF2-SHA256(code, salt, 200000, 32)`.
   - `POST /v1/pair/finish {pairId, proof: hex HMAC(K, "starry-pair-proof\n"+pairId)}`
   - The PC generates a random 32-byte device key `D` (stored DPAPI-encrypted) and returns
     `box = D XOR HMAC(K, "starry-pair-box\n"+pairId)` and `tag = HMAC(K, "starry-pair-tag\n"+pairId+"\n"+hex(box))`.
   - The Mac checks the tag, recovers `D`, and stores it in the macOS Keychain (service `starry-display`).

An eavesdropper sees the salt, proof and box. Brute-forcing the 40-bit code through 200,000 PBKDF2
rounds per guess is impractical. Each Mac gets its own key, so you can forget one without touching
the others (Settings → Forget, or `starry unpair`).

## Signed requests

Every endpoint except `/v1/health` and `/v1/pair/*` needs these headers:

| Header | Value |
| --- | --- |
| `X-Starry-Key` | key id from pairing |
| `X-Starry-Time` | unix seconds; must be within ±120 s of the PC (401s include `X-Starry-Server-Time` so clients can correct) |
| `X-Starry-Nonce` | 16–64 chars `[A-Za-z0-9_-]`, never reused (replays are rejected) |
| `X-Starry-Signature` | hex `HMAC-SHA256(D, METHOD + "\n" + PATH?QUERY + "\n" + TIME + "\n" + NONCE + "\n" + hex(SHA256(body)))` |

Integrity and authenticity, not secrecy: what crosses the LAN is display settings. Spotify tokens,
the Client ID, artwork and lyrics never do.

## Endpoints

| Method | Path | Body → Response |
| --- | --- | --- |
| GET | `/v1/health` | → `{name, app, version, protocol: 1, capabilities[], paired, pairing, serverTime}` (unauthenticated, nothing private) |
| GET | `/v1/state` | → `{display, playback, context}` |
| PATCH | `/v1/display` | DisplayPatch → DisplayState (422 with `details[]` on bad values) |
| PUT | `/v1/context` | ContextUpdate → ContextState |
| DELETE | `/v1/context` | → 204 |
| GET | `/v1/events` | Server-Sent Events (below) |
| DELETE | `/v1/pairing` | → 204 (the caller's own key is revoked) |
| POST | `/v1/pair/start`, `/v1/pair/finish` | pairing (only while a code is showing) |

Errors are always `{"error": "...", "details": [...]?}` with a meaningful status: 400, 401, 403, 404, 409 or 422.

### DisplayState (what the PC reports)

```json
{
  "layout": "stage",                 // "stage" | "strip"
  "visible": true,
  "mode": "live",                    // "live" (follow Spotify) | "paused" (stop polling)
  "overlay": { "alwaysOnTop": true, "clickThrough": false },
  "theme": {
    "scheme": "Galaxy",              // informational: the Mac's Starry Night scheme name
    "accent": "#FFF3C4",             // null = accent follows the artwork
    "sky": ["#00076F", "#B133C9"],   // null = default night; else card background top → bottom
    "lyricsScale": 1.0,              // 0.7 – 1.6
    "artworkGlow": true
  }
}
```

### DisplayPatch (what the Mac sends)

The same shape with **every field optional**: omitted means unchanged. `theme.accent` and `theme.sky`
also accept `"auto"` to return to the default. Changes from the Mac use the same code path as the tray
and toolbar, and are saved like local changes.

### Playback summary (read-only)

`{"state": "playing|paused|nothingPlaying|ad|unsupported|offline|rateLimited|signedOut|connecting", "kind": "track|episode|ad", "title", "artist", "device", "positionMs", "durationMs"}`.
Item fields are present only while the item is current.

### Context (Mac → PC hints)

```json
{ "source": "as-macbook-air", "ttlSec": 600, "data": { "focus": "coding", "lights": "dim" } }
```

Free-form, at most 32 keys, expiring (default 10 minutes, at most 24 hours). In v1 it's **stored, shown in
Settings, logged and echoed in events, but not acted on**. It's the slot for later phases (for example
"I'm in a call → dim"). If the Mac goes quiet, it lapses; nothing on Windows depends on it.

### Events (`GET /v1/events`, `text/event-stream`)

```
retry: 3000

id: 41
event: display
data: {...DisplayState...}

event: playback
data: {...PlaybackSummary...}

event: context
data: {...ContextState...}   or   {"cleared": true}

: ping            ← heartbeat every 20 s
```

On every (re)connect the server first sends the full current `display`, `playback` and `context`.
Every event carries complete state, so a reconnecting client never needs replay or `Last-Event-ID`.

## Reconnection

| Situation | What happens |
| --- | --- |
| Mac sleeps / closes the lid | Its stream dies. The PC notices on the next heartbeat write and drops it. On wake, `starry watch` times out its read (≤45 s), reconnects, and gets fresh state. One-shot commands just work. |
| PC restarts or the app restarts | Clients get connection refused, then back off 1→2→4…→30 s, then reconnect. *Verified: killed the app under a live `watch`; it reconnected about 1 s after relaunch and resumed events.* |
| Wi-Fi drop / IP change | The PC rebinds to its new addresses (debounced 2 s). Clients reconnect as above. Use `<pc>.local` (Bonjour) rather than an IP. |
| Clock drift | A 401 carries `X-Starry-Server-Time`. The client corrects its offset and retries once. |
| Mac gone for good | Nothing changes on Windows. Its context expires. Forget it in Settings. |

## Observability

- **PC, Settings → Mac link:** listening addresses, firewall rule state, pairing code and countdown, paired Macs (last seen), live connection count, current Mac context, and the last requests (time, status, method, path, peer, note).
- **PC, `%APPDATA%\StarryDisplay\app.log`:** `[link]` lines for every mutation, every failure and stream open/close (never keys or codes).
- **Mac:** `starry status` (one-shot), `starry watch` (live), and `starry raw METHOD PATH [JSON]` (any endpoint, signed).

## Forward compatibility

- Unknown fields are ignored on both sides. New optional fields can be added to v1 freely.
- New abilities are listed in `capabilities` (for example `"context"`). Clients check capabilities, not versions.
- Enum values a client doesn't know get a 422 that lists the allowed values.
- A breaking change gets a new path (`/v2/...`) served alongside `/v1`.

### Adding something later (example: "dim while I'm on a call")

1. The Mac sends `PUT /v1/context {"data": {"call": true}, "ttlSec": 300}`. This already works today.
2. Windows: in `App.Link.cs` `SetContextAsync`, react to `call` (for example by lowering opacity), and add `"context.call"` to `Capabilities`.

No new transport, endpoint or schema version is needed.
