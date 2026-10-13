# Starry Display (Windows)

A native Windows Spotify display. It follows whatever Spotify is playing on **any device** (normally
your iPhone), so Spotify never has to run on the PC, and it stays read-only: your phone stays the player.

Built with WPF on .NET 10, with no Electron, browser runtime or background service. It started from
[EvansLR/spotify-lyrics-overlay-wpf](https://github.com/EvansLR/spotify-lyrics-overlay-wpf) (MIT).
See [BASEPLATE.md](BASEPLATE.md) for what was evaluated, reused, adapted and why.

## Desktop display and gaming overlay

The display has two presentations, both in the same process. Each can be shown or hidden.

| | |
| --- | --- |
| **Desktop display: Stage card** | Artwork with a soft colour glow taken from the cover, title, artist, album, the device it's playing on, progress, and a scrolling synced lyric sheet. A normal window with Windows 11 rounded corners and shadow. Narrow it and it becomes a compact now-playing tile. |
| **Desktop display: Lyrics strip** | Two floating lines, the current one large and the next one dimmer, on a transparent window. Track info appears on hover and for a few seconds after each track change. |
| **Gaming overlay** | A small glassy card for playing games: lyric only, compact (artwork, track, current line), or full (plus the next line and progress). It stays on top, lets every click through to the game by default, and **never takes focus**: appearing, hiding, switching and even dragging it leave the game focused. It has no taskbar button and no Alt+Tab entry. |

Switch with **Ctrl+Alt+O**, the toolbar's gamepad button, the tray menu, Settings, or `starry overlay on` from the Mac.
Each desktop layout remembers its own size and position. The overlay remembers its monitor and position.

### Gaming overlay

- **Where it sits:** pick a monitor (or "Primary") and one of nine spots in Settings, or turn click-through off and drag it.
  The position is saved as *monitor + nearest spot + offset* and is always clamped onto that monitor. It therefore
  survives resolution and scaling changes, and comes back to the right corner when a monitor is replugged. If its
  monitor is unplugged, it waits on the primary until the monitor returns.
- **What it shows:** detail (lyric only / compact / full), lyrics on/off, artwork on/off, size (60–160%), opacity (30–100%).
- **Games:** works over **borderless and windowed** games. Games in **exclusive fullscreen** take over the screen,
  and no ordinary window can appear on top. That's how Windows works, and getting around it would mean hooking the
  game's graphics, which this app never does. Most games offer "borderless" or "windowed fullscreen". Many
  "fullscreen" DirectX 11/12 games actually run with Windows' fullscreen optimisations, which behave like borderless,
  so the overlay often shows there too. If it doesn't, switch the game to borderless.
- **Hands off the game:** the overlay is an ordinary window using documented Windows APIs. It doesn't inject anything, hook
  DirectX, load into or read any game process, or send input. Hotkeys use `RegisterHotKey`, which sees only its own
  combinations, not a keyboard hook. It uses none of the techniques anti-cheat systems watch for (injection, hooks,
  memory reads, synthetic input).

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
dotnet run -c Release -- --demo              # scripted playback, no Spotify (add --layout=strip|stage,
                                             #   --presentation=overlay, --density=minimal|compact|full)
cd ..\StarryDisplay.Tests; dotnet test       # core tests (any OS)
```

## Controls

| Control | Action |
| --- | --- |
| Drag anywhere | Move the window (when not click-through) |
| Window edges | Resize |
| Hover toolbar | Switch layout · Always on top · Click-through · Settings · Hide to tray |
| `Ctrl+Alt+O` | Desktop display ↔ gaming overlay |
| `Ctrl+Alt+H` | Show/hide whatever is showing (never takes focus, so it's safe mid-game) |
| `Ctrl+Alt+L` | Click-through on/off for whatever is showing |
| Tray, left click | Show/hide (or turn click-through off if it's on) |
| Tray menu | Show/hide · Gaming overlay · Layout · Always on top · Click-through · Pause updates · Settings… · Quit |
| Settings | Account, layout, always on top, artwork glow, lyrics size, gaming overlay, hotkeys, start with Windows |

**Hotkeys** are changed in Settings → Hotkeys: click a box, then press a new combination. Each needs Ctrl, Alt or Win,
because a plain key or Shift+key would be taken from every game. Backspace turns one off, and the main switch turns
them all off. If another app already owns a combination, Settings says so and the rest keep working.
On keyboard layouts with **AltGr**, Ctrl+Alt+*letter* can be a typed character (for example AltGr+L = ł in Polish), so pick
another combination there.

Closing the window (`Alt+F4`) hides it to the tray. **Quit** exits.

## Resource use

Measured on this PC (Windows 11, i5-10400F with 12 logical cores, RTX 3060), Release build. Details and method are in
[BASEPLATE.md](BASEPLATE.md#measurements).

| | CPU | GPU | Memory |
| --- | --- | --- | --- |
| **Gaming overlay**, real music with synced lyrics | ~3.3% of one core (0.28% of the machine) | ~0.3% | ~115 MB private |
| Stage card, same music | ~4.7% of one core (0.39% of the machine) | ~1.5% | ~90 MB private |
| Gaming overlay, demo stress cycle (lyrics + 4 artwork changes in 103 s) | ~2.4–2.6% of one core | ~0.15% | ~85–95 MB |
| Lyrics strip, same demo cycle | ~2.4% of one core | ~0.9% | ~85 MB |
| Overlay shown, nothing playing | ~0.4% of one core | 0% | ~63 MB |
| Hidden / screen locked | ≈0% (0.03%) | 0% | ~61 MB |

Phase 1 measured the stage card at ~1.9% on a different song. Lyric density and track changes move these numbers
by a couple of points.

How it stays light:
- **Polling:** one small HTTPS request every 3 s while playing, an extra one right as a track ends, 5 s when paused, and 10–20 s when nothing plays. It stops completely while the window is hidden, the screen is locked, or updates are paused from the tray.
- **Lyrics:** a one-shot timer fires exactly when the next line starts. There's no render loop.
- **Animation:** only short eased transitions, and nothing loops.
- **Windows:** the card is a normal window composited by the GPU. Only the small strip and the overlay use WPF's per-pixel-transparent mode, which copies each frame back from the GPU. The overlay's transitions are therefore capped at 30 fps, and it has no shadows or blur.
- **Overlay:** fixed size per detail level (a lyric change never resizes or moves the window); the progress bar ticks once a second (Full only); staying on top is event-driven, not a timer.
- **Artwork:** decoded once off the UI thread. The glow is a 28 px copy stretched smoothly, so there's no blur effect to render each frame.

## Mac link (optional)

Turn it on in **Settings → Mac link**. Then a paired Mac can change the layout, visibility,
always-on-top, click-through, live/paused mode and theme (accent, sky, lyric size, glow), and send
expiring context hints. The display keeps getting Spotify state itself, so the Mac is never required,
and **no Spotify credentials cross the network**.

1. Turn on **Allow control from my Mac**. If needed, approve the one admin prompt; it creates a narrow
   firewall rule (this app, port 47836, local subnet and Tailscale only). Until that rule exists, the link
   listens on this PC only, so Windows never shows its broad "allow on public networks" prompt.
2. Click **Pair a Mac…** and run the command it shows on the Mac (`starry pair zxcvbn.local CODE`).
   See [mac/README.md](../mac/README.md).
3. Settings shows paired Macs, live connections, Mac context and recent requests. The log has `[link]` lines.

`StarryDisplay.exe --pair` turns the link on and opens a pairing code directly.
Protocol, security model and reconnection behaviour: [protocol/PROTOCOL.md](../protocol/PROTOCOL.md).

## Where things live

`%APPDATA%\StarryDisplay\`

| File | Contents |
| --- | --- |
| `settings.json` | Client ID (public by design), presentation, layout, window positions, overlay placement and look, hotkeys, preferences |
| `token.bin` | Spotify tokens, **encrypted with Windows DPAPI**, so only your Windows account can read them |
| `lyrics-cache.v2.json` | Cached lyrics (hits 30 days, misses 12 h) |
| `link-keys.bin` | Paired Macs' link keys, **DPAPI-encrypted** |
| `app.log` | Startup, track changes, errors, `[link]` activity. Never tokens or keys. Create `diag.flag` here for verbose lyric tracing. |

## Architecture

```
StarryDisplay/
  App.xaml.cs                 composition root; routes toolbar/tray/hotkey/settings commands
  MainWindow.xaml(.cs)        desktop host: opaque (card) or transparent (strip), drag, resize, click-through
  NativeMethods.cs            the documented Win32 calls (window styles, z-order, monitors/DPI, hotkeys, foreground events)
  app.manifest                per-monitor (v2) DPI awareness
  Core/
    PlaybackController.cs     adaptive polling, state machine, per-track artwork+lyrics fetch with cancellation
    NowPlayingViewModel.cs    everything compositions need, plus ItemChanged/ArtworkChanged/LyricsChanged events
    PlaybackClock.cs          local playback head between polls (absorbs jitter, snaps on seeks)
    PollPolicy.cs             how long until the next poll (pure, tested)
    Overlay.cs                presentations, overlay settings, multi-monitor/DPI placement (pure, tested)
    Hotkeys.cs                hotkey settings + "Ctrl+Alt+O" parsing/validation (pure, tested)
    SpotifyPlayerParser.cs    /me/player JSON → snapshot (track, episode, ad, unsupported)
  Lyrics/                     isolated module: ILyricsProvider, LRCLIB provider, disk cache, LRC parser
  Link/                       Mac link: contract (LinkProtocol), crypto (LinkAuth), server (Kestrel), keys, firewall
  App.Link.cs                 the app's side of the link (IDisplayHost over the same commands as the tray)
  Services/                   Spotify (PKCE, DPAPI tokens), artwork, settings, tray, hotkeys, autostart, log, demo
  Views/
    IComposition.cs           the contract a layout implements
    StageView, StripView      the two desktop compositions
    OverlayWindow.cs          the gaming overlay window: no-activate, click-through, topmost, placement, drag
    OverlayView.xaml(.cs)     the overlay card (density, artwork, lyrics, progress)
    LyricsView.cs             shared synced-lyrics control (sheet or strip)
    SetupView, SettingsWindow, Anim.cs
StarryDisplay.Tests/          38 tests over the UI-free core and link contract/crypto (incl. Python-computed vectors)
```

**Adding a composition:** implement `IComposition` on a UserControl, subscribe to the view model's events,
and register it in `MainWindow.CreateComposition`.

**Mac playback hints (not needed today):** the link carries display settings and context, not
playback. If a later phase wants the Mac to *supply* playback, it can implement `IPlaybackSource` and hand it to
`PlaybackController`. The UI only ever sees the view model.
