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

// One scheduler for every DOM poller: a single rAF loop instead of ~10 setIntervals,
// and nothing runs while Spotify is hidden or minimised.
(function sfxScheduler() {
  const tasks = [];
  window.sfxEvery = (fn, ms) => tasks.push({ fn, ms, next: 0 });
  setInterval(() => {
    if (document.hidden) return;
    const now = performance.now();
    for (const t of tasks) if (now >= t.next) { t.next = now + t.ms; try { t.fn(); } catch {} }
  }, 100);
})();

// Starry FX: beat-reactive glow on the vinyl player (--beat 0..1 on :root).
(function starryFx() {
  if (!Spicetify?.Player?.data || !Spicetify.getAudioData) return setTimeout(starryFx, 300);
  let beats = null, uri = null, idx = 0;
  // --beat goes on the few elements that read it. Setting it on :root restyled the whole page every frame.
  let last3 = "";
  const setBeat = v => {
    if (v === last3) return; last3 = v;
    const art = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art");
    if (art && !art.querySelector(":scope > .sfx-glow")) { const g = document.createElement("div"); g.className = "sfx-glow"; art.prepend(g); }
    // .starrynight-bg-container holds ~400 stars and --beat inherits into all of them, so only feed it when the
    // hype weather actually uses it; the glow layer has no children and is cheap to update
    document.querySelector(".sfx-glow")?.style.setProperty("--beat", v);
    const sky = document.querySelector(".starrynight-bg-container");
    if (sky) {
      if (document.body.dataset.sfxMood === "hype" && window.sfxOn?.("weather") !== false) sky.style.setProperty("--beat", v);
      else if (sky.style.getPropertyValue("--beat")) sky.style.removeProperty("--beat");
    }
  };

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
    setBeat(level < 0.01 ? "0" : level.toFixed(2));
    // ~30 fps while playing; 4 checks a second while paused or hidden
    if (Spicetify.Player.isPlaying() && !document.hidden) setTimeout(() => requestAnimationFrame(frame), 28);
    else setTimeout(() => frame(performance.now()), 250);
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
  sfxEvery(() => {
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
    setTimeout(tick, 250);
  })();

  // Shelves: one big featured card that rotates through the row
  function tagShelves() {
    for (const sec of document.querySelectorAll('section[data-testid="component-shelf"]:not([data-sfx-shelf])')) {
      const card = sec.querySelector('[data-encore-id="card"]');
      if (card) sec.dataset.sfxShelf = "1"; // computed-style walk below runs once per shelf, not every second
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
  sfxEvery(tagShelves, 1000);
  sfxEvery(() => {
    for (const g of document.querySelectorAll(".sfx-shelf")) {
      if (g.dataset.hold || !g.isConnected) continue;
      const items = [...g.children];
      const i = items.findIndex(x => x.classList.contains("sfx-feat"));
      items[i]?.classList.remove("sfx-feat");
      const next = items[(i + 1) % items.length];
      next.classList.add("sfx-feat");
      // keep the featured card at the front (CSS order) so the row never keeps its tall height with the big card off-screen
      g.scrollTo({ left: 0, behavior: "smooth" });
    }
  }, 5000);
})();

// Elapsed / total time under the artist + "up next" mini records
(function timesAndQueue() {
  if (!Spicetify?.Player?.getProgress || !Spicetify?.Queue) return setTimeout(timesAndQueue, 300);
  const fmt = ms => { const s = Math.max(0, Math.floor(ms / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; };
  const img = u => (u || "").replace("spotify:image:", "https://i.scdn.co/image/");
  let lastQ = "";
  sfxEvery(() => {
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
    const x = st.x > 66 ? 8 + (st.x - 8) * 0.62 : st.x; // keep clear of the record/controls column
    d.style.left = x + "%"; d.style.top = st.y + "%";
    d.style.setProperty("--s", st.s + "px");
    d.title = st.t || "";
    l.appendChild(d);
  }
  function sparks() {
    // the visible check/like button next to the title (the first button in that block is a hidden 0-width one)
    const b = [...document.querySelectorAll(".Root__now-playing-bar .main-nowPlayingWidget-nowPlaying button")].find(x => x.getBoundingClientRect().width > 0);
    const r = b?.getBoundingClientRect();
    if (!r || !r.width) return;
    const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
    for (let k = 0; k < 14; k++) {
      const s = document.createElement("div");
      s.className = "sfx-spark";
      const a = Math.random() * Math.PI * 2, d = 18 + Math.random() * 34;
      s.style.left = cx + "px"; s.style.top = cy + "px";
      s.style.setProperty("--x", Math.cos(a) * d + "px"); s.style.setProperty("--y", Math.sin(a) * d + "px");
      s.style.animationDelay = Math.random() * 80 + "ms";
      document.body.appendChild(s);
      s.addEventListener("animationend", () => s.remove());
    }
  }
  function celebrate() {
    if (window.sfxOn?.("gold") === false) return;
    sparks();
    const l = layer(); if (!l) return;
    const st = { x: 8 + Math.random() * 58, y: 6 + Math.random() * 60, s: 3 + Math.random() * 2, t: Spicetify.Player.data?.item?.name };
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
  sfxEvery(() => {
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
  sfxEvery(() => { host(); update(); }, 1500);
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
  sfxEvery(() => {
    const art = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art");
    if (!art || art.querySelector(":scope > .sfx-dust")) return;
    const d = document.createElement("div");
    d.className = "sfx-dust";
    d.innerHTML = `<div class="sfx-dust-spin" style="background-image:url(${url})"></div>`;
    art.appendChild(d);
  }, 700);
})();

// Slow camera drift: now a pure CSS animation (see user.css)

// The player loads a 64px cover; swap in the 640px version so the record label stays sharp
sfxEvery(() => {
  const img = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art img");
  if (img && /ab67616d0000(4851|1e02)/.test(img.src)) {
    img.src = img.src.replace(/ab67616d0000(4851|1e02)/, "ab67616d0000b273");
    img.removeAttribute("srcset");
  }
}, 600);

// Artist pages: a "More" pill that reveals the hidden sections (fans also like, appears on, ...)
sfxEvery(() => {
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

// Home shelves as numbered chapters: "01 — Recommended Stations"
sfxEvery(() => {
  let n = 0;
  for (const sec of document.querySelectorAll('section[data-testid="component-shelf"]')) {
    const h = sec.querySelector("h2");
    if (!h) continue;
    n++;
    const ch = String(n).padStart(2, "0");
    if (h.dataset.ch !== ch) h.dataset.ch = ch;
  }
}, 800);

// ---------- Artist pages: poster treatment ----------
(function artistPoster() {
  const STAR = '<path d="M12 2.2l2.9 6.3 6.9.7-5.2 4.6 1.5 6.8L12 17.1l-6.1 3.5 1.5-6.8L2.2 9.2l6.9-.7z"/>';
  let tintFor = null;
  function tintLayer() {
    const h = document.querySelector(".Root__top-container");
    if (!h) return null;
    let t = h.querySelector(":scope > .sfx-artist-tint");
    if (!t) { t = document.createElement("div"); t.className = "sfx-artist-tint"; h.prepend(t); }
    return t;
  }
  async function sample(url) {
    const img = new Image(); img.crossOrigin = "anonymous"; img.src = url;
    await img.decode();
    const c = document.createElement("canvas"); c.width = c.height = 24;
    const x = c.getContext("2d"); x.drawImage(img, 0, 0, 24, 24);
    const d = x.getImageData(0, 0, 24, 24).data;
    let best = [120, 120, 140], score = -1;
    for (let i = 0; i < d.length; i += 4) {
      const mx = Math.max(d[i], d[i + 1], d[i + 2]), mn = Math.min(d[i], d[i + 1], d[i + 2]);
      const sc = (mx - mn) * mx;
      if (sc > score) { score = sc; best = [d[i], d[i + 1], d[i + 2]]; }
    }
    return best.join(",");
  }
  window.sfxEvery(() => {
    const page = document.querySelector('[data-testid="artist-page"]');
    const tint = tintLayer();
    if (!page) { if (tint) tint.classList.remove("on"); tintFor = null; return; }
    document.body.classList.add("sfx-on-artist");
    // monthly listeners -> small caps label
    for (const el of document.querySelectorAll(".main-entityHeader-headerText span, .main-entityHeader-headerText div")) {
      if (el.children.length === 0 && /monthly listeners/i.test(el.textContent)) el.classList.add("sfx-listeners");
    }
    // verified badge -> gold star
    for (const b of document.querySelectorAll('.main-view-container svg[data-encore-id="verifiedBadge"]:not(.sfx-star)')) {
      b.classList.add("sfx-star"); b.innerHTML = STAR + "<title>Verified</title>";
    }
    // sky tint from the artist photo
    // banner photo if the artist has one, otherwise the round profile picture
    const bg = document.querySelector('.main-view-container [data-testid="background-image"]');
    const m = bg && getComputedStyle(bg).backgroundImage.match(/url\("?(.*?)"?\)/);
    const src = m?.[1] || document.querySelector(".main-entityHeader-container img")?.src;
    if (src && src !== tintFor && tint) {
      tintFor = src;
      sample(src).then(rgb => { tint.style.setProperty("--tint", rgb); tint.classList.add("on"); }).catch(() => {});
    }
  }, 700);
  window.sfxEvery(() => { if (!document.querySelector('[data-testid="artist-page"]')) document.body.classList.remove("sfx-on-artist"); }, 700);
})();

// ---------- Dock slides away while scrolling ----------
(function dockOnScroll() {
  // React to the user scrolling the main view only (wheel / trackpad / keys). Programmatic scrolls,
  // like the shelf carousel advancing every 5s, used to fire "scroll" and hide the dock at random.
  let t = null;
  const hide = e => {
    if (!e.target.closest?.(".Root__main-view")) return;
    if (document.querySelector(".Root__globalNav:focus-within")) return; // typing in search: keep it
    document.body.classList.add("sfx-scrolling");
    clearTimeout(t);
    t = setTimeout(() => document.body.classList.remove("sfx-scrolling"), 500);
  };
  document.addEventListener("wheel", hide, { passive: true, capture: true });
  document.addEventListener("touchmove", hide, { passive: true, capture: true });
  document.addEventListener("keydown", e => { if (/^(PageUp|PageDown|ArrowUp|ArrowDown|Home|End| )$/.test(e.key) && !e.target.closest("input, textarea")) hide(e); }, true);
})();

// Page flags for page-specific styling (search results, album pages)
window.sfxEvery(() => {
  const path = Spicetify.Platform?.History?.location?.pathname || "";
  const onSearch = /^\/search\/./.test(path);
  document.body.classList.toggle("sfx-on-search", onSearch);
  // only the very first result row on the page is the hero
  const first = onSearch && document.querySelector('.main-view-container [data-testid="media"]')?.closest('[role="row"]');
  for (const r of document.querySelectorAll(".sfx-top-hit")) if (r !== first) r.classList.remove("sfx-top-hit");
  if (first) first.classList.add("sfx-top-hit");
  const onAlbum = path.startsWith("/album/");
  if (onAlbum && document.body.dataset.sfxAlbum !== path) {
    // replay the record slide-out each time a new album opens
    document.body.dataset.sfxAlbum = path;
    document.body.classList.remove("sfx-album-in"); void document.body.offsetWidth;
    document.body.classList.add("sfx-album-in");
  }
  document.body.classList.toggle("sfx-on-album", onAlbum);
  if (!onAlbum) delete document.body.dataset.sfxAlbum;
}, 300);

// Right-click menus: gold star on the Like / Liked Songs entries
window.sfxEvery(() => {
  for (const it of document.querySelectorAll('ul[role="menu"] [role="menuitem"]:not(.sfx-seen)')) {
    it.classList.add("sfx-seen");
    if (/liked songs|^\s*like\s*$/i.test(it.textContent)) it.classList.add("sfx-like-item");
  }
}, 150);

// Playlist / album headers: a blurred glow of the cover behind the title
window.sfxEvery(() => {
  const h = document.querySelector(".main-view-container .main-entityHeader-container");
  const img = h?.querySelector("img.main-entityHeader-image, .main-entityHeader-image img, img");
  if (!h || !img?.src) return;
  const url = `url("${img.src}")`;
  if (h.style.getPropertyValue("--sfx-cover") !== url) { h.style.setProperty("--sfx-cover", url); h.classList.add("sfx-cover-glow"); }
}, 600);

// ---------- Queue: Spotify opens it in the right panel the theme hides; show that panel as a floating glass card ----------
window.sfxEvery(() => {
  // read the Queue button's pressed state; reading the panel's innerText forced a full-page style + layout 4x a second
  const qb = document.querySelector('.Root__now-playing-bar button[aria-label="Queue"]');
  const open = qb?.getAttribute("aria-pressed") === "true";
  if (document.body.classList.contains("sfx-queue-open") !== open) document.body.classList.toggle("sfx-queue-open", open);
}, 250);

// ---------- Constellations of your most-liked artists ----------
(function constellations() {
  if (!Spicetify?.Platform?.LibraryAPI) return setTimeout(constellations, 500);
  const KEY = "sfx-top-artists";
  const rnd = seed => () => ((seed = (seed * 9301 + 49297) % 233280) / 233280);
  async function topArtists() {
    try {
      const c = JSON.parse(localStorage.getItem(KEY) || "null");
      if (c && Date.now() - c.at < 86400000) return c.names;
    } catch {}
    const r = await Spicetify.Platform.LibraryAPI.getTracks({ limit: 2000, offset: 0 });
    const count = {};
    for (const t of r.items || r) for (const a of t.artists || []) count[a.name] = (count[a.name] || 0) + 1;
    const names = Object.entries(count).sort((a, b) => b[1] - a[1]).slice(0, 5).map(x => x[0]);
    try { localStorage.setItem(KEY, JSON.stringify({ at: Date.now(), names })); } catch {}
    return names;
  }
  // five slots spread over the sky, kept clear of the player column on the right
  // empty sky in the record column: above the record and below the up-next list, never behind page content
  const SLOTS = [[74.5, 1.5], [87, 3], [74.5, 84], [87, 87], [80.5, 93]];
  async function draw() {
    const host = document.querySelector(".Root__top-container");
    if (!host || host.querySelector(":scope > .sfx-const")) return;
    let names = [];
    try { names = await topArtists(); } catch { return; }
    if (!names.length) return;
    const layer = document.createElement("div");
    layer.className = "sfx-const";
    layer.innerHTML = names.map((name, i) => {
      const r = rnd([...name].reduce((a, ch) => a + ch.charCodeAt(0), 7) * 31 + i);
      const n = 5 + Math.floor(r() * 3);
      const pts = Array.from({ length: n }, () => [r() * 150 + 10, r() * 90 + 10]);
      pts.sort((a, b) => a[0] - b[0]);
      const lines = pts.slice(1).map((p, k) => `<line x1="${pts[k][0]}" y1="${pts[k][1]}" x2="${p[0]}" y2="${p[1]}"/>`).join("");
      const dots = pts.map(p => `<circle cx="${p[0]}" cy="${p[1]}" r="${1.4 + r() * 1.4}"/>`).join("");
      const esc = String(name).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
      const [x, y] = SLOTS[i];
      return `<figure class="sfx-con" data-artist="${esc}" style="left:${x}%;top:${y}%"><svg viewBox="0 0 170 110">${lines}${dots}</svg><figcaption>${esc}</figcaption></figure>`;
    }).join("");
    host.prepend(layer);
  }
  window.sfxEvery(() => {
    draw();
    // the constellation of whoever is playing lights up
    const now = (Spicetify.Player.data?.item?.artists || []).map(a => a.name);
    for (const f of document.querySelectorAll(".sfx-con")) f.classList.toggle("lit", now.includes(f.dataset.artist));
  }, 1500);
})();

// ---------- "Now playing" story card: press S to save a 1080x1920 image ----------
(function storyCard() {
  async function load(src) { const i = new Image(); i.crossOrigin = "anonymous"; i.src = src; await i.decode(); return i; }
  async function make() {
    const it = Spicetify.Player.data?.item;
    if (!it) return;
    const W = 1080, H = 1920, c = document.createElement("canvas"); c.width = W; c.height = H;
    const x = c.getContext("2d");
    const cs = getComputedStyle(document.documentElement);
    const top = cs.getPropertyValue("--spice-sidebar-alt").trim() || "#000", bot = cs.getPropertyValue("--spice-sidebar").trim() || "#1c1c1c";
    const g = x.createLinearGradient(0, 0, 0, H); g.addColorStop(0, top); g.addColorStop(1, bot);
    x.fillStyle = g; x.fillRect(0, 0, W, H);
    for (let i = 0; i < 520; i++) {                       // starfield
      const s = Math.random() < .92 ? 1 + Math.random() * 1.2 : 2 + Math.random() * 2;
      x.fillStyle = `rgba(255,255,255,${.3 + Math.random() * .7})`;
      x.beginPath(); x.arc(Math.random() * W, Math.random() * H, s, 0, 7); x.fill();
    }
    const cx = W / 2, cy = 820, R = 420;
    x.save(); x.shadowColor = "rgba(255,255,255,.35)"; x.shadowBlur = 60;
    x.fillStyle = "#0c0c0c"; x.beginPath(); x.arc(cx, cy, R, 0, 7); x.fill(); x.restore();
    for (let r = R * .26; r < R; r += 3) {                 // grooves
      x.strokeStyle = `rgba(255,255,255,${r % 21 < 3 ? .07 : .028})`; x.lineWidth = 1;
      x.beginPath(); x.arc(cx, cy, r, 0, 7); x.stroke();
    }
    const sheen = x.createConicGradient(0.7, cx, cy);
    sheen.addColorStop(0, "rgba(255,255,255,0)"); sheen.addColorStop(.06, "rgba(255,255,255,.10)"); sheen.addColorStop(.12, "rgba(255,255,255,0)");
    sheen.addColorStop(.5, "rgba(255,255,255,0)"); sheen.addColorStop(.56, "rgba(255,255,255,.10)"); sheen.addColorStop(.62, "rgba(255,255,255,0)");
    x.fillStyle = sheen; x.beginPath(); x.arc(cx, cy, R, 0, 7); x.fill();
    try {                                                   // label = cover
      const src = (it.images?.[0]?.url || it.metadata?.image_url || "").replace("spotify:image:", "https://i.scdn.co/image/").replace(/ab67616d0000(4851|1e02)/, "ab67616d0000b273");
      const img = await load(src);
      x.save(); x.beginPath(); x.arc(cx, cy, R * .3, 0, 7); x.clip(); x.drawImage(img, cx - R * .3, cy - R * .3, R * .6, R * .6); x.restore();
    } catch {}
    x.fillStyle = "#0c0c0c"; x.beginPath(); x.arc(cx, cy, 9, 0, 7); x.fill();
    x.textAlign = "center"; x.fillStyle = "#fff";
    x.font = "500 78px 'Big Caslon', serif";
    const title = it.name || it.metadata?.title || "";
    let t = title; while (x.measureText(t).width > W - 140 && t.length > 4) t = t.slice(0, -2);
    x.fillText(t === title ? t : t + "…", cx, 1420);
    x.fillStyle = "rgba(255,255,255,.7)"; x.font = "500 30px Futura, sans-serif";
    x.letterSpacing = "10px";
    x.fillText((it.artists || []).map(a => a.name).join(", ").toUpperCase() || (it.metadata?.artist_name || "").toUpperCase(), cx, 1490);
    x.font = "400 22px Futura, sans-serif"; x.fillStyle = "rgba(255,255,255,.45)";
    x.fillText("NOW PLAYING", cx, 260);
    const blob = await new Promise(r => c.toBlob(r, "image/png"));
    try { await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]); } catch {}
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = `now-playing-${title.replace(/[^\w-]+/g, "-").slice(0, 40)}.png`;
    document.body.appendChild(a); a.click(); a.remove();
    Spicetify.showNotification?.("Story card saved to Downloads (and copied)");
  }
  window.sfxStoryCard = make;
  document.addEventListener("keydown", e => {
    if (e.target.closest?.("input, textarea, [contenteditable='true']")) return;
    if ((e.key === "s" || e.key === "S") && !e.metaKey && !e.ctrlKey && !e.altKey) make();
  });
})();

// ---------- Playing row + play state flag ----------
window.sfxEvery(() => {
  document.body.classList.toggle("sfx-is-playing", !!Spicetify.Player.isPlaying());
  const eq = document.querySelector('.main-view-container .main-trackList-trackListRow img[src*="equaliser"], .main-view-container .main-trackList-trackListRow [class*="playingIcon"], .main-view-container .main-trackList-trackListRow svg[class*="playing"]');
  const row = eq?.closest(".main-trackList-trackListRow")
    // fallback: the row whose title text matches the current track and is shown in the accent colour
    || [...document.querySelectorAll(".main-view-container .main-trackList-trackListRow")].find(r => {
      const t = r.querySelector(".main-trackList-rowTitle, [data-encore-id='text']");
      return t && t.textContent === Spicetify.Player.data?.item?.name && getComputedStyle(t).color !== getComputedStyle(r).color;
    });
  for (const r of document.querySelectorAll(".sfx-playing-row")) if (r !== row) r.classList.remove("sfx-playing-row");
  if (row && !row.classList.contains("sfx-playing-row")) row.classList.add("sfx-playing-row");
}, 700);

// ---------- 5. Energy: freeze effects when Spotify isn't in front, lighter mode on battery ----------
(function energy() {
  const sync = () => {
    const away = document.hidden || !document.hasFocus();
    document.body.classList.toggle("sfx-away", away);
    for (const a of document.getAnimations()) {
      const n = a.animationName || "";
      if (!/^(twinkle\d|sfx-|spin$)/.test(n)) continue;
      if (away) a.pause(); else if (a.playState === "paused" && !(n === "spin" && !Spicetify.Player.isPlaying())) a.play();
    }
  };
  window.addEventListener("blur", () => setTimeout(sync, 50));
  window.addEventListener("focus", sync);
  document.addEventListener("visibilitychange", sync);
  navigator.getBattery?.().then(b => {
    const set = () => document.body.classList.toggle("sfx-battery", !b.charging);
    set(); b.addEventListener("chargingchange", set);
  }).catch(() => {});
})();

// mark player title/artist lines that overflow so only those get the fade
window.sfxEvery(() => {
  for (const el of document.querySelectorAll(".Root__now-playing-bar .main-trackInfo-name, .Root__now-playing-bar .main-trackInfo-artists")) {
    const over = el.scrollWidth > el.clientWidth + 1;
    if (el.classList.contains("sfx-overflow") !== over) el.classList.toggle("sfx-overflow", over);
  }
}, 800);

// ---------- Clean recording mode: H toggles. Hides the dock, scrollbars, cursor-ish chrome and your name / avatar ----------
(function cleanMode() {
  const toggle = () => {
    const on = document.body.classList.toggle("sfx-clean");
    Spicetify.showNotification?.(on ? "Clean mode on (H to exit)" : "Clean mode off");
  };
  window.sfxClean = toggle;
  document.addEventListener("keydown", e => {
    if (e.target.closest?.("input, textarea, [contenteditable='true']")) return;
    if ((e.key === "h" || e.key === "H") && !e.metaKey && !e.ctrlKey && !e.altKey) toggle();
  });
})();

// Home: tag the "New release from …" block so its card can be hidden (space kept)
window.sfxEvery(() => {
  const t = [...document.querySelectorAll('[data-testid="home-page"] p')].find(p => /^New release from/i.test(p.textContent));
  const sec = t?.closest("section");
  if (sec && !sec.classList.contains("sfx-newrel")) sec.classList.add("sfx-newrel");
}, 1000);
