// Starry compat: Spotify 1.3.5 replaced its readable class names (Root__top-container, main-nowPlayingBar-*,
// main-trackList-*, ...) with scrambled ones. The theme is written against the old names, so this adapter
// re-tags the new elements with them, found through structure, roles and data-testids that Spotify keeps stable.
// It runs before starry-fx.js and theme.js (which waits for .Root__top-container).
(function starryCompat() {
  const add = (el, ...cls) => { if (!el) return; for (const c of cls) if (!el.classList.contains(c)) el.classList.add(c); };
  let grid = null;

  function layout() {
    const root = document.querySelector(".Root");
    if (!root) return;
    if (!grid?.isConnected) {
      grid = [...root.children].find(d => /global-nav/.test(getComputedStyle(d).gridTemplateAreas)) || null;
    }
    if (!grid) return;
    add(grid, "Root__top-container");
    for (const c of grid.children) {
      const a = getComputedStyle(c).gridArea;
      if (a === "left-sidebar") add(c, "Root__nav-bar");
      else if (a === "main-view") add(c, "Root__main-view");
      else if (a === "now-playing-bar") add(c, "Root__now-playing-bar");
      else if (a === "right-sidebar") add(c, "Root__right-sidebar");
      else if (a.startsWith("top-banner")) {
        c.setAttribute("data-sfx", "nav"); // class would be stripped: React re-renders this element's className constantly
        const kids = [...c.children].filter(k => k.tagName !== "A");
        const [hist, search, right] = kids;
        add(hist, "main-globalNav-historyButtonsContainer");
        add(search, "main-globalNav-searchSection");
        add(search?.firstElementChild, "main-globalNav-searchContainer");
        add(right, "main-globalNav-contentRight");
        const sc = search?.firstElementChild;
        const inp = search?.querySelector("input");
        if (sc && inp) { let e = inp; while (e && e.parentElement !== sc) e = e.parentElement; add(e, "main-globalNav-searchInputSection"); }
        for (const b of c.querySelectorAll("button, a")) if (b.closest(".main-globalNav-contentRight, .main-globalNav-historyButtonsContainer")) add(b.parentElement, "main-globalNav-navLink");
      }
    }
  }

  function player() {
    const bar = document.querySelector('[data-testid="now-playing-bar"]');
    if (!bar) return;
    add(bar, "main-nowPlayingBar-container");
    const inner = bar.firstElementChild;
    add(inner, "main-nowPlayingBar-nowPlayingBar");
    const [left, center, right] = inner ? inner.children : [];
    add(left, "main-nowPlayingBar-left"); add(center, "main-nowPlayingBar-center"); add(right, "main-nowPlayingBar-right");
    add(right?.firstElementChild, "main-nowPlayingBar-extraControls");
    const widget = bar.querySelector('[data-testid="now-playing-widget"]');
    add(widget, "main-nowPlayingWidget-nowPlaying");
    const slot = bar.querySelector('[data-testid="CoverSlotCollapsed__container"]');
    add(slot, "main-coverSlotCollapsed-container", "main-nowPlayingWidget-coverArt");
    const img = slot?.querySelector("img");
    if (img) { add(img, "cover-art-image", "main-image-image"); add(img.parentElement, "cover-art"); }
    const info = widget && widget.children[1];
    add(info, "main-trackInfo-container");
    if (info) { add(info.firstElementChild, "main-trackInfo-name"); add(info.lastElementChild, "main-trackInfo-artists"); }
    add(bar.querySelector('[data-testid="player-controls"]'), "player-controls");
    add(bar.querySelector('[data-testid="playback-position"]')?.parentElement, "playback-bar");
  }

  function pages() {
    const eh = document.querySelector('[data-testid="entity-header"]');
    add(eh, "main-entityHeader-container");
    const img = eh?.querySelector("img");
    if (eh) {
      add(eh.querySelector('[data-testid="playlist-image"], [data-testid$="-image"]'), "main-entityHeader-imageContainer");
      add(img, "main-entityHeader-image", "main-image-image");
      const title = eh.querySelector('[data-testid="entityTitle"], h1');
      add(title, "main-entityHeader-title");
      add(title?.parentElement, "main-entityHeader-headerText");
      add(eh.querySelector('[data-testid="creator-link"]')?.closest("div"), "main-entityHeader-metaData");
    }
    add(document.querySelector('[data-testid="action-bar"]'), "main-actionBar-ActionBar");
    add(document.querySelector('[data-testid="action-bar-row"]'), "main-actionBar-ActionBarRow");

    for (const g of document.querySelectorAll('.main-view-container [role="grid"]')) {
      const rows = [...g.querySelectorAll('[role="row"]')];
      if (!rows.length) continue;
      add(g, "main-trackList-trackList");
      for (const r of rows) {
        if (r.querySelector('[role="columnheader"]')) { add(r, "main-trackList-trackListHeaderRow", "main-trackList-trackListRowGrid"); add(r.parentElement, "main-trackList-trackListHeader"); continue; }
        const inner = r.firstElementChild;
        if (!inner || inner.getAttribute("role") !== "presentation") continue;
        add(r, "main-trackList-row");
        add(inner, "main-trackList-trackListRow", "main-trackList-trackListRowGrid");
        for (const c of inner.children) {
          const i = c.getAttribute("aria-colindex");
          if (i === "1") add(c, "main-trackList-rowSectionIndex");
          else if (i === "2") add(c, "main-trackList-rowSectionStart");
          else if (c.getAttribute("role") === "gridcell" && c === inner.lastElementChild) add(c, "main-trackList-rowSectionEnd");
          else if (c.getAttribute("role") === "gridcell") add(c, "main-trackList-rowSectionVariable");
        }
        const title = inner.querySelector('.main-trackList-rowSectionStart [data-encore-id="text"]');
        add(title, "main-trackList-rowTitle");
      }
    }

    for (const card of document.querySelectorAll('[data-encore-id="card"]:not(.sfx-card)')) {
      // own names on purpose: Spotify still ships CSS for the old card class names, which would resize the cards
      add(card, "sfx-card");
      const im = card.querySelector("img");
      if (im) { add(im.parentElement, "sfx-card-imageWrapper"); add(im.parentElement?.parentElement, "sfx-card-imageContainer"); }
    }
    for (const s of document.querySelectorAll('[class*="x-settings-section"], [data-testid="settings-section"]')) add(s, "x-settings-section");
  }

  function tick() { try { layout(); player(); pages(); } catch {} }
  tick();
  const fast = setInterval(tick, 300);
  setTimeout(() => { clearInterval(fast); setInterval(tick, 700); }, 40000);
})();
