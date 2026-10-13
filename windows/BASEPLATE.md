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
- **This PC's network:** the Wi-Fi is classified *Public* (inbound blocked by default), Portmaster was running (since removed), and
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
- Cross-machine: the MacBook Air paired over Wi-Fi (`192.168.4.27` → `zxcvbn.local:47836`) through the narrow
  firewall rule. The first attempts timed out because Portmaster dropped inbound LAN connections
  (`inbound connections blocked` in its log). After Portmaster was removed, `pair/start` and `pair/finish` both
  returned 200 and the Mac's key was stored.

---

# Phase 3: gaming overlay

Researched and built 2026-10-12. Goal: a real gaming-overlay presentation of the same display (no game detection,
injection or capture), with persisted placement, hotkeys, and the Mac link still compatible.

## What existed before

- Desktop display with two layouts: stage card (opaque) and lyrics strip (layered/transparent, could already be
  always-on-top and click-through via Ctrl+Alt+L).
- Ctrl+Alt+L was registered on the display window itself, so every strip ↔ card window swap had to hand it over.
- No DPI manifest: the app was **system**-DPI-aware (bitmap-stretched on a second monitor with different scaling).
- Placement: remembered bounds, with a "still on a screen?" check against the virtual desktop.
- Mac link v1 with `layout`, `visible`, `overlay.alwaysOnTop`, `overlay.clickThrough`.

## What was looked at

Metadata checked 2026-10-12 (GitHub API).

