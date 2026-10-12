# Starry Display (Windows)

A native Windows Spotify display. It follows whatever Spotify is playing on **any device** (normally
your iPhone), so Spotify never has to run on the PC, and it stays read-only: your phone stays the player.

Built with WPF on .NET 10, with no Electron, browser runtime or background service. It started from
[EvansLR/spotify-lyrics-overlay-wpf](https://github.com/EvansLR/spotify-lyrics-overlay-wpf) (MIT).
See [BASEPLATE.md](BASEPLATE.md) for what was evaluated, reused, adapted and why.

## Two layouts

| | |
| --- | --- |
| **Stage card** | Artwork with a soft colour glow taken from the cover, title, artist, album, the device it's playing on, progress, and a scrolling synced lyric sheet. A normal window with Windows 11 rounded corners and shadow. Narrow it and it becomes a compact now-playing tile. |
| **Lyrics strip** | Two floating lines, the current one large and the next one dimmer, on a transparent window. Built for sitting over games and windows. Pair it with click-through. Track info appears on hover and for a few seconds after each track change. |

Switch with the toolbar button (hover the window), the tray menu, or Settings. Each layout remembers its own size and position.

## States it handles

| Situation | What you see |
| --- | --- |
| Playing / paused | Live progress; a "Paused" chip; the paused lyric line holds, slightly dimmed |
| Nothing playing (Spotify closed, phone asleep, no device) | "Nothing playing" with the last played track |
| Podcast | Episode, show and artwork; "No lyrics for podcasts" |
| Advertisement | "Ad break" chip; "Spotify will be right back." |
| Private session / local file Spotify won't describe | "Playing something private" |
| No lyrics / instrumental / unsynced | A calm message, "♪ Instrumental", or static lyrics you can scroll |
| Network down | The last track stays up with an "Offline · retrying" chip; backs off 5→10→20→30 s |
| Spotify rate limit | Waits exactly as long as Spotify's `Retry-After` header says |
| Sign-in revoked / 403 | Back to the Connect screen with the reason |
| Track changes while lyrics are loading | The old lookup is cancelled, so the previous song's lyrics never appear |

## One-time setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).
2. Create an app at the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) with the
   Redirect URI `http://127.0.0.1:8766/callback` (exactly as written; Spotify rejects `localhost`). The account that
   owns the app needs Premium (Spotify's Development Mode rule).
3. Run the app, paste the **Client ID** and click **Connect**. There's no client secret, because PKCE doesn't use one.

## Run

```powershell
cd windows\StarryDisplay
dotnet run -c Release                        # run
dotnet publish -c Release -o ..\dist         # build windows\dist\StarryDisplay.exe
dotnet run -c Release -- --demo              # scripted playback, no Spotify (add --layout=strip|stage)
cd ..\StarryDisplay.Tests; dotnet test       # core tests (any OS)
```

## Controls

| Control | Action |
| --- | --- |
| Drag anywhere | Move the window (when not click-through) |
| Window edges | Resize |
| Hover toolbar | Switch layout · Always on top · Click-through · Settings · Hide to tray |
| `Ctrl+Alt+L` | Click-through on/off (clicks pass to whatever is underneath) |
| Tray, left click | Show/hide (or turn click-through off if it's on) |
| Tray menu | Show/hide · Layout · Always on top · Click-through · Pause updates · Settings… · Quit |
| Settings | Account, layout, always on top, artwork glow, lyrics size, start with Windows |

Closing the window (`Alt+F4`) hides it to the tray. **Quit** exits.

## Resource use

Measured on this PC (Windows 11, 12 logical cores) during continuous lyric playback:

| | CPU | Memory |
| --- | --- | --- |
| Stage card, playing with lyrics | ~1.9% of one core (0.16% of the machine) | ~80 MB private |
| Lyrics strip, playing | ~2.6% of one core (0.2% of the machine) | ~85 MB private |
| Nothing playing / hidden / screen locked | ≈0% | — |

How it stays light:
- **Polling:** one small HTTPS request every 3 s while playing, an extra one right as a track ends, 5 s when paused, and 10–20 s when nothing plays. It stops completely while the window is hidden, the screen is locked, or updates are paused from the tray.
- **Lyrics:** a one-shot timer fires exactly when the next line starts. There's no render loop.
- **Animation:** only short eased transitions, and nothing loops.
- **Windows:** the card is a normal window composited by the GPU. Only the small strip uses WPF's per-pixel-transparent mode, which copies each frame back from the GPU.
- **Artwork:** decoded once off the UI thread. The glow is a 28 px copy stretched smoothly, so there's no blur effect to render each frame.

## Where things live

`%APPDATA%\StarryDisplay\`

| File | Contents |
| --- | --- |
| `settings.json` | Client ID (public by design), layout, window positions, preferences |
| `token.bin` | Spotify tokens, **encrypted with Windows DPAPI**, so only your Windows account can read them |
| `lyrics-cache.v2.json` | Cached lyrics (hits 30 days, misses 12 h) |
| `app.log` | Startup, track changes, errors. Never tokens. Create `diag.flag` here for verbose lyric tracing. |

## Architecture

```
StarryDisplay/
  App.xaml.cs                 composition root; routes toolbar/tray/hotkey/settings commands
  MainWindow.xaml(.cs)        borderless host: opaque (card) or transparent (strip), drag, resize, click-through
  Core/
    PlaybackController.cs     adaptive polling, state machine, per-track artwork+lyrics fetch with cancellation
    NowPlayingViewModel.cs    everything compositions need, plus ItemChanged/ArtworkChanged/LyricsChanged events
    PlaybackClock.cs          local playback head between polls (absorbs jitter, snaps on seeks)
    PollPolicy.cs             how long until the next poll (pure, tested)
    SpotifyPlayerParser.cs    /me/player JSON → snapshot (track, episode, ad, unsupported)
  Lyrics/                     isolated module: ILyricsProvider, LRCLIB provider, disk cache, LRC parser
  Services/                   Spotify (PKCE, DPAPI tokens), artwork, settings, tray, autostart, log, demo
  Views/
    IComposition.cs           the contract a layout implements
    StageView, StripView      the two compositions
    LyricsView.cs             shared synced-lyrics control (sheet or strip)
    SetupView, SettingsWindow, Anim.cs
StarryDisplay.Tests/          22 tests over the UI-free core
```

**Adding a composition:** implement `IComposition` on a UserControl, subscribe to the view model's events,
and register it in `MainWindow.CreateComposition`.

**Future Mac link (not built yet):** implement `IPlaybackSource` (for example a LAN/Tailscale listener fed by
the Mac) and hand it to `PlaybackController`. The UI only ever sees the view model.
