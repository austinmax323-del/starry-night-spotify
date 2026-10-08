#!/bin/zsh
# Re-apply the Starry Night setup after a Spotify update, then check every piece landed.
# Usage: ~/.config/spicetify/starry-repair.sh
set -u
XPUI="/Applications/Spotify.app/Contents/Resources/Apps/xpui"
ok=0; bad=0
pass() { print -P "%F{green}✓%f $1"; ok=$((ok + 1)); }
fail() { print -P "%F{red}✗%f $1"; bad=$((bad + 1)); }

print "Applying Spicetify…"
out=$(spicetify apply 2>&1)
if print -r -- "$out" | grep -qiE "mismatch|not backed up|run .?spicetify backup"; then
  print "Spotify was updated (or backup is stale) — rebuilding the backup and re-applying…"
  out=$(spicetify restore backup apply 2>&1)
fi
# ignore warnings and the harmless "check for spicetify updates" GitHub call
print -r -- "$out" | grep -viE "warning|release info|GitHub response|rate limit" | grep -iwE "error" && fail "spicetify reported an error (see above)" || pass "spicetify apply"

# Theme + colour scheme
grep -q "sfx-" "$XPUI/user.css" 2>/dev/null && pass "theme styles (user.css)" || fail "theme styles missing from Spotify"
[[ -s "$XPUI/colors.css" ]] && pass "colour scheme" || fail "colour scheme missing"

# Extensions listed in the config must exist inside Spotify
exts=$(grep '^extensions' "$HOME/.config/spicetify/config-xpui.ini" | cut -d= -f2 | tr -d ' ' | tr '|' ' ')
for e in ${=exts}; do
  [[ -f "$XPUI/extensions/$e" ]] && pass "extension $e" || fail "extension $e not installed"
done

# Fonts the theme expects
fonts=$(system_profiler SPFontsDataType 2>/dev/null)
for f in "Big Caslon" "Futura" "Avenir Next"; do
  print -r -- "$fonts" | grep -q "Family: $f" && pass "font $f" || fail "font $f not found"
done

print ""
(( bad == 0 )) && print -P "%F{green}All $ok checks passed.%f Restart Spotify if it is open." \
               || print -P "%F{red}$bad problem(s)%f, $ok ok."
exit $(( bad > 0 ))
