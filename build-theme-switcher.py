#!/usr/bin/env python3
"""Regenerate Extensions/theme-switcher.js from Themes/StarryNight/color.ini."""
import json, os, re
base = os.path.expanduser("~/.config/spicetify")
schemes, cur = {}, None
for line in open(f"{base}/Themes/StarryNight/color.ini"):
    line = line.split(";")[0].strip()
    if m := re.match(r"\[(.+)\]", line):
        cur = schemes.setdefault(m[1], {})
    elif "=" in line and cur is not None:
        k, v = (s.strip() for s in line.split("=", 1))
        cur[k] = v
js = open(f"{base}/theme-switcher.template.js").read().replace("__SCHEMES__", json.dumps(schemes))
open(f"{base}/Extensions/theme-switcher.js", "w").write(js)
print(f"{len(schemes)} schemes")
