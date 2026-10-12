# Windows baseplate: what we reused and why

Phase 0 decision record for the Windows display (`windows/StarryDisplay`). Researched 2026-10-12.

## The constraint that decides everything

Music plays on the **iPhone**. The PC never runs Spotify, so anything that reads
Windows' local media session (SMTC), the Spotify desktop window title, or local
audio **cannot see what's playing**. The display must ask the **Spotify Web API**
(`GET /me/player`), which reports playback on any device, including the iPhone.
That rules out most Windows lyrics apps, including the most popular one.

## Candidates evaluated

| Project | Stack | License | Maintenance | Spotify auth | Synced lyrics | Transparent / on-top / click-through / tray | Playback source | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [EvansLR/spotify-lyrics-overlay-wpf](https://github.com/EvansLR/spotify-lyrics-overlay-wpf) | WPF, .NET 10, no NuGet deps | **MIT** | v0.1.2, last commit 2026-04-28, small (≈1.4k LOC) | **PKCE**, user's own Client ID | LRCLIB, line-synced + cache | ✅ / ✅ / ✅ (`WS_EX_TRANSPARENT`, Ctrl+Alt+L) / ✅ | **Web API: sees iPhone** ✅ | **Chosen** |
| [WhySoDk/spotify-lyrics-overlay](https://github.com/WhySoDk/spotify-lyrics-overlay) | .NET 9, WPF settings + **WinForms/GDI** overlay, SpotifyAPI.Web | WTFPL | Active (2026-10-09), ≈3.7k LOC | PKCE | LRCLIB + 8 line transitions, album colours | ✅ / ✅ / ✅ / ❌ no tray | Web API ✅ | Runner-up. GDI overlay is a dead end for polished visuals; token stored in plaintext next to the exe; no tray. Good **idea source** for transitions/album colour later. |
| [jayfunc/BetterLyrics](https://github.com/jayfunc/BetterLyrics) | WinUI 3 / Win2D | GPL-3.0 | Very active, ≈2.2k★ | n/a | Excellent (word-level, many sources) | Always-on-top, wallpaper and taskbar modes | **SMTC (local players)**: can't see iPhone ❌ | Rejected: wrong data source, copyleft, ~330 MB repo, heavy for a baseplate |
| [Sean861026/DesktopPulse](https://github.com/Sean861026/DesktopPulse) | WPF .NET 8 | MIT | 2026-07 | n/a | LRCLIB | Widget | SMTC ❌ | Rejected: can't see iPhone; bundles hardware monitor/NAudio |
| [BrianLima/OverFy](https://github.com/BrianLima/OverFy) | WPF, .NET Framework 4.6.1 | GPL-3.0 | Stale (2019) | Old Web API flow | ❌ | Draws via **RivaTuner OSD** (works in exclusive-fullscreen games) | Web API | Rejected as base; RTSS approach noted for a future in-game phase |
| [aleab/toastify](https://github.com/aleab/toastify) | WPF | GPL-2.0 | **Archived** 2023 | Local client | ❌ | Toasts + hotkeys | Local Spotify ❌ | Rejected |
| [DrDetective/Versify_WPF](https://github.com/DrDetective/Versify_WPF) | WPF .NET 8 | MIT | Stale (2025-02), 157 LOC | — | Genius scrape, **unsynced** | — | — | Rejected |
| spotlightify, xsaardo/Spotify-Lyrics-Overlay, MowkE/cadence, viraj-rgb/… | Electron / JS / Python | mixed, some unlicensed | — | — | — | — | — | Rejected: Electron/Python, or no license |

Libraries considered (not adopted yet, kept for later phases):

| Library | License | Why not now / when |
| --- | --- | --- |
| [JohnnyCrazy/SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) | MIT, ≈1.6k★, active | We call one endpoint; the reviewed ~100-line PKCE client is smaller than the dependency. Adopt when we need many endpoints (queue, control). |
| [WXRIW/Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper) | Apache-2.0 | Word-level (syllable) lyrics from more providers. Candidate for a "karaoke" phase. |
| [HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | MIT | WPF-native tray. The WinForms `NotifyIcon` is enough today. |
| [LRCLIB](https://lrclib.net) ([server](https://github.com/tranxuanthang/lrclib), MIT) | Free, no key | **In use** for lyrics. |

## What was reused

Upstream `EvansLR/spotify-lyrics-overlay-wpf` at commit
`202953fa1ad9257da63e6d2f2fc657ec5754bce1` (v0.1.2), MIT. The license is kept
verbatim as [`StarryDisplay/LICENSE.upstream`](StarryDisplay/LICENSE.upstream).
The first commit on this branch is the **unmodified** copy, so
`git diff <that commit> -- windows/StarryDisplay` shows every adaptation.

Kept as-is: the transparent always-on-top WPF window and its XAML, the
click-through lock (`NativeMethods`), the global hotkey, the tray menu, the
setup/lyrics panels, LRC parsing, lyrics cache, progress interpolation and
marquee animation.

Not copied: the Inno Setup installers and the release script (not needed for a
baseplate), README, user guide, screenshot.

## What was changed, and why

| Change | Why |
| --- | --- |
| Renamed to **Starry Display** (`StarryDisplay` namespace/assembly, data in `%APPDATA%\StarryDisplay`) | Part of this project; avoids clashing with an installed copy of upstream |
| **Tokens encrypted with DPAPI** (`token.bin`, current-user scope) instead of plaintext `token.json` | "Handle secrets safely": a copied file can't be used by another account or PC |
| **OAuth `state`** generated and checked (constant-time) | CSRF protection on the loopback callback; upstream omitted it |
| **3-minute sign-in timeout**, port-busy message, ignores stray requests (favicon) | Upstream could hang forever if the browser tab was closed |
| **401 → refresh once** before signing out; transient refresh failures keep the token | Upstream deleted the token on any 401, forcing needless re-logins |
| **429 handling**: honour `Retry-After` | Upstream treated rate limits as generic errors and kept hammering |
| **Adaptive polling**: 2.5 s while playing, 6 s when paused/idle | Fewer API calls; still in sync while music plays |
| Revoked sign-in returns to the Connect screen | Upstream polled forever showing "No active track" |
| Parse **device name** + **album art URL**; artist line shows "· on iPhone" | Confirms the multi-device premise; art URL is ready for later visuals |
| **`IPlaybackSource` seam** (`Services/IPlaybackSource.cs`) | The clean path for future Mac→PC communication without touching the UI |
| LRCLIB: **User-Agent**; retry without album name on 404 | LRCLIB etiquette; Spotify "Deluxe/Remaster" album names often miss |
| **Single instance** (named mutex) | Two copies would fight over tray, hotkey and the callback port |
| **Crash-safe**: unhandled-exception logging to `app.log` (never tokens), UI exceptions don't kill the app; atomic settings writes | "Launches reliably" |
| Tray uses the app icon; connect button disabled while signing in | Small polish/robustness |

Unchanged deliberately: visual design (later phases restyle it), lyric engine
(line-level only), no playback control scopes (read-only for now).

## Known limits (accepted for Phase 0)

- **Spotify Development Mode** (since Feb 2026): the app owner needs Premium, max 5 users per app. Fine for a personal display.
- Polling, not push: lyric sync can be up to ~2.5 s stale after a seek on the phone.
- `Topmost` windows don't draw over **exclusive-fullscreen** games; borderless-windowed games are fine. In-game overlay (RTSS/DirectX hook) is a later phase.
- Episodes/podcasts show "Nothing playing" (upstream behaviour).

---

# Phase 1: from baseplate to standalone display

Researched 2026-10-12. The goal was a display good enough to leave running on its own, with no Mac link, game
awareness, AI or screen capture.

## What else was looked at before building

| Project | License | What it offered | Decision |
| --- | --- | --- | --- |
| [The-MuffinDev/LyricsOnTheGo](https://github.com/The-MuffinDev/LyricsOnTheGo) | MIT | Native WPF lyrics overlay; LRC parser; LRCLIB search fallback; acrylic "glass" via a second Windows.UI.Composition window | **Reused** the LRC parser (adapted) and the search-fallback idea. The glass was skipped: it needs a separate composition window plus WinRT interop, it reads playback locally (SMTC), and an artwork-tinted card suits a dedicated display better. |
| [WhySoDk/spotify-lyrics-overlay](https://github.com/WhySoDk/spotify-lyrics-overlay) | WTFPL | Album-colour picker (most saturated swatch that's neither too dark nor too white) | **Reused the heuristic**, re-implemented on WPF imaging (no GDI or ColorThief dependency) |
| [shhhh007/music-island](https://github.com/shhhh007/music-island) | MIT | Always-on-top island with artwork + lyrics | Reads local playback (SMTC), so it can't see the iPhone. Not used. |
| [jayfunc/BetterLyrics](https://github.com/jayfunc/BetterLyrics) | GPL-3.0 | Best-in-class visuals | Still SMTC + GPL + WinUI 3; reference only |
| [JohnnyCrazy/SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) | MIT | Full Web API client | Still one endpoint; not worth the dependency yet |

No reusable WPF control did "lyrics driven by a remote, polled playback clock", so `LyricsView`,
`PlaybackClock` and the transitions are custom (small and tested).

## What changed and why

| Change | Why |
| --- | --- |
| Split the engine (`Core/`) from presentation (`Views/`) via `NowPlayingViewModel` + `IComposition` | Compositions are modular; new layouts don't touch polling or lyrics |
| `PlaybackController` state machine + `PollPolicy` | All the edge cases (pause, no device, ad, podcast, private, offline, 429, 403, revoked) in one testable place |
| Per-item `CancellationToken` for artwork and lyrics | A track change mid-lookup can't show stale lyrics |
| `PlaybackClock` (absorbs ≤0.9 s jitter, never steps back for server lag, snaps on seeks) | Lyrics stay in sync between 3 s polls without jumping backwards |
| `LyricsView` schedules a one-shot timer for the next line | Accurate timing with ~zero idle CPU (no 60 fps loop) |
| Lyrics module: `ILyricsProvider` → LRCLIB (`get` → `get` without album → `search` by cleaned title, ±3 s duration) → disk cache | Isolated provider; higher hit rate for "Remastered", "feat." and deluxe-album titles; one request for most tracks |
| `ArtworkService`: decode once off-thread, 28 px backdrop, accent colour, LRU | Premium look (artwork glow, accent progress) at no per-frame cost |
| Stage card on a **non-layered** window; strip on a layered one | WPF's transparent windows copy every frame back from the GPU. Moving the card to a normal window took it from ~4.9% to ~1.9% of a core |
| Lyric sheet hosted in a `Canvas` | Inside a Grid, WPF's layout clip silently hid every line after the first screenful |
| Polling pauses when hidden, screen-locked, or paused from the tray; resumes after sleep | Leave-it-open friendly |
| Sign-in: stray/stale callbacks are ignored instead of aborting | A refreshed old tab used to fail the whole sign-in ("state mismatch") |
| Tray menu (layout, topmost, click-through, pause updates, settings, quit), Settings window (Mica), start with Windows (HKCU Run) | Requested tray/settings integration; no services |
| `--demo` scripted playback | Lets every state be checked without Spotify (also used for this phase's verification) |

## Verified on the target PC

- Real playback from the iPhone: track changes picked up within one poll, synced lyrics found and scrolled,
  the device chip shows "iPhone", the accent colour follows each cover.
- Demo cycle on both layouts: synced lyrics through the whole song, pause hold, break "• • •", podcast, ad,
  instrumental, nothing playing with last played.
- CPU and memory as in the README table; 22 core tests pass.

## Known limits

- Lyrics come only from LRCLIB (free, community-run), so some tracks have none, and LRCLIB briefly returned 503s
  during testing. Those are shown as "unavailable" and retried once.
- Line-level sync only (no word-by-word karaoke). `Lyricify-Lyrics-Helper` is the candidate if that's wanted later.
- A seek on the phone takes up to ~3 s (one poll) to reach the PC.
- The strip floats over borderless or windowed games, not exclusive-fullscreen ones (a later phase).

---

# Phase 2: Mac ↔ Windows link

Researched and built 2026-10-12. Goal: the smallest clean local channel so the Mac can steer the display,
without turning this into a distributed system.

## What was looked at

- **This repo:** the Mac side is a Spicetify theme plus zsh scripts (`theme-cycle.sh`, `starry-repair.sh`), so a
  terminal-friendly client fits. The Spicetify extension can't be the client: Spotify's HTTPS UI can't call
  an `http://` LAN host.
- **Your Orynth project:** it already runs Mac (control plane) ↔ this PC over Tailscale with the same rules (no secrets
  between machines, no remote shell). Tailscale is installed here but wasn't running, so the link works on the plain
  LAN and also binds Tailscale's 100.64/10 when it's up.
- **This PC's network:** the Wi-Fi is classified *Public* (inbound blocked by default), Portmaster runs, and
  Bonjour is present, so the Mac resolves as `as-macbook-air.local` and the PC as `zxcvbn.local`.
- **Transports:** compared in [protocol/PROTOCOL.md](../protocol/PROTOCOL.md#why-this-transport). Chose HTTP/JSON + Server-Sent
  Events on **Kestrel** (part of the .NET runtime already installed, mature, no URL-ACL/admin needed, unlike
  `HttpListener`), and **Python's standard library** on the Mac (`hmac`, `hashlib.pbkdf2_hmac`, `urllib`) with the
  **Keychain** via `security`. No third-party packages on either side.

## Decisions

| Decision | Why |
| --- | --- |
| PC serves, Mac is a client | The display is the always-on side; the Mac needs no daemon |
| Display keeps polling Spotify itself | Works without the Mac; no Spotify credentials cross machines |
| Off by default; explicit addresses; peer filter; loopback-only until a narrow firewall rule exists | Conservative exposure. Avoids Windows' broad prompt; a "Cancel" there would have created block rules that override the narrow one (observed, then handled by replacing them) |
| Short-code pairing → per-device key; HMAC-signed requests with ±120 s skew and nonce replay guard | Lightweight auth for commands that change the display; no account system, no TLS certificates to manage |
| State snapshot on every event-stream connect | Reconnection needs no replay logic |
| Context is stored and expiring but not acted on | A slot for later phases without designing them now |
| Theme sync from the Mac's Starry Night `color.ini` (button → accent, sidebar-alt/sidebar → sky) | The one piece of "context" that's useful today, and it ties the two machines' looks together |

## Verified on the PC

- 38 unit tests pass, including C#⇄Python vectors for signing, PBKDF2, proof, box and tag.
- Live server: pairing with the short code; layout, theme and context from the client; 11/11 negative checks
  (unsigned, tampered body, wrong signature, replay, clock skew, invalid values → 422 with details, unknown endpoint,
  pairing closed, unknown fields ignored).
- Reconnection: killed the app under a live `starry watch`; it backed off 1 s → 2 s, reconnected about 1 s after
  relaunch with full state, and kept receiving events. Display settings persisted; context lapsed as designed.
- The cross-machine path (Mac → PC over Wi-Fi through the firewall rule and Portmaster) still needs one run from the Mac.
