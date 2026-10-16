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
  const hash = (s, salt) => { let h = 2166136261 ^ salt; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0) / 4294967296; };
  let tracks = null;
  function draw(cv) {
    if (!tracks?.length) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2), W = innerWidth, H = innerHeight;
    cv.width = W * dpr; cv.height = H * dpr;
    const x = cv.getContext("2d"); x.scale(dpr, dpr); x.clearRect(0, 0, W, H);
    const n = tracks.length, cx = W * 0.38, cy = H * 0.54, R = Math.min(W * 0.34, H * 0.62), rot = -0.35;
    for (let i = 0; i < n; i++) {
      const u = i / Math.max(1, n - 1), id = tracks[i].uri;
      const arm = Math.floor(hash(id, 1) * 3) * (Math.PI * 2 / 3);
      const ang = u * 8.5 + arm + (hash(id, 2) - 0.5) * (0.35 + (1 - u) * 1.1);
      const rad = R * (0.04 + 0.96 * Math.pow(u, 0.85)) + (hash(id, 3) - 0.5) * R * 0.1;
      const px = cx + (Math.cos(ang) * rad * Math.cos(rot) - Math.sin(ang) * rad * 0.58 * Math.sin(rot));
      const py = cy + (Math.cos(ang) * rad * Math.sin(rot) + Math.sin(ang) * rad * 0.58 * Math.cos(rot));
      const recent = i >= n - 40;
      const a = 0.2 + 0.55 * u * u + (recent ? 0.25 : 0);
      const sz = 0.5 + 1.1 * u * u * u + (recent ? 0.9 : 0);
      if (recent) { x.shadowColor = "rgba(255,200,110,.9)"; x.shadowBlur = 10; } else x.shadowBlur = 0;
      x.fillStyle = `rgba(255,${recent ? 226 : 214},${recent ? 150 : 140},${Math.min(1, a)})`;
      x.beginPath(); x.arc(px, py, sz, 0, 6.2832); x.fill();
    }
  }
  async function load() {
    try {
      const r = await Spicetify.Platform.LibraryAPI.getTracks({ limit: 3000, offset: 0 });
      tracks = (r.items || r).filter(t => t.addedAt && t.uri).sort((a, b) => new Date(a.addedAt) - new Date(b.addedAt));
    } catch { return; }
    paint();
  }
  function paint() {
    const host = document.querySelector(".Root__top-container");
    if (!host) return setTimeout(paint, 600);
    let cv = host.querySelector(":scope > canvas.sfx-liked-sky");
    if (!cv) { cv = document.createElement("canvas"); cv.className = "sfx-liked-sky"; host.prepend(cv); }
    draw(cv);
  }
  let rz = null;
  window.addEventListener("resize", () => { clearTimeout(rz); rz = setTimeout(paint, 500); });
  setTimeout(load, 2500);
  setInterval(load, 30 * 60 * 1000);
  const flag = () => document.body.classList.toggle("sfx-on-home", (Spicetify.Platform.History.location?.pathname || "") === "/");
  setInterval(flag, 600); flag();
})();
