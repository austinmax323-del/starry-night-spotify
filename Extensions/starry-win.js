// Starry Night, Windows compatibility.
// Some Spotify builds ship the v2 now-playing widget (data-testid="now-playing-widget-v2"),
// which has none of the classes the theme and starry-fx hook into. Tag its parts with the
// legacy class names so the vinyl player, ring and track info styling apply unchanged.
(function starryWin() {
  if (!document.body) return setTimeout(starryWin, 300);
  if (/^Win/.test(navigator.platform)) document.body.classList.add("sfx-win");

  const tag = (el, cls) => { if (el && !el.classList.contains(cls)) el.classList.add(cls); };

  function shim() {
    const w = document.querySelector('[data-testid="now-playing-widget-v2"]');
    if (!w) return;
    tag(w, "main-nowPlayingWidget-nowPlaying");
    tag(w, "sfx-v2");
    const slot = w.querySelector('[data-testid="cover-art-container"]');
    tag(slot, "main-nowPlayingWidget-coverArt");
    const btn = slot && slot.querySelector('[data-testid="cover-art-button"]');
    // the element that directly wraps the image becomes the record
    const art = btn && (btn.firstElementChild || btn);
    tag(art, "cover-art");
    const info = slot && slot.nextElementSibling;
    if (info) {
      tag(info, "main-trackInfo-container");
      const [name, artists] = info.querySelectorAll(":scope > *");
      tag(name, "main-trackInfo-name");
      tag(artists, "main-trackInfo-artists");
    }
  }

  let queued = false;
  new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; shim(); });
  }).observe(document.body, { childList: true, subtree: true });
  shim();
})();
