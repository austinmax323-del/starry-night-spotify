// Starry palette: (1) a summon palette over the sky ( "/" or Cmd/Ctrl+K, or click the dock's search ):
// search songs / artists / playlists / albums, shuffle Liked Songs, resume, or play a mood.
// Results bloom into the sky as soft cards and vanish on Esc / click away.
// (2) Liked-songs galaxy: every liked song is a star, laid out by the date you liked it.
(function starryPalette() {
  if (!Spicetify?.Platform?.History || !Spicetify.GraphQL?.Request) return setTimeout(starryPalette, 400);

  const MOODS = ["Chill", "Pop Rap", "Rap", "R&B", "Beats", "Sad", "Slow", "Smooth", "Upbeat", "Rock", "Trap", "Hip Hop", "New Age", "Funny", "Reggae", "Dub"];
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  const firstUrl = o => { const st = [o]; while (st.length) { const x = st.pop(); if (!x || typeof x !== "object") continue; if (typeof x.url === "string") return x.url; for (const k in x) st.push(x[k]); } return ""; };
  const path = uri => { const [, kind, id] = uri.split(":"); return `/${kind}/${id}`; };

  // ---------- overlay ----------
  const root = document.createElement("div");
  root.className = "sfx-pal";
  root.hidden = true;
  root.innerHTML = `<div class="sfx-pal-box"><input class="sfx-pal-input" type="text" spellcheck="false" autocomplete="off" placeholder="search, or type a mood" />
    <div class="sfx-pal-actions"></div><div class="sfx-pal-results"></div></div>`;
  document.body.appendChild(root);
  const input = root.querySelector(".sfx-pal-input"), actions = root.querySelector(".sfx-pal-actions"), results = root.querySelector(".sfx-pal-results");
  let seq = 0, timer = null;

  function open() {
    if (!root.hidden) return;
    root.hidden = false; root.classList.remove("out"); void root.offsetWidth; root.classList.add("in");
    input.value = ""; results.innerHTML = ""; renderActions(""); input.focus();
  }
  function close() {
    if (root.hidden) return;
    root.classList.remove("in"); root.classList.add("out");
    setTimeout(() => { if (root.classList.contains("out")) root.hidden = true; }, 260);
    input.blur();
  }

  const item = (cls, attrs, html, i) => `<button class="${cls}" data-pal ${attrs} style="--i:${i}">${html}</button>`;

  function renderActions(q) {
    const t = q.trim().toLowerCase();
    const moods = MOODS.filter(m => t && m.toLowerCase().startsWith(t)).slice(0, 4);
    const base = [["resume", "Resume"], ["shuffle", "Shuffle Liked Songs"], ["browse", "Browse all"]];
    const list = (t ? [] : base).concat((t ? moods : MOODS.slice(0, 8)).map(m => ["mood:" + m, t ? `Shuffle ${m} from Liked Songs` : m]));
    actions.innerHTML = list.map(([id, label], i) => item("sfx-pal-chip", `data-act="${esc(id)}"`, esc(label), i)).join("");
  }

  function renderResults(r) {
    const parts = [];
    let i = 0;
    const group = (title, cls, arr) => { if (arr.length) parts.push(`<div class="sfx-pal-group ${cls}"><div class="sfx-pal-title">${title}</div><div class="sfx-pal-row">${arr.join("")}</div></div>`); };
    group("Songs", "tracks", r.tracks.map(x => item("sfx-pal-card track", `data-play="${x.uri}"`, `<img src="${esc(x.img)}"><span><b>${esc(x.name)}</b><i>${esc(x.sub)}</i></span>`, i++)));
    group("Artists", "artists", r.artists.map(x => item("sfx-pal-card artist", `data-open="${x.uri}"`, `<img src="${esc(x.img)}"><b>${esc(x.name)}</b>`, i++)));
    group("Playlists", "pls", r.playlists.map(x => item("sfx-pal-card cover", `data-open="${x.uri}"`, `<img src="${esc(x.img)}"><b>${esc(x.name)}</b><i>${esc(x.sub)}</i>`, i++)));
    group("Albums", "albums", r.albums.map(x => item("sfx-pal-card cover", `data-open="${x.uri}"`, `<img src="${esc(x.img)}"><b>${esc(x.name)}</b><i>${esc(x.sub)}</i>`, i++)));
    results.innerHTML = parts.join("") || '<div class="sfx-pal-empty">nothing out there</div>';
  }

  async function search(q) {
    const my = ++seq;
    let items;
    try {
      // searchModalResults is always registered; the bigger searchDesktop query only exists after the Search page has been opened
      const r = await Spicetify.GraphQL.Request(Spicetify.GraphQL.Definitions.searchModalResults, { searchTerm: q, offset: 0, limit: 24, numberOfTopResults: 24, includeAudiobooks: false, includePreReleases: false, includeAuthors: false, includeArtistHasConcertsField: false });
      items = r?.data?.searchV2?.topResultsV2?.itemsV2;
    } catch { return; }
    if (my !== seq || !items) return;
    const names = a => (a?.items || []).map(x => x?.profile?.name).filter(Boolean).join(", ");
    const by = t => items.map(i => i.item?.data).filter(d => d?.__typename === t);
    renderResults({
      tracks: by("Track").slice(0, 6).map(t => ({ name: t.name, uri: t.uri, sub: names(t.artists), img: firstUrl(t.albumOfTrack?.coverArt) })),
      artists: by("Artist").slice(0, 5).map(a => ({ name: a.profile?.name, uri: a.uri, img: firstUrl(a.visuals) })),
      playlists: by("Playlist").slice(0, 4).map(p => ({ name: p.name, uri: p.uri, sub: p.ownerV2?.data?.name || "", img: firstUrl(p.images) })),
      albums: by("Album").slice(0, 4).map(a => ({ name: a.name, uri: a.uri, sub: names(a.artists), img: firstUrl(a.coverArt) }))
    });
  }

  // ---------- actions ----------
  // Shuffle all Liked Songs without leaving the page (Spotify exposes them as a "liked-songs" context)
  async function shuffleLiked() {
    const u = (await Spicetify.Platform.UserAPI.getUser()).username;
    await Spicetify.Player.setShuffle(true);
    Spicetify.Player.playUri(`spotify:user:${u}:collection`);
  }
  // Plays Liked Songs narrowed by a mood chip by driving the real page: open it, click the chip, shuffle on, press play
  async function playLiked(mood) {
    Spicetify.Platform.History.push("/collection/tracks");
    const until = async (fn, tries = 40) => { for (let i = 0; i < tries; i++) { const v = fn(); if (v) return v; await sleep(250); } return null; };
    if (mood) {
      const chip = await until(() => [...document.querySelectorAll(".main-view-container button")].find(x => x.textContent.trim() === mood));
      if (!chip) { Spicetify.showNotification?.(`No "${mood}" mood in your Liked Songs`); return; }
      chip.click(); await sleep(1100);
    }
    await Spicetify.Player.setShuffle(true);
    // the big play button of the page header: "Play Liked Songs" / "Play <playlist>" (it reads "Pause" while that context is playing)
    const play = await until(() => document.querySelector('[data-testid="action-bar-row"] button[aria-label^="Play"]'));
    await sleep(400);
    play?.click();
  }
  async function act(id) {
    close();
    if (id === "resume") Spicetify.Player.play();
    else if (id === "shuffle") await shuffleLiked();
    else if (id === "browse") Spicetify.Platform.History.push("/search");
    else if (id.startsWith("mood:")) await playLiked(id.slice(5));
  }

  // ---------- events ----------
  input.addEventListener("input", () => {
    const q = input.value;
    renderActions(q);
    clearTimeout(timer);
    if (!q.trim()) { results.innerHTML = ""; seq++; return; }
    timer = setTimeout(() => search(q.trim()), 220);
  });
  root.addEventListener("click", e => {
    if (e.target === root) return close();
    const a = e.target.closest("[data-act]");
    if (a) return act(a.dataset.act);
    const p = e.target.closest("[data-play]");
    if (p) { Spicetify.Player.playUri(p.dataset.play); return close(); }
    const o = e.target.closest("[data-open]");
    if (o) { Spicetify.Platform.History.push(path(o.dataset.open)); close(); }
  });
  root.addEventListener("keydown", e => {
    if (e.key === "Escape") { e.preventDefault(); return close(); }
    const items = [...root.querySelectorAll("[data-pal]")];
    const cur = items.indexOf(document.activeElement);
    if (e.key === "Enter") {
      e.preventDefault();
      const t = cur >= 0 ? items[cur] : items.find(x => x.classList.contains("sfx-pal-card")) || items[0];
      t?.click();
    } else if (["ArrowDown", "ArrowRight"].includes(e.key) && items.length) { e.preventDefault(); (items[cur + 1] || items[0]).focus(); }
    else if (["ArrowUp", "ArrowLeft"].includes(e.key)) { e.preventDefault(); if (cur <= 0) input.focus(); else items[cur - 1].focus(); }
  });
  document.addEventListener("keydown", e => {
    const typing = e.target.closest?.("input, textarea, [contenteditable='true']");
    if (e.key === "/" && !typing && !e.metaKey && !e.ctrlKey && !e.altKey) { e.preventDefault(); open(); }
    else if ((e.key === "k" || e.key === "K") && (e.metaKey || e.ctrlKey)) { e.preventDefault(); open(); }
  }, true);
  // clicking the dock's search opens the palette instead of leaving the page
  document.addEventListener("click", e => {
    const nav = e.target.closest('[data-sfx="nav"]');
    if (!nav) return;
    if (e.target.closest('button[aria-label="Search"], button[aria-label^="Search"]')) { e.preventDefault(); e.stopPropagation(); open(); }
  }, true);
  document.addEventListener("focusin", e => {
    if (e.target.matches?.('[data-sfx="nav"] input')) { e.target.blur(); open(); }
  }, true);
  window.sfxPalette = open;

  // ---------- liked-songs galaxy ----------
  // A rotating wrapper holds: the static star canvas, a highlight canvas (playlist / artist hover), year labels and the now-playing ring.
  // Everything inside moves with one compositor-only CSS rotation (1 turn / 20 min); hit-testing applies the same angle in JS.
  const hash = (s, salt) => { let h = 2166136261 ^ salt; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0) / 4294967296; };
  const SPIN_MS = 20 * 60 * 1000;
  const yearHue = (y, y0, y1) => 215 - ((y - y0) / Math.max(1, y1 - y0)) * 175; // old = cool blue, new = gold
  const yearOf = t => new Date(t.addedAt).getFullYear();
  let tracks = null, pos = [], idxOf = new Map(), yearMode = false;
  let geo = { cx: 0, cy: 0, R: 0 }, wrap = null, cv = null, ov = null, labels = null, nowEl = null;
  let lit = null, litSrc = null, curArtist = null, curRow = null;
  const plCache = new Map();

  const spinAngle = () => { const an = wrap?.getAnimations?.()[0]; const t = an ? Number(an.currentTime) || 0 : 0; return (t % SPIN_MS) / SPIN_MS * Math.PI * 2; };

  function ensure() {
    const host = document.querySelector(".Root__top-container");
    if (!host) return false;
    wrap = host.querySelector(":scope > .sfx-galaxy");
    if (!wrap) {
      wrap = document.createElement("div"); wrap.className = "sfx-galaxy";
      wrap.innerHTML = '<canvas class="sfx-liked-sky"></canvas><canvas class="sfx-galaxy-lit"></canvas><div class="sfx-galaxy-labels"></div><div class="sfx-now-star" hidden></div>';
      host.prepend(wrap);
    }
    cv = wrap.querySelector(".sfx-liked-sky"); ov = wrap.querySelector(".sfx-galaxy-lit");
    labels = wrap.querySelector(".sfx-galaxy-labels"); nowEl = wrap.querySelector(".sfx-now-star");
    return true;
  }

  function point(u, id, arm0) {
    const arm = arm0 ? 0 : Math.floor(hash(id, 1) * 3) * (Math.PI * 2 / 3);
    const ang = u * 8.5 + arm + (arm0 ? 0 : (hash(id, 2) - 0.5) * (0.35 + (1 - u) * 1.1));
    const rad = geo.R * (0.04 + 0.96 * Math.pow(u, 0.85)) + (arm0 ? 0 : (hash(id, 3) - 0.5) * geo.R * 0.1);
    const rot = -0.35;
    return [geo.cx + (Math.cos(ang) * rad * Math.cos(rot) - Math.sin(ang) * rad * 0.58 * Math.sin(rot)),
            geo.cy + (Math.cos(ang) * rad * Math.sin(rot) + Math.sin(ang) * rad * 0.58 * Math.cos(rot))];
  }

  function sizeCanvas(c, W, H, dpr) { c.width = W * dpr; c.height = H * dpr; const x = c.getContext("2d"); x.setTransform(dpr, 0, 0, dpr, 0, 0); x.clearRect(0, 0, W, H); return x; }

  function paint() {
    if (!tracks?.length) return;
    if (!ensure()) return setTimeout(paint, 600);
    const dpr = Math.min(window.devicePixelRatio || 1, 1.25), W = innerWidth, H = innerHeight, n = tracks.length;
    const mv = document.querySelector(".Root__main-view")?.getBoundingClientRect();
    const mx = mv ? mv.left : 0, mw = mv ? mv.width : W;
    geo = { cx: mx + mw * 0.5, cy: H * 0.56, R: Math.min(mw * 0.42, H * 0.62) };
    wrap.style.transformOrigin = `${geo.cx}px ${geo.cy}px`;
    const x = sizeCanvas(cv, W, H, dpr); sizeCanvas(ov, W, H, dpr);
    const y0 = yearOf(tracks[0]), y1 = yearOf(tracks[n - 1]);
    pos = new Array(n);
    for (let i = 0; i < n; i++) {
      const u = i / Math.max(1, n - 1);
      const [px, py] = point(u, tracks[i].uri);
      pos[i] = [px, py];
      const recent = i >= n - 40;
      const a = (yearMode ? 0.5 : 0.2) + 0.55 * u * u + (recent ? 0.25 : 0);
      const sz = (yearMode ? 0.9 : 0.5) + 1.1 * u * u * u + (recent ? 0.9 : 0);
      if (recent && !yearMode) { x.shadowColor = "rgba(255,200,110,.9)"; x.shadowBlur = 10; } else x.shadowBlur = 0;
      x.fillStyle = yearMode ? `hsla(${yearHue(yearOf(tracks[i]), y0, y1)},85%,68%,${Math.min(1, a)})` : `rgba(255,${recent ? 226 : 214},${recent ? 150 : 140},${Math.min(1, a)})`;
      x.beginPath(); x.arc(px, py, sz, 0, 6.2832); x.fill();
    }
    // year labels along the first spiral arm, counter-rotated so they stay upright while the galaxy turns
    labels.innerHTML = "";
    const an = wrap.getAnimations?.()[0], delay = -(an ? Number(an.currentTime) || 0 : 0);
    let last = -1;
    for (let i = 0; i < n; i++) {
      const y = yearOf(tracks[i]); if (y === last) continue; last = y;
      const u = i / Math.max(1, n - 1), [lx, ly] = point(u, "", true);
      const dx = lx - geo.cx, dy = ly - geo.cy, d = Math.hypot(dx, dy) || 1, off = 16;
      const el = document.createElement("div");
      el.className = "sfx-galaxy-year"; el.textContent = y;
      el.style.left = (lx + dx / d * off) + "px"; el.style.top = (ly + dy / d * off) + "px";
      el.style.setProperty("--yc", `hsl(${yearHue(y, y0, y1)},85%,68%)`);
      el.style.animationDelay = delay + "ms";
      labels.appendChild(el);
    }
    wrap.classList.toggle("years", yearMode);
    drawLit(); updateNow();
  }

  function drawLit() {
    if (!ov || !pos.length) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 1.25), W = innerWidth, H = innerHeight;
    const x = sizeCanvas(ov, W, H, dpr);
    wrap.classList.toggle("lit", !!lit?.size);
    if (!lit?.size) return;
    const ids = [...lit].sort((a, b) => a - b);
    if (ids.length <= 160) {                                   // faint chain through the songs, oldest to newest
      x.strokeStyle = "rgba(255,226,170,.16)"; x.lineWidth = 0.7; x.beginPath();
      ids.forEach((i, k) => k ? x.lineTo(pos[i][0], pos[i][1]) : x.moveTo(pos[i][0], pos[i][1]));
      x.stroke();
    }
    x.shadowColor = "rgba(255,220,150,.95)"; x.shadowBlur = 9; x.fillStyle = "#fff0cf";
    for (const i of ids) { x.beginPath(); x.arc(pos[i][0], pos[i][1], 2.1, 0, 6.2832); x.fill(); }
  }
  function setLit(set, src) { lit = set; litSrc = src; drawLit(); }
  function clearLit(src) { if (src && litSrc !== src) return; lit = null; litSrc = null; drawLit(); }

  function updateNow() {
    if (!nowEl) return;
    const i = idxOf.get(Spicetify.Player.data?.item?.uri);
    if (i == null || !pos[i]) { nowEl.hidden = true; return; }
    nowEl.hidden = false; nowEl.style.left = pos[i][0] + "px"; nowEl.style.top = pos[i][1] + "px";
  }
  Spicetify.Player.addEventListener("songchange", () => setTimeout(updateNow, 300));

  async function load() {
    try {
      const r = await Spicetify.Platform.LibraryAPI.getTracks({ limit: 3000, offset: 0 });
      tracks = (r.items || r).filter(t => t.addedAt && t.uri).sort((a, b) => new Date(a.addedAt) - new Date(b.addedAt));
      idxOf = new Map(tracks.map((t, i) => [t.uri, i]));
    } catch { return; }
    paint();
  }

  // hold Y: colour the stars by the year you liked them
  document.addEventListener("keydown", e => {
    if (e.repeat || e.key.toLowerCase() !== "y" || e.metaKey || e.ctrlKey || e.altKey) return;
    if (e.target.closest?.("input, textarea, [contenteditable='true']") || !root.hidden) return;
    yearMode = true; paint();
  });
  const endYear = () => { if (yearMode) { yearMode = false; paint(); } };
  document.addEventListener("keyup", e => { if (e.key.toLowerCase() === "y") endYear(); });
  window.addEventListener("blur", endYear);

  // hover a library playlist: its songs that you liked light up and are chained together
  const rowUri = el => {
    const row = el?.closest?.('.Root__nav-bar [role="row"]');
    const id = row?.querySelector('[aria-labelledby^="listrow-title-"]')?.getAttribute("aria-labelledby") || "";
    const m = id.match(/listrow-title-(spotify:playlist:[A-Za-z0-9]+)/);
    return m ? [row, m[1]] : [row, null];
  };
  async function showPlaylist(uri) {
    let set = plCache.get(uri);
    if (!set) {
      try {
        const c = await Spicetify.Platform.PlaylistAPI.getContents(uri, { limit: 3000 });
        set = new Set((c.items || []).map(i => idxOf.get(i.uri)).filter(i => i != null));
        plCache.set(uri, set);
      } catch { return; }
    }
    if (curRow && rowUriCache === uri && set.size) setLit(set, "playlist");
  }
  let rowUriCache = null;
  document.addEventListener("mouseover", e => {
    if (!tracks) return;
    const [row, uri] = rowUri(e.target);
    if (row === curRow) return;
    curRow = row; rowUriCache = uri;
    if (!row || !uri) { clearLit("playlist"); return; }
    showPlaylist(uri);
  }, true);

  // hover a constellation (an artist you like) or a star
  const tip = document.createElement("div");
  tip.className = "sfx-star-tip"; tip.hidden = true;
  document.body.appendChild(tip);
  window.sfxStarHover = null;
  let moving = false;
  function setArtist(name) {
    if (name === curArtist) return;
    curArtist = name;
    for (const f of document.querySelectorAll(".sfx-con")) f.classList.toggle("hov", f.dataset.artist === name);
    if (!name) return clearLit("artist");
    const set = new Set();
    tracks.forEach((t, i) => { if ((t.artists || []).some(a => a.name === name)) set.add(i); });
    set.size ? setLit(set, "artist") : clearLit("artist");
  }
  const onMove = e => {
    if (moving) return; moving = true;
    requestAnimationFrame(() => {
      moving = false;
      const home = document.body.classList.contains("sfx-on-home") && root.hidden && pos.length;
      let hit = null, artist = null;
      if (home) {
        for (const f of document.querySelectorAll(".sfx-con")) {
          const r = f.getBoundingClientRect();
          if (e.clientX >= r.left - 6 && e.clientX <= r.right + 6 && e.clientY >= r.top - 6 && e.clientY <= r.bottom + 6) { artist = f.dataset.artist; break; }
        }
        if (!artist && !e.target.closest?.('button, a, input, img, [role="button"], [data-encore-id="card"], [data-sfx="nav"], .Root__now-playing-bar, .Root__nav-bar')) {
          const th = spinAngle(), c = Math.cos(th), sn = Math.sin(th);
          let best = 13 * 13;
          for (let i = 0; i < pos.length; i++) {
            const dx0 = pos[i][0] - geo.cx, dy0 = pos[i][1] - geo.cy;
            const dx = geo.cx + dx0 * c - dy0 * sn - e.clientX, dy = geo.cy + dx0 * sn + dy0 * c - e.clientY, d = dx * dx + dy * dy;
            if (d < best) { best = d; hit = i; }
          }
          if (hit != null) { const dx0 = pos[hit][0] - geo.cx, dy0 = pos[hit][1] - geo.cy; tip._xy = [geo.cx + dx0 * c - dy0 * sn, geo.cy + dx0 * sn + dy0 * c]; }
        }
      }
      if (tracks) setArtist(home ? artist : null);
      window.sfxStarHover = hit;
      document.body.classList.toggle("sfx-star-hover", hit != null);
      if (hit == null) { tip.hidden = true; return; }
      const t = tracks[hit], d = new Date(t.addedAt);
      tip.innerHTML = `<b>${esc(t.name)}</b><i>${esc((t.artists || []).map(a => a.name).join(", "))}</i><span>liked ${d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" })}</span>`;
      tip.hidden = false;
      tip.style.left = Math.min(innerWidth - 260, tip._xy[0] + 14) + "px"; tip.style.top = (tip._xy[1] + 14) + "px";
    });
  };
  document.addEventListener("pointermove", onMove, true);
  document.addEventListener("mousemove", onMove, true);
  document.addEventListener("mouseleave", () => { window.sfxStarHover = null; tip.hidden = true; document.body.classList.remove("sfx-star-hover"); setArtist(null); });
  document.addEventListener("click", e => {
    if (window.sfxStarHover == null) return;
    const t = tracks[window.sfxStarHover];
    if (t) { e.preventDefault(); e.stopPropagation(); Spicetify.Player.playUri(t.uri); }
  }, true);

  let rz = null;
  window.addEventListener("resize", () => { clearTimeout(rz); rz = setTimeout(paint, 500); });
  setTimeout(load, 2500);
  setInterval(load, 30 * 60 * 1000);
  const flag = () => document.body.classList.toggle("sfx-on-home", (Spicetify.Platform.History.location?.pathname || "") === "/");
  setInterval(flag, 600); flag();
})();
