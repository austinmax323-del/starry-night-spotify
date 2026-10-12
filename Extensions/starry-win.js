// Starry Night, Windows compatibility.
// Some Spotify builds ship the v2 now-playing widget (data-testid="now-playing-widget-v2"),
// which has none of the classes the theme and starry-fx hook into. Tag its parts with the
// legacy class names so the vinyl player, ring and track info styling apply unchanged.
(function starryWin() {
  if (!document.body) return setTimeout(starryWin, 300);
  if (/^Win/.test(navigator.platform)) document.body.classList.add("sfx-win");

  const tag = (el, cls) => { if (el && !el.classList.contains(cls)) el.classList.add(cls); };

  function shim() {
    // newer builds drop the Root__now-playing-bar wrapper class; tag the grid child that holds the player
    const bar = document.querySelector('[data-testid="now-playing-bar"]');
    if (bar) {
      let el = bar;
      while (el.parentElement && !el.parentElement.classList.contains("Root__top-container")) el = el.parentElement;
      tag(el.parentElement ? el : bar, "Root__now-playing-bar");
    }
    if (!bar || bar.querySelector(".main-nowPlayingWidget-coverArt .cover-art:not(.sfx-tagged)")) return;
    // the cover button exists in every widget variant; work outwards from it
    const btn = bar.querySelector('[data-testid="cover-art-button"]');
    if (!btn) return;
    const slot = btn.closest('[data-testid="cover-art-container"], [data-testid="cover-art-slot"]') || btn.parentElement;
    const w = btn.closest('[data-testid="now-playing-widget-v2"]') || slot.parentElement;
    tag(w, "main-nowPlayingWidget-nowPlaying");
    tag(w, "sfx-v2");
    tag(slot, "main-nowPlayingWidget-coverArt");
    // the element that directly wraps the image becomes the record
    const art = btn && (btn.firstElementChild || btn);
    tag(art, "cover-art");
    tag(art, "sfx-tagged");
    const info = slot && slot.nextElementSibling;
    if (info) {
      tag(info, "main-trackInfo-container");
      const [name, artists] = info.querySelectorAll(":scope > *");
      tag(name, "main-trackInfo-name");
      tag(artists, "main-trackInfo-artists");
    }
  }


  // Ctrl+Alt+D: copy a layout report of the dock and player (for debugging on Windows)
  document.addEventListener("keydown", e => {
    if (!(e.ctrlKey && e.altKey && e.code === "KeyD")) return;
    const r = el => { if (!el) return null; const b = el.getBoundingClientRect(), s = getComputedStyle(el);
      return { cls: String(el.className?.baseVal ?? el.className ?? "").slice(0, 120), tid: el.dataset?.testid,
        box: [b.x, b.y, b.width, b.height].map(Math.round), pos: s.position, bottom: s.bottom, z: s.zIndex,
        transform: s.transform, overflow: s.overflow, display: s.display, backdrop: s.backdropFilter, contain: s.contain, opacity: s.opacity }; };
    const chain = el => { const o = []; while (el && el !== document.documentElement) { o.push(r(el)); el = el.parentElement; } return o; };
    const nav = document.querySelector(".Root__globalNav");
    const out = { win: [innerWidth, innerHeight, devicePixelRatio], body: document.body.className,
      dock: chain(nav), dockKids: nav ? [...nav.children].map(r) : null,
      npb: r(document.querySelector(".Root__now-playing-bar")), npbV2: r(document.querySelector('[data-testid="now-playing-widget-v2"]')),
      cover: r(document.querySelector(".Root__now-playing-bar .cover-art")), coverBtn: r(document.querySelector('[data-testid="cover-art-button"]')), grid: getComputedStyle(document.querySelector(".Root__top-container")).gridTemplateColumns, main: r(document.querySelector(".Root__main-view")), top: r(document.querySelector(".Root__top-container")),
      atBottom: [.25, .5, .75].map(f => r(document.elementFromPoint(innerWidth * f, innerHeight - 30))) };
    const text = JSON.stringify(out);
    navigator.clipboard.writeText(text).then(
      () => Spicetify.showNotification?.("Starry layout report copied - paste it to Claude"),
      () => { const t = document.createElement("textarea"); t.value = text; document.body.append(t); t.select(); document.execCommand("copy"); t.remove(); Spicetify.showNotification?.("Starry layout report copied"); });
  });

  let queued = false;
  new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; shim(); });
  }).observe(document.body, { childList: true, subtree: true });
  shim();
})();
