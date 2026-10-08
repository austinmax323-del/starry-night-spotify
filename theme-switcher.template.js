// Theme Switcher: live-swap StarryNight colour schemes from the top bar.
(function themeSwitcher() {
  if (!Spicetify?.Topbar || !Spicetify?.PopupModal) return setTimeout(themeSwitcher, 300);
  const SCHEMES = __SCHEMES__;
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
      ["dust", "Dust & scratches"], ["drift", "Sky drift"], ["planet", "Planet"]];
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
