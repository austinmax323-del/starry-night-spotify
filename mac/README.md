# `starry`: control the Windows display from your Mac

One small Python 3 script with no dependencies. It works with the `python3` from Xcode's command line tools.
It talks to Starry Display on the PC over your local network ([protocol](../protocol/PROTOCOL.md)).
Nothing runs in the background.

## Set up once

```sh
# 1. put it on your PATH (the repo lives in ~/.config/spicetify)
ln -sf ~/.config/spicetify/mac/starry /opt/homebrew/bin/starry   # or /usr/local/bin, ~/bin

# 2. on the PC: Settings → Mac link → turn on, "Allow through firewall…", then "Pair a Mac…"
# 3. run the command the PC shows, e.g.
starry pair zxcvbn.local ABCD-EFGH
```

The pairing key goes into your **Keychain** (service `starry-display`). The host is remembered in
`~/.config/starry-display/config.json`.

## Use

```sh
starry status                     # what the display is doing
starry overlay on                 # gaming overlay (compact, on top, click-through); off = desktop display
starry layout strip               # desktop layout: stage | strip (also leaves the overlay)
starry hide / starry show         # whatever is showing
starry top off                    # always on top (whatever is showing)
starry clickthrough on            # clicks pass through (whatever is showing)
starry mode paused                # stop following Spotify (live to resume)
starry lyrics 1.3                 # lyric size 0.7–1.6
starry accent '#FF88AA'           # or: auto
starry theme Galaxy               # send a Starry Night scheme (accent + sky) to the PC; or: auto
starry context focus=coding --ttl 900
starry watch                      # live events; survives sleep, restarts, Wi-Fi drops
starry raw GET /v1/state          # any endpoint, signed
starry unpair                     # forget on both sides
```

`theme-cycle.sh` calls `starry theme <scheme>` automatically once you're paired, so switching your
Spotify scheme from the terminal re-tints the PC display too.

Showing anything from the Mac never takes focus on the PC, so it's safe mid-game. On the PC itself,
**Ctrl+Alt+O** switches desktop ↔ overlay, **Ctrl+Alt+H** shows/hides, and **Ctrl+Alt+L** toggles click-through
(all changeable in Settings → Hotkeys).

**Tip:** macOS Shortcuts → "Run Shell Script" → `/usr/local/bin/starry layout strip` gives you a
menu-bar or keyboard-shortcut button for any command.

## Troubleshooting

| Message | Fix |
| --- | --- |
| `Can't reach the display` | PC awake? Starry Display running with Mac link on? On the PC, Settings → Mac link should say "Listening on 192.168…". If it says "this PC only", click **Allow through firewall…**. If Portmaster is installed, allow incoming LAN connections for StarryDisplay. |
| `unknown key (pair again)` | The PC forgot this Mac. Run `starry pair` again. |
| `pairing is not open` | Click **Pair a Mac…** on the PC first. The code lasts 3 minutes. |
| `.local` name doesn't resolve | Use the PC's IP (`starry pair 192.168.x.y CODE`), or reserve its address in your router. |
