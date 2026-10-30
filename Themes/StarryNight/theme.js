function waitForElement(els, func, timeout = 100) {
  const queries = els.map((el) => document.querySelector(el));
  if (queries.every((a) => a)) {
    func(queries);
  } else if (timeout > 0) {
    setTimeout(waitForElement, 300, els, func, --timeout);
  }
}

function random(min, max) {
  // min inclusive max exclusive
  return Math.random() * (max - min) + min;
}

waitForElement(['.Root__top-container'], ([topContainer]) => {
  const r = document.documentElement;
  const rs = window.getComputedStyle(r);

  const backgroundContainer = document.createElement('div');
  backgroundContainer.className = 'starrynight-bg-container';
  topContainer.appendChild(backgroundContainer);

  // to position stars and shooting stars between the background and everything else
  const rootElement = document.querySelector('.Root__top-container');
  rootElement.style.zIndex = '0';

  // the stars: ONE canvas (was ~500 divs, each its own glow + compositing layer)
  const sky = document.createElement('canvas');
  sky.className = 'sfx-sky';
  backgroundContainer.appendChild(sky);
  const starRGB = (rs.getPropertyValue('--spice-star').trim() || '#ffffff');
  let stars = [], sprite = null, W = 0, H = 0, lastDraw = 0;
  const sctx = sky.getContext('2d');

  function makeSprite() {
    sprite = document.createElement('canvas'); sprite.width = sprite.height = 24;
    const g = sprite.getContext('2d'), grad = g.createRadialGradient(12, 12, 0, 12, 12, 12);
    grad.addColorStop(0, starRGB); grad.addColorStop(0.18, starRGB); grad.addColorStop(0.2, 'rgba(255,255,255,.28)'); grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad; g.fillRect(0, 0, 24, 24);
  }
  function layout() {
    W = backgroundContainer.clientWidth || innerWidth; H = backgroundContainer.clientHeight || innerHeight;
    if (!W || !H) return false;
    sky.width = W; sky.height = H;
    const n = Math.round(W * H / 4000);
    stars = [];
    for (let i = 0; i < n; i++) {
      stars.push({ x: random(0, 99) / 100 * W, y: random(0, 99) / 100 * H, r: Math.random() < 0.5 ? 1 : 2, a: random(0.5, 1),
        tw: Math.random() < 0.2, ph: random(0, 6.28), sp: random(0.6, 1.6) });
    }
    return true;
  }
  function drawSky(t) {
    if (!sprite) makeSprite();
    sctx.clearRect(0, 0, W, H);
    for (const s of stars) {
      const a = s.tw ? s.a * (0.62 + 0.38 * Math.sin(t * 0.0011 * s.sp + s.ph)) : s.a;
      sctx.globalAlpha = a;
      const d = s.r * 6; sctx.drawImage(sprite, s.x - d / 2, s.y - d / 2, d, d);
    }
    sctx.globalAlpha = 1;
  }
  if (layout()) drawSky(0);
  // twinkle at ~8 fps; stills when the window is in the background, on battery, or with the sky effect off
  setInterval(() => {
    const b = document.body.classList;
    if (document.hidden || b.contains('sfx-away') || b.contains('sfx-battery') || b.contains('sfx-off-drift')) return;
    drawSky(performance.now());
  }, 125);
  let rz = 0;
  addEventListener('resize', () => { clearTimeout(rz); rz = setTimeout(() => { if (layout()) drawSky(performance.now()); }, 250); });

  // handles resizing of playbar panel to match right sidebar below it
  const playbar = document.querySelector('.Root__now-playing-bar');
  waitForElement(['.Root__right-sidebar'], ([rightbar]) => {
    const resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.target === rightbar) {
          let newWidth = entry.contentRect.width;
          if (newWidth === 0) {
            const localStorageWidth = localStorage.getItem(
              '223ni6f2epqcidhx5etjafeai:panel-width-saved'
            );
            if (localStorageWidth) {
              newWidth = localStorageWidth;
            } else {
              newWidth = 420;
            }
          }
          playbar.style.width = `${newWidth}px`;
          break;
        }
      }
    });

    resizeObserver.observe(rightbar);
  });

  /*
  Pure CSS Shooting Star Animation Effect Copyright (c) 2021 by Delroy Prithvi (https://codepen.io/delroyprithvi/pen/LYyJROR)

  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
  */
  for (let i = 0; i < 4; i++) {
    const shootingstar = document.createElement('span');
    shootingstar.className = 'shootingstar';
    if (Math.random() < 0.75) {
      shootingstar.style.top = '-4px'; // hidden off screen when animation is delayed
      shootingstar.style.right = `${random(0, 90)}%`;
    } else {
      shootingstar.style.top = `${random(0, 50)}%`;
      shootingstar.style.right = '-4px'; // hidden when animation is delayed
    }

    const shootingStarGlowColor = `rgba(${rs.getPropertyValue(
      '--spice-rgb-shooting-star-glow'
    )},${0.1})`;
    shootingstar.style.boxShadow = `0 0 0 4px ${shootingStarGlowColor}, 0 0 0 8px ${shootingStarGlowColor}, 0 0 20px ${shootingStarGlowColor}`;

    shootingstar.style.animationDuration = `${
      Math.floor(Math.random() * 3) + 3
    }s`;
    shootingstar.style.animationDelay = `${Math.floor(Math.random() * 7)}s`;

    backgroundContainer.appendChild(shootingstar);

    shootingstar.addEventListener('animationend', () => {
      if (Math.random() < 0.75) {
        shootingstar.style.top = '-4px'; // hidden off screen when animation is delayed
        shootingstar.style.right = `${random(0, 90)}%`;
      } else {
        shootingstar.style.top = `${random(0, 50)}%`;
        shootingstar.style.right = '-4px'; // hidden when animation is delayed
      }

      shootingstar.style.animation = 'none'; // Remove animation

      void shootingstar.offsetWidth;

      shootingstar.style.animation = '';
      shootingstar.style.setProperty("animation-duration", `${Math.floor(Math.random() * 4) + 3}s`, "important");
    });
  }
});
