#!/bin/zsh
# Usage: theme-cycle.sh [scheme]   (no arg = random)
# If the Windows display is paired (mac/starry), it's re-tinted to match. Skipped silently otherwise.
STARRY="${0:A:h}/mac/starry"
cd ~/.config/spicetify/Themes/StarryNight
S=$(grep -o '^\[[^]]*\]' color.ini | tr -d '[]')
pick=${1:-$(echo "$S" | sort -R | head -1)}
spicetify config color_scheme "$pick" >/dev/null && spicetify apply -q && echo "→ $pick"
if [[ -x "$STARRY" && -f ~/.config/starry-display/config.json ]]; then
  "$STARRY" theme "$pick" --quiet 2>/dev/null &!
fi
