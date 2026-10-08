// Theme Switcher: live-swap StarryNight colour schemes from the top bar.
(function themeSwitcher() {
  if (!Spicetify?.Topbar || !Spicetify?.PopupModal) return setTimeout(themeSwitcher, 300);
  const SCHEMES = {"Base": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "152238", "card": "152238", "sidebar": "142b44", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "ADB5BD", "button-active": "FFF3C4", "button": "FFF3C4", "button-disabled": "000000", "highlight": "191919", "highlight-elevated": "152238", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "4687d6", "tab-active": "333333", "player": "181818"}, "Cotton-candy": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "9f45b0", "card": "9f45b0", "sidebar": "509be1", "sidebar-alt": "ff71b2", "text": "FFFFFF", "subtext": "fff4f4", "button-active": "d3e9ff", "button": "d3e9ff", "button-disabled": "FFFFFF", "highlight": "a763b6", "highlight-elevated": "7f78be", "shadow": "000000", "selected-row": "ffa0ad", "misc": "7F7F7F", "notification-error": "E22134", "notification": "4687d6", "tab-active": "333333", "player": "181818"}, "Forest": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "011502", "card": "011502", "sidebar": "14442b", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "ADB5BD", "button-active": "9893DA", "button": "c4c6ff", "button-disabled": "000000", "highlight": "191919", "highlight-elevated": "011502", "shadow": "000000", "selected-row": "FFFFFF", "misc": "DBF9F4", "notification-error": "E22134", "notification": "77be80", "tab-active": "333333", "player": "181818"}, "Galaxy": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "9f45b0", "card": "9f45b0", "sidebar": "b133c9", "sidebar-alt": "00076f", "text": "ffe4f2", "subtext": "FFFFFF", "button-active": "FFF3C4", "button": "FFF3C4", "button-disabled": "939bb6", "highlight": "9d00ff", "highlight-elevated": "9d00ff", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "4687d6", "tab-active": "333333", "player": "181818"}, "Orange": {"star": "ffe234", "star-glow": "fff3ad", "shooting-star": "fff099", "shooting-star-glow": "fffcea", "main": "000000", "main-elevated": "e69138", "card": "c37728", "sidebar": "e69138", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "FFFFFF", "button-active": "e06666", "button": "fbe39b", "button-disabled": "000000", "highlight": "191919", "highlight-elevated": "e69138", "shadow": "000000", "selected-row": "FFFFFF", "misc": "f9f7db", "notification-error": "E22134", "notification": "e69138", "tab-active": "333333", "player": "181818"}, "Sky": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "6b94f5", "card": "6b94f5", "sidebar": "62cff4", "sidebar-alt": "1e48a9", "text": "FFFFFF", "subtext": "040a18", "button-active": "FFF3C4", "button": "FFF3C4", "button-disabled": "000000", "highlight": "95b3f8", "highlight-elevated": "aac2f9", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "4687d6", "tab-active": "333333", "player": "181818"}, "Sunrise": {"star": "FFFFFF", "star-glow": "FFFFFF", "shooting-star": "FFFFFF", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "C49C48", "card": "C49C48", "sidebar": "F83D41", "sidebar-alt": "FFAE41", "text": "FFFFFF", "subtext": "E0E0E0", "button-active": "FFF3C4", "button": "FFF3C4", "button-disabled": "000000", "highlight": "191919", "highlight-elevated": "C49C48", "shadow": "000000", "selected-row": "000000", "misc": "7F7F7F", "notification-error": "E22134", "notification": "4687d6", "tab-active": "333333", "player": "181818"}, "Synthwave": {"star": "ff9ef5", "star-glow": "ff9ef5", "shooting-star": "ff9ef5", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "2d0b4e", "card": "2d0b4e", "sidebar": "ff2a6d", "sidebar-alt": "0b0221", "text": "FFFFFF", "subtext": "f5c2ff", "button-active": "05d9e8", "button": "05d9e8", "button-disabled": "000000", "highlight": "3a0f63", "highlight-elevated": "2d0b4e", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff2a6d", "tab-active": "333333", "player": "181818"}, "Aurora": {"star": "b8fff1", "star-glow": "b8fff1", "shooting-star": "b8fff1", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "0c2a33", "card": "0c2a33", "sidebar": "0f5e4f", "sidebar-alt": "020b1a", "text": "FFFFFF", "subtext": "a7e8d8", "button-active": "5dffb0", "button": "5dffb0", "button-disabled": "000000", "highlight": "123a40", "highlight-elevated": "0c2a33", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "43e8b0", "tab-active": "333333", "player": "181818"}, "Bloodmoon": {"star": "ffd1c2", "star-glow": "ffd1c2", "shooting-star": "ffd1c2", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "2a0606", "card": "2a0606", "sidebar": "5c0a0a", "sidebar-alt": "050000", "text": "FFFFFF", "subtext": "e0a8a0", "button-active": "ff4d3d", "button": "ff4d3d", "button-disabled": "000000", "highlight": "3d0b0b", "highlight-elevated": "2a0606", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff4d3d", "tab-active": "333333", "player": "181818"}, "Vaporwave": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "6a3fa0", "card": "6a3fa0", "sidebar": "01cdfe", "sidebar-alt": "ff71ce", "text": "FFFFFF", "subtext": "fff0ff", "button-active": "fffb96", "button": "fffb96", "button-disabled": "000000", "highlight": "8250c0", "highlight-elevated": "6a3fa0", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "b967ff", "tab-active": "333333", "player": "181818"}, "Matrix": {"star": "9dff9d", "star-glow": "9dff9d", "shooting-star": "9dff9d", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "021a08", "card": "021a08", "sidebar": "012b0e", "sidebar-alt": "000000", "text": "d6ffd6", "subtext": "63c46a", "button-active": "00ff41", "button": "00ff41", "button-disabled": "000000", "highlight": "053312", "highlight-elevated": "021a08", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "00ff41", "tab-active": "333333", "player": "181818"}, "Deep-ocean": {"star": "8fd8ff", "star-glow": "8fd8ff", "shooting-star": "8fd8ff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "001d3d", "card": "001d3d", "sidebar": "003566", "sidebar-alt": "000814", "text": "FFFFFF", "subtext": "9fbfdf", "button-active": "ffc300", "button": "ffc300", "button-disabled": "000000", "highlight": "00284d", "highlight-elevated": "001d3d", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ffd60a", "tab-active": "333333", "player": "181818"}, "Lavender-haze": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "4a3270", "card": "4a3270", "sidebar": "b388eb", "sidebar-alt": "2b1a4a", "text": "FFFFFF", "subtext": "e6d9ff", "button-active": "ffd6f5", "button": "ffd6f5", "button-disabled": "000000", "highlight": "5c4085", "highlight-elevated": "4a3270", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "c79bff", "tab-active": "333333", "player": "181818"}, "Golden-hour": {"star": "fff2c7", "star-glow": "fff2c7", "shooting-star": "fff2c7", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "3d1f3a", "card": "3d1f3a", "sidebar": "ff8c42", "sidebar-alt": "1a0b2e", "text": "FFFFFF", "subtext": "ffd9b0", "button-active": "ffcf56", "button": "ffcf56", "button-disabled": "000000", "highlight": "522a45", "highlight-elevated": "3d1f3a", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff8c42", "tab-active": "333333", "player": "181818"}, "Nord-night": {"star": "eceff4", "star-glow": "eceff4", "shooting-star": "eceff4", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "2e3440", "card": "2e3440", "sidebar": "3b4252", "sidebar-alt": "1b1f27", "text": "eceff4", "subtext": "b6bfcd", "button-active": "88c0d0", "button": "88c0d0", "button-disabled": "000000", "highlight": "3b4252", "highlight-elevated": "2e3440", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "81a1c1", "tab-active": "333333", "player": "181818"}, "Toxic": {"star": "e8ff9a", "star-glow": "e8ff9a", "shooting-star": "e8ff9a", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "1a2400", "card": "1a2400", "sidebar": "2f4a00", "sidebar-alt": "0a0a0a", "text": "f5ffe0", "subtext": "b8d27a", "button-active": "c6ff00", "button": "c6ff00", "button-disabled": "000000", "highlight": "2a3a05", "highlight-elevated": "1a2400", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "c6ff00", "tab-active": "333333", "player": "181818"}, "Sakura": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "4a1d35", "card": "4a1d35", "sidebar": "ff9fc4", "sidebar-alt": "2a0f1f", "text": "FFFFFF", "subtext": "ffd3e4", "button-active": "ffe3ee", "button": "ffe3ee", "button-disabled": "000000", "highlight": "5e2744", "highlight-elevated": "4a1d35", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff7aa8", "tab-active": "333333", "player": "181818"}, "Ice": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "1a3550", "card": "1a3550", "sidebar": "a8dcff", "sidebar-alt": "0a1a2a", "text": "FFFFFF", "subtext": "d4ecff", "button-active": "e0f7ff", "button": "e0f7ff", "button-disabled": "000000", "highlight": "244566", "highlight-elevated": "1a3550", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "7cc7ff", "tab-active": "333333", "player": "181818"}, "Ember": {"star": "ffb37a", "star-glow": "ffb37a", "shooting-star": "ffb37a", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "1f0a02", "card": "1f0a02", "sidebar": "3a1200", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "e8b49a", "button-active": "ff6b1a", "button": "ff6b1a", "button-disabled": "000000", "highlight": "361405", "highlight-elevated": "1f0a02", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff6b1a", "tab-active": "333333", "player": "181818"}, "Cyberpunk": {"star": "fcee0a", "star-glow": "fcee0a", "shooting-star": "fcee0a", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "1a0f2e", "card": "1a0f2e", "sidebar": "00f0ff", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "c8f8ff", "button-active": "fcee0a", "button": "fcee0a", "button-disabled": "000000", "highlight": "2a1a45", "highlight-elevated": "1a0f2e", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff003c", "tab-active": "333333", "player": "181818"}, "Midnight-mono": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "111111", "card": "111111", "sidebar": "1c1c1c", "sidebar-alt": "000000", "text": "FFFFFF", "subtext": "9a9a9a", "button-active": "ffffff", "button": "ffffff", "button-disabled": "000000", "highlight": "1e1e1e", "highlight-elevated": "111111", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "bbbbbb", "tab-active": "333333", "player": "181818"}, "Nebula": {"star": "ffd6ff", "star-glow": "ffd6ff", "shooting-star": "ffd6ff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "220a3d", "card": "220a3d", "sidebar": "5b1a8f", "sidebar-alt": "07001a", "text": "FFFFFF", "subtext": "dcc4f5", "button-active": "ff7eb6", "button": "ff7eb6", "button-disabled": "000000", "highlight": "331257", "highlight-elevated": "220a3d", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ff7eb6", "tab-active": "333333", "player": "181818"}, "Tropical": {"star": "ffffff", "star-glow": "ffffff", "shooting-star": "ffffff", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "0a4a5c", "card": "0a4a5c", "sidebar": "00b4a6", "sidebar-alt": "003049", "text": "FFFFFF", "subtext": "c9f2ec", "button-active": "ffd166", "button": "ffd166", "button-disabled": "000000", "highlight": "0f5c6e", "highlight-elevated": "0a4a5c", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "ef476f", "tab-active": "333333", "player": "181818"}, "Dracula": {"star": "f8f8f2", "star-glow": "f8f8f2", "shooting-star": "f8f8f2", "shooting-star-glow": "FFFFFF", "main": "000000", "main-elevated": "282a36", "card": "282a36", "sidebar": "44475a", "sidebar-alt": "191a21", "text": "f8f8f2", "subtext": "bdc1d6", "button-active": "ff79c6", "button": "ff79c6", "button-disabled": "000000", "highlight": "343746", "highlight-elevated": "282a36", "shadow": "000000", "selected-row": "FFFFFF", "misc": "7F7F7F", "notification-error": "E22134", "notification": "bd93f9", "tab-active": "333333", "player": "181818"}};
  const KEY = "starry-scheme";
  const rgb = h => [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)).join(",");

  // "Album-art": live scheme pulled from the current cover
  const dark = (h, f) => h.replace(/^#/, "").match(/../g).map(x => Math.round(parseInt(x, 16) * f).toString(16).padStart(2, "0")).join("");
  async function coverColors() {
    const it = Spicetify.Player.data?.item;
    const u = it?.images?.[0]?.url || it?.metadata?.image_url;
    if (!u) return null;
    const img = new Image();
    img.crossOrigin = "anonymous";
    img.src = u.replace("spotify:image:", "https://i.scdn.co/image/");
    await img.decode();
    const c = document.createElement("canvas"); c.width = c.height = 24;
    const x = c.getContext("2d"); x.drawImage(img, 0, 0, 24, 24);
    const d = x.getImageData(0, 0, 24, 24).data;
    let best = null, bestScore = -1, sum = [0, 0, 0];
    for (let i = 0; i < d.length; i += 4) {
      const [r, g, b] = [d[i], d[i + 1], d[i + 2]];
      sum[0] += r; sum[1] += g; sum[2] += b;
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
      const score = (mx - mn) * (mx / 255); // saturation x brightness
      if (score > bestScore) { bestScore = score; best = [r, g, b]; }
    }
    const hex = a => a.map(v => Math.round(v).toString(16).padStart(2, "0")).join("");
    const avg = sum.map(v => v / 576);
    const lift = a => { const m = Math.max(...a, 1); return a.map(v => Math.min(255, v * 235 / m)); };
    return { v: hex(best), dv: hex(avg), lv: hex(lift(best)) };
  }
  async function albumScheme() {
    const k = await coverColors().catch(() => null);
    if (!k) return null;
    const { v, dv, lv } = k;
    return { star: dark(lv, 1), "star-glow": dark(lv, 1), "shooting-star": dark(lv, 1), "shooting-star-glow": "FFFFFF",
      "sidebar-alt": dark(dv, 0.15), sidebar: dark(dv, 0.55), "main-elevated": dark(dv, 0.35), card: dark(dv, 0.35),
      text: "FFFFFF", subtext: "D0D0D0", button: dark(lv, 1), "button-active": dark(lv, 1),
      highlight: dark(dv, 0.45), "highlight-elevated": dark(dv, 0.35), notification: dark(v, 1) };
  }
  SCHEMES["Album-art"] = { "sidebar-alt": "222222", sidebar: "888888" };
  Spicetify.Player.addEventListener("songchange", () => {
    try { if (localStorage.getItem(KEY) === "Album-art") setTimeout(() => apply("Album-art"), 300); } catch {}
  });

  async function apply(name) {
    const s = name === "Album-art" ? await albumScheme() : SCHEMES[name];
    if (!s) return;
    const root = document.documentElement.style;
    for (const [k, v] of Object.entries(s)) {
      if (k === "main") continue; // StarryNight keeps main transparent
      root.setProperty(`--spice-${k}`, `#${v}`);
      root.setProperty(`--spice-rgb-${k}`, rgb(v));
    }
    document.querySelectorAll('div[style*="z-index: -1"][style*="border-radius: 50%"]')
      .forEach(el => (el.style.backgroundColor = `#${s.star}`));
    try { localStorage.setItem(KEY, name); } catch {}
  }

  function open() {
    const cur = (() => { try { return localStorage.getItem(KEY); } catch { return null; } })();
    const grid = document.createElement("div");
    grid.style.cssText = "display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:10px";
    for (const [name, s] of Object.entries(SCHEMES)) {
      const b = document.createElement("button");
      b.textContent = name.replace(/-/g, " ");
      b.style.cssText = `height:80px;border-radius:10px;border:2px solid ${name === cur ? "#fff" : "transparent"};
        background:${name === "Album-art" ? "conic-gradient(#f5576c,#f7b733,#43e97b,#4facfe,#a18cd1,#f5576c)" : `linear-gradient(#${s["sidebar-alt"]},#${s.sidebar})`};color:#fff;font-weight:700;
        text-shadow:0 1px 3px #000;cursor:pointer;display:flex;align-items:flex-end;padding:8px`;
      b.onclick = () => { apply(name); Spicetify.PopupModal.hide(); Spicetify.showNotification?.(`Theme: ${name}`); };
      grid.appendChild(b);
    }
    const wrap = document.createElement("div");
    wrap.appendChild(grid);
    const FX = [["intro", "Startup intro"], ["warp", "Warp on skip & page change"], ["meteors", "Shooting stars & meteor showers"],
      ["gold", "Gold star when you like a song"], ["weather", "Song weather"], ["glow", "Beat glow"], ["swap", "Record swap"],
      ["dust", "Dust & scratches"], ["planet", "Planet"], ["const", "Artist constellations"]];
    const head = document.createElement("div");
    head.textContent = "Effects";
    head.style.cssText = "margin:26px 0 10px;font-family:Futura,sans-serif;font-size:12px;letter-spacing:.25em;text-transform:uppercase;opacity:.7";
    wrap.appendChild(head);
    const list = document.createElement("div");
    list.style.cssText = "display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:8px";
    for (const [k, label] of FX) {
      const row = document.createElement("label");
      row.style.cssText = "display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 14px;border-radius:12px;background:rgba(255,255,255,.05);cursor:pointer";
      const on = window.sfxOn ? window.sfxOn(k) : true;
      row.innerHTML = `<span>${label}</span><input type="checkbox" ${on ? "checked" : ""} style="width:18px;height:18px;accent-color:#fff;cursor:pointer">`;
      row.querySelector("input").onchange = e => window.sfxSet?.(k, e.target.checked);
      list.appendChild(row);
    }
    wrap.appendChild(list);
    Spicetify.PopupModal.display({ title: "Starry Night schemes", content: wrap, isLarge: true });
  }

  new Spicetify.Topbar.Button("Themes",
    `<svg viewBox="0 0 16 16" width="16" height="16" fill="currentColor"><path d="M8 1a7 7 0 1 0 0 14c.8 0 1.3-.6 1.3-1.3 0-.4-.1-.7-.4-1-.2-.3-.4-.6-.4-1 0-.7.6-1.3 1.3-1.3H11a4 4 0 0 0 4-4C15 3.6 11.9 1 8 1zM4 8a1 1 0 1 1 0-2 1 1 0 0 1 0 2zm2-3a1 1 0 1 1 0-2 1 1 0 0 1 0 2zm4 0a1 1 0 1 1 0-2 1 1 0 0 1 0 2zm2 3a1 1 0 1 1 0-2 1 1 0 0 1 0 2z"/></svg>`,
    open);

  // re-apply the saved scheme; Album-art waits until a track is loaded
  (function restore(n = 0) {
    let saved = null;
    try { saved = localStorage.getItem(KEY); } catch {}
    if (!saved) return;
    if (saved === "Album-art" && !Spicetify.Player.data?.item && n < 60) return setTimeout(() => restore(n + 1), 500);
    apply(saved);
  })();
})();
