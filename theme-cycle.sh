#!/bin/zsh
# Usage: theme-cycle.sh [scheme]   (no arg = random)
cd ~/.config/spicetify/Themes/StarryNight
S=$(grep -o '^\[[^]]*\]' color.ini | tr -d '[]')
pick=${1:-$(echo "$S" | sort -R | head -1)}
spicetify config color_scheme "$pick" >/dev/null && spicetify apply -q && echo "→ $pick"
