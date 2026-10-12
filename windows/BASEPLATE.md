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
