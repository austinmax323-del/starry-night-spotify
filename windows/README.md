# Starry Display (Windows)

The Windows half of the Starry Night setup: a transparent, always-on-top lyrics and
now-playing display for the PC. It follows whatever Spotify is playing on **any device**
(normally the iPhone), so Spotify doesn't need to run on the PC.

Built on [EvansLR/spotify-lyrics-overlay-wpf](https://github.com/EvansLR/spotify-lyrics-overlay-wpf) (MIT).
See [BASEPLATE.md](BASEPLATE.md) for what was evaluated, reused and changed.

## Device roles

```
iPhone ──plays──▶ Spotify cloud ◀──GET /me/player (PKCE)── Windows PC: Starry Display
                                                              ▲
Mac (Spicetify theme; controller/context source) ─ ─ future ─ ┘ via IPlaybackSource
```

## One-time setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).
2. Create an app at the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard):
   - Redirect URI: `http://127.0.0.1:8766/callback` (exactly; Spotify rejects `localhost`).
   - API: Web API. The account that owns the app needs Premium (Spotify's Development Mode rule).
3. Copy the **Client ID**. There is no client secret: PKCE doesn't use one, and nothing secret goes in this repo.

## Run

```powershell
cd windows\StarryDisplay
dotnet run                       # dev run
dotnet publish -c Release -o ..\dist   # framework-dependent build → windows\dist\StarryDisplay.exe
```

Paste the Client ID and click **Connect**. Your browser opens to approve, then the overlay starts following playback.

| Control | Action |
| --- | --- |
| Drag the window | Move it (when unlocked) |
| **Lock** / `Ctrl+Alt+L` | Click-through mode for gaming; toolbar hides |
| Tray icon, left click | Show/hide (or unlock if locked) |
| Tray menu | Show/Hide · Lock · Quit |
| Style / 2 lines | Font size, colour, one or two lines |

Closing the window hides it to the tray; **Quit** exits.

## Where things live

`%APPDATA%\StarryDisplay\`

| File | Contents |
| --- | --- |
| `settings.json` | Client ID (public by design), layout, font size, colour |
| `token.bin` | Spotify tokens, **encrypted with Windows DPAPI** (only your Windows account can read them) |
| `lyrics-cache.json` | Cached LRCLIB lyrics |
| `app.log` | Startup/errors for troubleshooting; never contains tokens |

**Disconnect** deletes `token.bin`. To revoke fully, remove the app under spotify.com → Account → Apps.

## Architecture (deliberately small)

```
StarryDisplay/
  App.xaml.cs               single instance, crash logging
  MainWindow.xaml(.cs)      transparent topmost window, tray, hotkey, lyric rendering
  NativeMethods.cs          click-through (WS_EX_TRANSPARENT), global hotkey
  Models/Models.cs          TrackInfo, lyrics, settings, token
  Services/
    IPlaybackSource.cs      "what's playing" seam (Web API today; Mac link later)
    SpotifyService.cs       PKCE sign-in + /me/player polling (implements IPlaybackSource)
    LyricsService.cs        LRCLIB lookup + cache
    SettingsStore.cs        JSON settings, DPAPI secrets, atomic writes
    Log.cs                  tiny file log
```

No background services, no Docker, no paid APIs, no Electron. The app runs only while you run it.

**Future Mac↔PC link (not built yet):** add a class that implements `IPlaybackSource`
(for example, a small WebSocket/HTTP listener fed by the Mac over the LAN or Tailscale) and
prefer it over polling when the Mac is connected. The UI already consumes only the interface.
