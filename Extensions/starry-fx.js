// Feature switches (set from the palette menu). Stored as a list of disabled keys.
(function sfxFlags() {
  let off = [];
  try { off = JSON.parse(localStorage.getItem("sfx-off") || "[]"); } catch {}
  const sync = () => { if (document.body) for (const k of off) document.body.classList.add("sfx-off-" + k); else setTimeout(sync, 20); };
  sync();
  window.sfxOn = k => !off.includes(k);
  window.sfxSet = (k, on) => {
    off = off.filter(x => x !== k); if (!on) off.push(k);
    try { localStorage.setItem("sfx-off", JSON.stringify(off)); } catch {}
    document.body.classList.toggle("sfx-off-" + k, !on);
  };
})();

// Starry FX: beat-reactive glow on the vinyl player (--beat 0..1 on :root).
(function starryFx() {
  if (!Spicetify?.Player?.data || !Spicetify.getAudioData) return setTimeout(starryFx, 300);
  let beats = null, uri = null, idx = 0;
  const root = document.documentElement.style;

  async function load() {
    const u = Spicetify.Player.data?.item?.uri;
    if (!u || u === uri) return;
    uri = u; beats = null; idx = 0;
    try { const a = await Spicetify.getAudioData(u); if (a?.beats?.length) beats = a.beats.map(b => b.start * 1000); } catch {}
  }
  Spicetify.Player.addEventListener("songchange", load);
  load();

  let level = 0, last = performance.now();
  function frame(now) {
    const dt = now - last; last = now;
    if (Spicetify.Player.isPlaying()) {
      const t = Spicetify.Player.getProgress();
      if (beats) {
        if (idx && beats[idx - 1] > t + 500) idx = 0; // seeked back
        while (idx < beats.length && beats[idx] <= t) { idx++; level = 1; }
      } else {
        // no beat data: steady ~110 bpm pulse
        const phase = (t % 545) / 545;
        if (phase < 0.06) level = Math.max(level, 0.8);
      }
    }
    level = Math.max(0, level - dt / 260);
    root.setProperty("--beat", level.toFixed(3));
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
})();

// Shooting star from (x, y)
window.sfxShoot = (x, y, d) => shoot(x, y, d);
function shoot(x, y, delay = 0) {
  const ang = 20 + Math.random() * 30; // degrees below horizontal, heading down-left or down-right
  const dir = Math.random() < 0.5 ? 1 : -1;
  const dist = 260 + Math.random() * 200;
  const rad = ang * Math.PI / 180;
  const s = document.createElement("div");
  s.className = "sfx-shoot";
  s.style.left = `${x - 160}px`;
  s.style.top = `${y - 1}px`;
  s.style.animationDelay = `${delay}ms`;
  s.style.setProperty("--r", `${135 + (ang - 35) * 0.3}deg`);
  s.style.setProperty("--dx", `${dist}px`);
  document.body.appendChild(s);
  s.addEventListener("animationend", () => s.remove());
}
// Click on empty sky -> shooting star
document.addEventListener("click", e => {
  if (e.target.closest("button, a, input, textarea, img, [role='row'], [role='button'], [draggable='true'], .cover-art, .main-trackList-row, .main-card-card")) return;
  if (window.sfxOn?.("meteors") !== false) shoot(e.clientX, e.clientY);
}, true);

// Meteor shower when a song gets loud (section loudness jumps)
(function shower() {
  if (!Spicetify?.Player?.data) return setTimeout(shower, 300);
  let sections = null, uri = null, i = 0, cool = 0;
  async function load() {
    const u = Spicetify.Player.data?.item?.uri;
    if (!u || u === uri) return;
    uri = u; sections = null; i = 0;
    try { const a = await Spicetify.getAudioData(u); sections = a?.sections?.map(s => ({ t: s.start * 1000, l: s.loudness })); } catch {}
  }
  Spicetify.Player.addEventListener("songchange", load);
  load();
  setInterval(() => {
    if (!sections || !Spicetify.Player.isPlaying()) return;
    const t = Spicetify.Player.getProgress();
    if (i && sections[i - 1].t > t + 1000) i = 0;
    while (i < sections.length && sections[i].t <= t) {
      const prev = sections[i - 1], cur = sections[i];
      i++;
      if (prev && cur.l - prev.l >= 2.5 && Date.now() > cool) {
        cool = Date.now() + 6000;
        if (window.sfxOn?.("meteors") !== false) for (let k = 0; k < 14; k++) shoot(Math.random() * innerWidth * 0.9 + innerWidth * 0.1, Math.random() * innerHeight * 0.5, k * 90 + Math.random() * 60);
      }
    }
  }, 150);
})();

// Warp speed + record swap on track change
(function warpSwap() {
  if (!Spicetify?.Player?.addEventListener) return setTimeout(warpSwap, 300);
  const loadedAt = Date.now();
  Spicetify.Player.addEventListener("songchange", () => {
    if (Date.now() - loadedAt < 4000) return; // ignore the startup track load
    if (window.sfxOn?.("warp") !== false) warp();
    document.body.classList.remove("sfx-swap"); void document.body.offsetWidth;
    document.body.classList.add("sfx-swap");
    setTimeout(() => document.body.classList.remove("sfx-swap"), 1300);
  });
  window.sfxWarp = warp;
  function warp(count = 220, dur = 700) {
    const c = document.createElement("canvas");
    c.className = "sfx-warp";
    c.width = innerWidth * devicePixelRatio; c.height = innerHeight * devicePixelRatio;
    document.body.appendChild(c);
    const x = c.getContext("2d"), W = c.width, H = c.height, dpr = devicePixelRatio;
    const col = getComputedStyle(document.documentElement).getPropertyValue("--spice-rgb-star").trim() || "255,255,255";
    // same heading as the theme's shooting stars: top-right -> bottom-left
    const dx = -Math.SQRT1_2, dy = Math.SQRT1_2;
    const P = Array.from({ length: count }, () => ({ x: Math.random() * (W + H), y: Math.random() * H * 1.4 - H * 0.6, v: (3 + Math.random() * 7) * dpr }));
    const t0 = performance.now();
    (function draw(now) {
      const k = (now - t0) / dur;
      if (k >= 1) return c.remove();
      x.clearRect(0, 0, W, H);
      x.lineWidth = 1.5 * dpr;
      const speed = Math.sin(k * Math.PI); // ramp up then down
      x.strokeStyle = `rgba(${col},${0.85 * speed})`;
      x.beginPath();
      for (const p of P) {
        p.v *= 1.05;
        p.x += dx * p.v * (0.3 + speed * 2); p.y += dy * p.v * (0.3 + speed * 2);
        const len = p.v * speed * 10;
        x.moveTo(p.x, p.y); x.lineTo(p.x - dx * len, p.y - dy * len);
      }
      x.stroke();
      requestAnimationFrame(draw);
    })(t0);
  }
})();

// Planet textured with the current cover + cinema mode
(function planetCinema() {
  if (!Spicetify?.Player?.data || !Spicetify?.Topbar) return setTimeout(planetCinema, 300);

  const planet = document.createElement("div");
  planet.className = "sfx-planet";
  planet.innerHTML = '<div class="sfx-planet-surface"></div><div class="sfx-planet-ring"></div>';
  document.body.appendChild(planet);
  const surface = planet.firstChild;
  function texture() {
    const it = Spicetify.Player.data?.item;
    const u = it?.images?.[0]?.url || it?.metadata?.image_url;
    if (u) surface.style.backgroundImage = `url("${u.replace("spotify:image:", "https://i.scdn.co/image/")}")`;
  }
  Spicetify.Player.addEventListener("songchange", texture);
  texture();

  const toggle = () => document.body.classList.toggle("sfx-cinema");
  new Spicetify.Topbar.Button("Cinema mode",
    `<svg viewBox="0 0 16 16" width="16" height="16" fill="currentColor"><path d="M1 3h14v10H1zM2.5 4.5v7h11v-7zM6 6l4 2-4 2z"/></svg>`,
    toggle);
  document.addEventListener("keydown", e => {
    if (e.target.closest("input, textarea, [contenteditable='true']")) return;
    if ((e.key === "c" || e.key === "C") && !e.metaKey && !e.ctrlKey && !e.altKey) toggle();
    else if (e.key === "Escape") document.body.classList.remove("sfx-cinema");
  });
})();

// Progress ring around the record (click the ring to seek) + rotating featured shelves
(function ringAndShelves() {
  if (!Spicetify?.Player?.getProgress) return setTimeout(ringAndShelves, 300);
  const NS = "http://www.w3.org/2000/svg";
  const R = 48, C = 2 * Math.PI * R;
  function ensureRing() {
    const art = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art");
    if (!art) return null;
    let svg = art.querySelector(":scope > svg.sfx-ring");
    if (svg) return svg;
    svg = document.createElementNS(NS, "svg");
    svg.setAttribute("class", "sfx-ring");
    svg.setAttribute("viewBox", "0 0 100 100");
    svg.innerHTML = `<circle class="sfx-ring-track" cx="50" cy="50" r="${R}"/><circle class="sfx-ring-fill" cx="50" cy="50" r="${R}" stroke-dasharray="${C}" stroke-dashoffset="${C}"/><circle class="sfx-ring-hit" cx="50" cy="50" r="${R}"/>`;
    svg.querySelector(".sfx-ring-hit").addEventListener("click", e => {
      const b = svg.getBoundingClientRect();
      const a = Math.atan2(e.clientY - (b.top + b.height / 2), e.clientX - (b.left + b.width / 2));
      const frac = ((a + Math.PI / 2) / (2 * Math.PI) + 1) % 1; // 0 at 12 o'clock, clockwise
      Spicetify.Player.seek(Math.round(frac * Spicetify.Player.getDuration()));
      e.stopPropagation();
    });
    art.appendChild(svg);
    return svg;
  }
  (function tick() {
    try {
      const svg = ensureRing();
      if (svg) {
        const d = Spicetify.Player.getDuration() || 1;
        const f = Math.min(1, (Spicetify.Player.getProgress() || 0) / d);
        svg.querySelector(".sfx-ring-fill").setAttribute("stroke-dashoffset", String(C * (1 - f)));
      }
    } catch {} // player not ready yet; keep the loop alive
    requestAnimationFrame(tick);
  })();

  // Shelves: one big featured card that rotates through the row
  function tagShelves() {
    for (const sec of document.querySelectorAll('section[data-testid="component-shelf"]')) {
      const card = sec.querySelector('[data-encore-id="card"]');
      if (!card) continue;
      let g = card.parentElement;
      while (g && g !== sec && getComputedStyle(g).display !== "grid") g = g.parentElement;
      if (!g || g === sec || g.classList.contains("sfx-shelf")) continue;
      g.classList.add("sfx-shelf");
      g.firstElementChild?.classList.add("sfx-feat");
      g.addEventListener("mouseenter", () => (g.dataset.hold = "1"));
      g.addEventListener("mouseleave", () => delete g.dataset.hold);
    }
  }
  setInterval(tagShelves, 1000);
  setInterval(() => {
    for (const g of document.querySelectorAll(".sfx-shelf")) {
      if (g.dataset.hold || !g.isConnected) continue;
      const items = [...g.children];
      const i = items.findIndex(x => x.classList.contains("sfx-feat"));
      items[i]?.classList.remove("sfx-feat");
      const next = items[(i + 1) % items.length];
      next.classList.add("sfx-feat");
      g.scrollTo({ left: Math.max(0, next.offsetLeft - g.offsetLeft - 8), behavior: "smooth" });
    }
  }, 5000);
})();

// Elapsed / total time under the artist + "up next" mini records
(function timesAndQueue() {
  if (!Spicetify?.Player?.getProgress || !Spicetify?.Queue) return setTimeout(timesAndQueue, 300);
  const fmt = ms => { const s = Math.max(0, Math.floor(ms / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; };
  const img = u => (u || "").replace("spotify:image:", "https://i.scdn.co/image/");
  let lastQ = "";
  setInterval(() => {
    try {
      const info = document.querySelector(".Root__now-playing-bar .main-trackInfo-container");
      if (info) {
        let t = info.querySelector(":scope > .sfx-time");
        if (!t) { t = document.createElement("div"); t.className = "sfx-time"; info.appendChild(t); }
        t.textContent = `${fmt(Spicetify.Player.getProgress())} / ${fmt(Spicetify.Player.getDuration())}`;
      }
      const bar = document.querySelector(".Root__now-playing-bar .main-nowPlayingBar-nowPlayingBar");
      if (!bar) return;
      let box = bar.querySelector(":scope > .sfx-next");
      if (!box) { box = document.createElement("div"); box.className = "sfx-next"; bar.appendChild(box); lastQ = ""; }
      const next = (Spicetify.Queue.nextTracks || []).filter(x => x?.contextTrack?.metadata?.title).slice(0, 3);
      const key = next.map(x => x.contextTrack.uri).join();
      if (key === lastQ) return;
      lastQ = key;
      box.innerHTML = next.length ? '<div class="sfx-next-label">Up next</div>' + next.map(x => {
        const m = x.contextTrack.metadata;
        const esc = s => String(s || "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
        return `<div class="sfx-next-item"><div class="sfx-mini"><img src="${esc(img(m.image_url))}"></div><div class="sfx-next-text"><div class="sfx-next-title">${esc(m.title)}</div><div class="sfx-next-artist">${esc(m.artist_name)}</div></div></div>`;
      }).join("") : "";
    } catch {}
  }, 500);
})();


// Like a song -> gold shooting star that stays in the sky as a permanent star
(function likeStars() {
  if (!Spicetify?.Player?.getHeart) return setTimeout(likeStars, 300);
  const KEY = "sfx-like-stars";
  let stars = [];
  try { stars = JSON.parse(localStorage.getItem(KEY) || "[]"); } catch {}
  function layer() {
    const host = document.querySelector(".Root__top-container");
    if (!host) return null;
    let l = host.querySelector(":scope > .sfx-gold-layer");
    if (!l) {
      l = document.createElement("div"); l.className = "sfx-gold-layer";
      host.prepend(l);
      for (const st of stars) dot(l, st);
    }
    return l;
  }
  function dot(l, st, fresh) {
    const d = document.createElement("div");
    d.className = "sfx-gold" + (fresh ? " fresh" : "");
    d.style.left = st.x + "%"; d.style.top = st.y + "%";
    d.style.setProperty("--s", st.s + "px");
    d.title = st.t || "";
    l.appendChild(d);
  }
  function celebrate() {
    if (window.sfxOn?.("gold") === false) return;
    const l = layer(); if (!l) return;
    const st = { x: 8 + Math.random() * 84, y: 6 + Math.random() * 60, s: 3 + Math.random() * 2, t: Spicetify.Player.data?.item?.name };
    const ex = st.x / 100 * innerWidth, ey = st.y / 100 * innerHeight;
    // streak comes in from the top-right, the same heading as the other shooting stars, and lands on the new star
    const s = document.createElement("div");
    s.className = "sfx-gold-streak";
    s.style.left = `${ex - 260}px`; s.style.top = `${ey}px`;
    document.body.appendChild(s);
    s.addEventListener("animationend", () => s.remove());
    setTimeout(() => {
      dot(l, st, true);
      stars.push(st);
      try { localStorage.setItem(KEY, JSON.stringify(stars.slice(-400))); } catch {}
    }, 850);
  }
  window.sfxLikeStar = celebrate;
  let uri = null, liked = null;
  setInterval(() => {
    try {
      layer();
      const u = Spicetify.Player.data?.item?.uri, h = !!Spicetify.Player.getHeart();
      if (u !== uri) { uri = u; liked = h; return; } // new track: just record its state
      if (h && liked === false) celebrate();
      liked = h;
    } catch {}
  }, 400);
})();

// Song "weather": fog / aurora / hype, picked from the track's tempo, loudness and key.
// (Spotify no longer exposes artist genres, so the sky reads the audio itself.)
(function weather() {
  if (!Spicetify?.Player?.data || !Spicetify.getAudioData) return setTimeout(weather, 300);
  let hypeTimer = null, uri = null;
  function host() {
    const h = document.querySelector(".Root__top-container");
    if (!h) return null;
    let w = h.querySelector(":scope > .sfx-weather");
    if (!w) {
      w = document.createElement("div"); w.className = "sfx-weather";
      w.innerHTML = '<div class="fog f1"></div><div class="fog f2"></div><div class="aur a1"></div><div class="aur a2"></div><div class="aur a3"></div>';
      h.prepend(w);
    }
    return w;
  }
  async function update() {
    const u = Spicetify.Player.data?.item?.uri;
    if (!u || u === uri) return;
    uri = u;
    let mood = "clear";
    try {
      const t = (await Spicetify.getAudioData(u))?.track;
      if (t) {
        if (t.tempo >= 128 || t.loudness > -6.5) mood = "hype";
        else if (t.loudness < -11 || t.tempo < 82) mood = "aurora";
        else if (t.mode === 0 || t.tempo < 105) mood = "fog";
      }
    } catch {}
    const w = host(); if (!w) return;
    w.dataset.mood = mood;
    document.body.dataset.sfxMood = mood;
    clearInterval(hypeTimer);
    if (mood === "hype" && window.sfxShoot) hypeTimer = setInterval(() => {
      if (Spicetify.Player.isPlaying() && window.sfxOn?.("meteors") !== false) window.sfxShoot(Math.random() * innerWidth, Math.random() * innerHeight * 0.5);
    }, 1800);
  }
  Spicetify.Player.addEventListener("songchange", () => setTimeout(update, 200));
  setInterval(() => { host(); update(); }, 1500);
})();

// Page transitions: zoom through the stars
(function pageTransitions() {
  if (!Spicetify?.Platform?.History?.listen) return setTimeout(pageTransitions, 300);
  let last = Spicetify.Platform.History.location?.pathname;
  Spicetify.Platform.History.listen(loc => {
    if (loc?.pathname === last) return;
    last = loc?.pathname;
    const v = document.querySelector(".Root__main-view");
    if (!v) return;
    v.classList.remove("sfx-page-in"); void v.offsetWidth; v.classList.add("sfx-page-in");
    if (window.sfxOn?.("warp") !== false) window.sfxWarp?.(90, 480);
  });
})();

// Dust + scratches on the record: generated once, spins with the disc, catches the light under the sheen
(function recordDust() {
  const c = document.createElement("canvas");
  const S = 640; c.width = c.height = S;
  const x = c.getContext("2d"), m = S / 2;
  for (let i = 0; i < 1400; i++) {              // specks
    const a = Math.random() * Math.PI * 2, r = (0.24 + Math.random() * 0.76) * m;
    const sz = Math.random() < 0.9 ? 0.6 + Math.random() * 0.8 : 1.4 + Math.random() * 1.6;
    x.fillStyle = `rgba(255,255,255,${0.5 + Math.random() * 0.5})`;
    x.beginPath(); x.arc(m + Math.cos(a) * r, m + Math.sin(a) * r, sz, 0, Math.PI * 2); x.fill();
  }
  for (let i = 0; i < 26; i++) {                // hairline scratches, mostly following the grooves
    const r = (0.28 + Math.random() * 0.7) * m, a0 = Math.random() * Math.PI * 2, len = 0.05 + Math.random() * 0.5;
    x.strokeStyle = `rgba(255,255,255,${0.3 + Math.random() * 0.4})`;
    x.lineWidth = 0.5 + Math.random() * 0.7;
    x.beginPath(); x.arc(m, m, r, a0, a0 + len); x.stroke();
  }
  for (let i = 0; i < 4; i++) {                 // a few straight cross-groove scuffs
    const a = Math.random() * Math.PI * 2, r = (0.4 + Math.random() * 0.5) * m;
    const px = m + Math.cos(a) * r, py = m + Math.sin(a) * r, d = 10 + Math.random() * 30, t = a + Math.PI / 2 + (Math.random() - 0.5);
    x.strokeStyle = "rgba(255,255,255,.22)"; x.lineWidth = 0.6;
    x.beginPath(); x.moveTo(px, py); x.lineTo(px + Math.cos(t) * d, py + Math.sin(t) * d); x.stroke();
  }
  const url = c.toDataURL("image/png");
  setInterval(() => {
    const art = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art");
    if (!art || art.querySelector(":scope > .sfx-dust")) return;
    const d = document.createElement("div");
    d.className = "sfx-dust";
    d.innerHTML = `<div class="sfx-dust-spin" style="background-image:url(${url})"></div>`;
    art.appendChild(d);
  }, 700);
})();

// Slow camera drift: a gentle Lissajous path, ~2.5 min per loop
(function drift() {
  const root = document.documentElement.style, t0 = performance.now();
  setInterval(() => {
    const t = (performance.now() - t0) / 1000;
    root.setProperty("--sfx-dx", `${(Math.sin(t * 2 * Math.PI / 150) * 2.0).toFixed(3)}vw`);
    root.setProperty("--sfx-dy", `${(Math.sin(t * 2 * Math.PI / 110 + 1) * 1.4).toFixed(3)}vh`);
  }, 100);
})();

// The player loads a 64px cover; swap in the 640px version so the record label stays sharp
setInterval(() => {
  const img = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art img");
  if (img && /ab67616d0000(4851|1e02)/.test(img.src)) {
    img.src = img.src.replace(/ab67616d0000(4851|1e02)/, "ab67616d0000b273");
    img.removeAttribute("srcset");
  }
}, 600);

// Artist pages: a "More" pill that reveals the hidden sections (fans also like, appears on, ...)
setInterval(() => {
  const page = document.querySelector('[data-testid="artist-page"]');
  if (!page) { document.body.classList.remove("sfx-artist-more"); return; }
  if (page.querySelector(".sfx-more-pill")) return;
  const hidden = page.querySelectorAll('section[aria-label="Fans also like"], section[aria-label="Appears On"], section[aria-label="Discovered on"]');
  if (!hidden.length) return;
  const b = document.createElement("button");
  b.className = "sfx-more-pill";
  const label = () => (b.textContent = document.body.classList.contains("sfx-artist-more") ? "Less" : "More");
  label();
  b.onclick = () => { document.body.classList.toggle("sfx-artist-more"); label(); };
  page.appendChild(b);
}, 800);