| Project | License / activity | What it offers | Decision |
| --- | --- | --- | --- |
| [thomaslevesque/NHotkey](https://github.com/thomaslevesque/NHotkey) | Apache-2.0, ★343, pushed 2026-01 | WPF global hotkeys via `RegisterHotKey` on an `HwndSource` parented to `HWND_MESSAGE` ("hotkey sink"); `HotkeyAlreadyRegistered` event | **Pattern adapted** (about 30 lines in `HotkeyService`). Not imported: its WPF surface is `KeyBinding`-driven, and we need per-action status and suspend/resume. |
| [File-New-Project/EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | MIT (with named exclusions), ★11k, active | `HotkeyManager` over a message window, with **Pause/Resume** while the user records a new hotkey | **Idea reused**: Settings suspends hotkeys while recording so the combination can be pressed. |
| [micdenny/WpfScreenHelper](https://github.com/micdenny/WpfScreenHelper) | MIT, ★120, pushed 2024-08 | `Screen` for WPF: `EnumDisplayMonitors` + `GetMonitorInfo` + `GetDpiForMonitor` | **Pattern adapted** (about 25 lines in `NativeMethods.Monitors`). Not imported: about 700 lines plus a D2D fallback for Windows 7, and we want physical pixels and per-monitor scale in a pure, testable record. |
| [microsoft/PowerToys](https://github.com/microsoft/PowerToys) (Always On Top) | MIT, very active | Keeps windows topmost by re-asserting `HWND_TOPMOST` from an out-of-context `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`, …) | **Pattern adapted**: the overlay re-asserts topmost on foreground changes only (borderless games sometimes raise themselves). |
| [dechamps/RudeWindowFixer](https://github.com/dechamps/RudeWindowFixer) | (no license file), 2023 | Documents how full-screen *transparent* click-through windows (WS_EX_LAYERED + TRANSPARENT/NOACTIVATE) confuse the taskbar's "rude window" logic | **Design input**: the overlay is a small card, never a full-screen transparent window, so it can't trigger this. |
| [michel-pi/GameOverlay.Net](https://github.com/michel-pi/GameOverlay.Net) | MIT, **archived** 2021 | Direct2D overlay windows (SharpDX) | Rejected: archived, brings a rendering stack, more than a small card needs. |
| [HavenDV/H.Hooks](https://github.com/HavenDV/H.Hooks) | MIT, active | Low-level keyboard/mouse hooks | Rejected: a low-level hook sees every key the game gets. `RegisterHotKey` sees only our combinations. |
| [lepoco/wpfui](https://github.com/lepoco/wpfui) | MIT, ★9.7k | Fluent WPF control library | Rejected: a UI framework to get a few toggles; the existing theme already has them. |
| Microsoft docs: [per-monitor DPI for WPF](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware) | n/a | `PerMonitorV2` via `app.manifest` | **Adopted.** |

Electron, WebView, injected overlays, DirectX hooks and overlay frameworks were out of scope by design. Nothing
needed them: a no-activate topmost layered window is the documented way to float UI over a borderless game.

## What changed, and why

| Change | Why |
| --- | --- |
| **Presentation** (`desktop` / `overlay`) + **visible**, in one process | The overlay is a mode of the display, not an app. `ApplyPresentation` is the only path that shows/hides windows: it shows the new one before hiding the old one (no polling blip), and activates only when the user asked from the tray |
| `OverlayWindow`: layered transparent, `WS_EX_NOACTIVATE` + `ShowActivated=false` + `WM_MOUSEACTIVATE → MA_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT` when click-through (default on), topmost | Never steals focus (showing, clicking, dragging), no taskbar/Alt+Tab entry, the mouse goes to the game |
| Topmost re-asserted on `EVENT_SYSTEM_FOREGROUND` (out-of-context WinEvent, skip own process), only while visible and topmost | Survives games that raise themselves; event-driven, so there's no timer |
| `OverlayView`: fixed card size per density (lyric only / compact / full); `LyricsView` gained an `Overlay` layout (left-aligned, one or two lines) | Lyric changes never resize or re-place the window; the same one-shot line timer as the desktop (no render loop); progress ticks at 1 Hz only in Full |
| No effects (shadow/blur) on the overlay | In a layered window every effect is re-rendered and read back on each change |
| `OverlayPlacement` (pure): monitor by device name, then by bounds (renumbering), then primary; nine anchors on the work area + DPI-scaled offset; always clamped onto the monitor; a drop becomes *nearest anchor + offset* | Multi-monitor, mixed DPI, unplug/replug, primary changes and resolution changes can't strand it off-screen, and it returns to its monitor when replugged |
| Re-place on `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `DisplaySettingsChanged`; hop to the target monitor before measuring | WPF rescales for the target DPI before the anchor maths |
| `app.manifest`: PerMonitorV2 | Crisp on every monitor, and real pixels for placement (the app was system-aware before) |
| `HotkeyService` on a message-only window for the app's lifetime; three configurable hotkeys (Ctrl+Alt+O / H / L), master switch, per-hotkey off, status "in use" | Hotkeys no longer depend on which display window exists; a taken combination degrades to tray/Settings/Mac instead of failing |
| `HotkeyGesture` (pure) requires Ctrl, Alt or Win and allows only letters, digits, F-keys and a few navigation keys | A registered hotkey is swallowed system-wide; a plain key or Shift+key would be stolen from every game |
| Main window `ShowActivated=false` | Leaving the overlay (hotkey, Mac) no longer pulls focus from a game |
| Protocol: additive `presentation` + capability `display.presentation`; `overlay.*` and `visible` apply to whatever is showing; `layout` alone returns to the desktop | Phase 2 clients keep working unchanged (`starry layout stage` still shows the stage card) |
| `LinkFirewall.RuleExists` now checks the rule's **program** | Found during testing: a second copy of the app (the Debug build) treated the Release build's rule as its own, listened on the LAN, and Windows showed its broad prompt. Now another copy stays loopback-only |
| Tray: "Gaming overlay" item; hotkey hints show only when registered. Toolbar: gamepad button. Settings: Gaming overlay + Hotkeys sections | Requested controls, kept to the smallest useful set |

## Verified on the PC

Windows 11, i5-10400F (12 logical cores), RTX 3060, one 2560×1080 monitor at 100% with the taskbar on the left.

- **Builds:** Debug and Release, 0 warnings, 0 errors. **Tests:** 75 pass (38 before this phase + 37 new: placement for
  every anchor, 150% DPI, clamping, oversize, drop round-trips at 100% and 150%, resolution change, monitor fallback,
  renumbered monitor, primary change, hotkey parsing/refusals/VK mapping, settings migration from a Phase 2 file,
  repair of bad values, JSON round-trip, protocol validation + compatibility, firewall rule program matching).
- **Transitions through the Mac link** (loopback test client, 18 checks) and **through the hotkeys** (6 checks):
  desktop → overlay → hidden → overlay → desktop, hidden → overlay in one call, click-through on/off,
  always-on-top on/off on both the overlay and the desktop display, an old-style `{"layout":"strip"}` patch leaving the
  overlay, `presentation` in `GET /v1/state`, 422 for an unknown presentation. After every step the window's actual
  extended styles were read back (`topmost`, `clickthrough`, `toolwindow`, `noactivate`) and **the foreground window
  was unchanged every time**.
- **Restart persistence:** killed the app in overlay mode with click-through off; it came back as the overlay, with
  click-through off, without taking focus.
- **Drag:** with click-through off, the overlay was dragged with the mouse; focus stayed on the foreground app, and the
  drop was saved as `center +412,+132` (exactly the drop point). With click-through on, the mouse went through the
  overlay to the window underneath.
- **Still controllable with click-through on:** hotkeys, tray (left click turns click-through off), Settings and the Mac.
- **Hotkey taken by another app:** a second instance logged `Hotkey Ctrl+Alt+L (lock) is taken by another app (error 1409)`;
  Settings shows "Taken by another app; pick another"; everything else kept working.
- **DPI:** the process is now per-monitor-v2 aware (checked with `GetWindowDpiAwarenessContext`); it was system-aware.
- **Mac link from the real Mac:** paired and controlled (layout, theme) over Wi-Fi earlier today; the protocol change
  is additive and the Phase 2 commands were re-run against the new build over loopback.
- **No invasive techniques:** the app imports 14 Win32 functions (`user32`, `dwmapi`, `shcore`): window styles,
  `SetWindowPos`, monitor/DPI queries, `RegisterHotKey`, and an out-of-context foreground WinEvent. No process
  handles, hooks, injection, capture or synthetic input (checked by search).
- Visual check of all three densities and the Settings sections (screenshots during development).

## Measurements

Release build, measured from the process's own CPU time over the window shown, so it covers everything the app
does. GPU is the Windows "GPU Engine" 3D utilisation attributed to the process. dwm.exe's CPU can't be read without
elevation; its GPU share stayed at 0.2–0.5% throughout. The `--demo` cycle (103 s) is a **stress case**: a
synced-lyrics song with a pause, a podcast, an ad, a second song and nothing playing, so 4 artwork changes per 103 s,
against about 1 per 3–4 minutes with real music.

| What | CPU (one core) | CPU (machine) | GPU 3D | Private memory |
| --- | --- | --- | --- | --- |
| Real app, overlay shown, nothing playing (60 s) | 0.44% | 0.04% | 0.00% | 63 MB |
| Real app, hidden (polling paused, 60 s) | 0.03% | 0.00% | 0.00% | 61 MB |
| Demo, overlay Full, before the 30 fps cap | 3.28% | 0.27% | 0.20% avg | 91 MB |
| Demo, overlay **Full** (30 fps transitions) | **2.36%** | **0.20%** | 0.16% avg, 1.1% max | 93 MB |
| Demo, overlay **Compact** (30 fps transitions) | **2.56%** | **0.21%** | 0.12% avg, 1.1% max | 84 MB |
| Demo, overlay Full, lyrics moving only (40 s) | 2.29% | 0.19% | 0.26% avg | 100 MB |
| Demo, desktop stage card (for comparison) | 3.96% | 0.33% | 1.31% avg, 17.5% max | 86 MB |
| Demo, desktop lyrics strip (for comparison) | 2.43% | 0.20% | 0.94% avg | 86 MB |
| Real app, overlay **Compact**, real music + synced lyrics (90 s, warm) | **3.31%** | **0.28%** | 0.29% avg, 1.0% max | 114 MB |
| Real app, desktop stage card, same music (90 s, warm) | 4.68% | 0.39% | 1.48% avg, 10.1% max | 89 MB |

Reading it: the overlay costs less than the stage card, both in the demo cycle and with real music, and about the
same as the lyrics strip. The real-music rows also include Spotify polling (HTTPS every 3 s) and one track change;
Phase 1 measured the card at about 1.9% on a different song, so lyric density and track changes move these numbers
by a couple of points. Memory with real music is higher because of cached artwork and lyrics, and because the hidden
desktop window keeps its composition.
Artwork changes add little on top of lyric movement (2.36% for the whole cycle against 2.29% for lyrics only).
Capping the overlay's transitions at 30 fps took Full from 3.3% to 2.4% of a core, because each frame of a
transparent window is copied back from the GPU. Nothing runs between changes: no render loop, no high-frequency
timer (lyrics use a one-shot timer per line, the progress bar ticks at 1 Hz in Full only, and staying on top is
event-driven).

## Known limits

- **Exclusive fullscreen:** a game in true exclusive fullscreen bypasses the desktop compositor, so no ordinary
  window can draw over it. Getting around that takes a graphics hook or injection into the game, which this project
  doesn't do. Supported: borderless and windowed, plus "fullscreen" games that Windows' fullscreen optimisations run
  like borderless.
- **Elevated games:** not tested. If a game runs as administrator, `RegisterHotKey` still works, but the
  topmost re-assert after the game takes the foreground may not win against an elevated window that raises itself.
- **Scaling and multiple monitors:** this PC has one monitor at 100%. Mixed-DPI, unplug/replug and primary changes are
  covered by unit tests and handled by the window messages above, but weren't exercised on hardware (see manual checks).
  Changing Windows' display scaling for a test wasn't done, because it's a system setting on your PC.
- **AltGr layouts:** Ctrl+Alt+letter is AltGr+letter on many European layouts; change the hotkeys there.
- The desktop display's click-through still resets on restart (as before); the overlay's is remembered.
- While the overlay is the presentation and Spotify is signed out, the desktop window shows the sign-in screen instead.
- Testing left two block rules ("starrydisplay.exe") for the **Debug** build from Windows' prompt. They affect only that
  copy, and they are why `RuleExists` now checks the program.

## Manual checks still for you

1. In a borderless game: turn on the overlay (Ctrl+Alt+O), play for a minute, and confirm it stays visible and the
   game keeps focus and the mouse. Try Ctrl+Alt+H and Ctrl+Alt+L in-game.
2. If you have a second monitor (or change scaling): pick it in Settings → Gaming overlay → Monitor, then unplug it and
   plug it back; the overlay should wait on the primary and then return.
3. From the Mac: `git pull`, then `starry overlay on`, `starry hide`, `starry show`, `starry overlay off`.

## What Phase 4 inherits

- Presentation state: `AppSettings.Presentation` + visibility, switched only through `App.SetPresentationAsync` /
  `SetVisible` / `ApplyPresentation`. A later automatic trigger (for example game awareness) should call these
  rather than touching windows.
- `OverlayWindow` / `OverlayView` (density, lyrics/artwork toggles, scale, opacity), `OverlayPlacement` (pure, tested),
  `HotkeyService` (add an action by adding a field to `HotkeySettings` and a case in `App.OnCommand`).
- Protocol v1 with `presentation` and capability `display.presentation`; context hints remain stored-only.
- No game detection, process profiles, capture, vision or Mac-driven game logic exists yet. Those are Phase 4+.
