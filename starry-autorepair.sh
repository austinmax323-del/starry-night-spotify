#!/bin/zsh
# Called by launchd (com.ab.starry-autorepair): when a Spotify update wipes the Starry Night patch,
# re-apply it with starry-repair.sh and show a notification. Cheap no-op when nothing changed.
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:$PATH"
DIR="$HOME/.config/spicetify"
XPUI="/Applications/Spotify.app/Contents/Resources/Apps/xpui"
LOG="$DIR/autorepair.log"
FAILSTAMP="$DIR/.autorepair-failed"
log() { print -r -- "$(date '+%F %T') $*" >> "$LOG"; }
notify() { osascript -e "display notification \"$2\" with title \"$1\"" >/dev/null 2>&1; }

[[ -d "$XPUI" ]] || exit 0                                   # Spotify not installed / mid-update
grep -q "sfx-" "$XPUI/user.css" 2>/dev/null && exit 0        # still patched: nothing to do

ver=$(defaults read /Applications/Spotify.app/Contents/Info CFBundleShortVersionString 2>/dev/null)
# don't hammer a failing repair: one attempt per Spotify version per hour
if [[ -f "$FAILSTAMP" ]] && grep -q "^$ver " "$FAILSTAMP" && [[ -n $(find "$FAILSTAMP" -mmin -60 2>/dev/null) ]]; then exit 0; fi

# let a finishing update settle (files still being written)
sleep 20
grep -q "sfx-" "$XPUI/user.css" 2>/dev/null && exit 0

log "patch missing on Spotify $ver; running starry-repair.sh"
out=$("$DIR/starry-repair.sh" 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | tr '\r' '\n')
if print -r -- "$out" | grep -q "checks passed"; then
  rm -f "$FAILSTAMP"
  log "repair ok ($ver)"
  notify "Spotify updated" "Starry Night re-applied (v$ver). Restart Spotify if it looks stock."
else
  print -r -- "$ver $(date +%s)" > "$FAILSTAMP"
  log "repair FAILED ($ver): $(print -r -- "$out" | grep -E '✗|problem' | tr '\n' ' ')"
  notify "Starry Night repair failed" "Spotify $ver: run ~/.config/spicetify/starry-repair.sh"
fi
