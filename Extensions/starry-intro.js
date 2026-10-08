// Starry Intro: WebGL startup sequence.
// Hyperspace jump -> stars converge into a spinning record (label painted from the current cover)
// -> record flies into the player -> flash -> UI fades in.
// Replay with the "I" key or window.sfxIntro(). Click to skip.
(function starryIntro() {
  if (!document.body || !Spicetify?.Player) return setTimeout(starryIntro, 50);

  const N = 26000;            // particles
  const LABEL = 0.21;         // label radius as a fraction of the disc (matches the CSS vinyl)
  let running = false;

  const introOff = (() => { try { return JSON.parse(localStorage.getItem("sfx-off") || "[]").includes("intro"); } catch { return false; } })();
  if (!introOff) document.body.classList.add("sfx-intro-on");

  async function coverPixels() {
    for (let i = 0; i < 60 && !Spicetify.Player.data?.item; i++) await new Promise(r => setTimeout(r, 50));
    const it = Spicetify.Player.data?.item;
    const u = it?.images?.[0]?.url || it?.metadata?.image_url;
    if (!u) return null;
    try {
      const img = new Image();
      img.crossOrigin = "anonymous";
      img.src = u.replace("spotify:image:", "https://i.scdn.co/image/");
      await Promise.race([img.decode(), new Promise((_, j) => setTimeout(j, 1200))]);
      const c = document.createElement("canvas"); c.width = c.height = 64;
      const x = c.getContext("2d"); x.drawImage(img, 0, 0, 64, 64);
      return x.getImageData(0, 0, 64, 64).data;
    } catch { return null; }
  }

  const VS = `
    attribute vec3 aR; attribute vec2 aD; attribute vec3 aC; attribute float aE;
    uniform float uTravel, uSpeed, uMorph, uTilt, uAspect, uSpin, uPt, uMode;
    uniform vec2 uCenter; uniform float uRad;
    varying vec3 vC; varying float vA;
    vec2 tunnel(float z){ vec2 dir = normalize(aR.xy + 1e-4) * (0.12 + length(aR.xy)); return dir / (mix(0.035, 1.0, z) * 2.6); }
    void main(){
      float z  = fract(aR.z - uTravel);
      float zt = min(z + uSpeed * 0.045, 1.0);
      vec2 head = tunnel(z);  head.x /= uAspect;
      vec2 tail = tunnel(zt); tail.x /= uAspect;
      float ang = aD.y + uSpin * (1.0 + 0.15 * (1.0 - aD.x));
      vec2 dl = vec2(cos(ang), sin(ang)) * aD.x;
      dl.y *= cos(uTilt);
      vec2 disc = uCenter + vec2(dl.x / uAspect, dl.y) * uRad;
      float m = uMorph;
      vec2 tun = aE > 0.5 ? mix(tail, head, m) : head;
      gl_Position = vec4(mix(tun, disc, m), 0.0, 1.0);
      gl_PointSize = uPt;
      vC = mix(vec3(0.85, 0.9, 1.0), aC, m);
      float near = 1.0 - z;
      vA = uMode < 0.5 ? near * near * (1.0 - aE) * (1.0 - m) : m;
    }`;
  const FS = `precision mediump float; varying vec3 vC; varying float vA;
    void main(){ gl_FragColor = vec4(vC * vA, vA); }`;

  function sh(gl, t, s) { const o = gl.createShader(t); gl.shaderSource(o, s); gl.compileShader(o); if (!gl.getShaderParameter(o, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(o)); return o; }

  async function run() {
    if (running) return;
    running = true;
    document.body.classList.add("sfx-intro-on");

    const wrap = document.createElement("div");
    wrap.className = "sfx-intro";
    const cv = document.createElement("canvas");
    const title = document.createElement("div");
    title.className = "sfx-intro-title";
    const flash = document.createElement("div");
    flash.className = "sfx-intro-flash";
    wrap.append(cv, title, flash);
    document.body.appendChild(wrap);

    let done = false;
    function finish() {
      done = true;
      document.body.classList.remove("sfx-intro-on");
      wrap.remove();
      running = false;
    }

    const dpr = Math.min(devicePixelRatio || 1, 2);
    const W = innerWidth, H = innerHeight;
    cv.width = W * dpr; cv.height = H * dpr;
    const gl = cv.getContext("webgl", { alpha: true, premultipliedAlpha: true, antialias: true });
    if (!gl) return finish();

    const pix = await coverPixels();
    const meta = Spicetify.Player.data?.item?.metadata || {};
    const esc = s => String(s || "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
    // first launch of the day: "Welcome back" card, the song underneath
    const today = new Date().toDateString();
    let first = false;
    try { first = localStorage.getItem("sfx-intro-day") !== today; localStorage.setItem("sfx-intro-day", today); } catch {}
    let who = "";
    if (first) { try { who = (await Spicetify.Platform?.UserAPI?.getUser?.())?.displayName || ""; } catch {} }
    const song = esc(Spicetify.Player.data?.item?.name || meta.title), artist = esc(meta.artist_name);
    title.innerHTML = first
      ? `<div class="t">Welcome back${who ? ", " + esc(who) : ""}</div><div class="a">${song}${artist ? " · " + artist : ""}</div>`
      : `<div class="t">${song}</div><div class="a">${artist}</div>`;

    // Two vertices per particle (head/tail) so the jump draws as streaks
    const R = new Float32Array(N * 2 * 3), Dd = new Float32Array(N * 2 * 2), C = new Float32Array(N * 2 * 3), E = new Float32Array(N * 2);
    for (let i = 0; i < N; i++) {
      const rx = Math.random() * 2 - 1, ry = Math.random() * 2 - 1, rz = Math.random();
      const isLabel = i < N * 0.32;
      // grooves: snap to 70 concentric rings so the disc reads as a record, not noise
      const ring = Math.floor(Math.sqrt(Math.random()) * 70);
      const r = isLabel ? Math.sqrt(Math.random()) * (LABEL - 0.02) + 0.02 : 0.235 + (ring + Math.random() * 0.35) / 70 * 0.765;
      const a = Math.random() * Math.PI * 2;
      let c;
      if (isLabel && pix) {
        const px = Math.min(63, Math.max(0, Math.floor((Math.cos(a) * r / LABEL * 0.5 + 0.5) * 63)));
        const py = Math.min(63, Math.max(0, Math.floor((-Math.sin(a) * r / LABEL * 0.5 + 0.5) * 63)));
        const k = (py * 64 + px) * 4;
        c = [pix[k] / 255 * 1.2, pix[k + 1] / 255 * 1.2, pix[k + 2] / 255 * 1.2];
      } else {
        // dim grooves, brighter every 7th ring and at the outer rim, plus a two-lobe light sheen
        const sheen = Math.pow(Math.abs(Math.cos(a - 0.9)), 6);
        let g = 0.07 + (ring % 7 === 0 ? 0.12 : 0) + (ring > 67 ? 0.35 : 0) + sheen * 0.45;
        if (Math.random() < 0.006) g += 0.7; // glints
        c = [g, g, g * 1.08];
      }
      for (let e = 0; e < 2; e++) {
        const j = i * 2 + e;
        R.set([rx, ry, rz], j * 3); Dd.set([r, a], j * 2); C.set(c, j * 3); E[j] = e;
      }
    }

    let p;
    try {
      p = gl.createProgram();
      gl.attachShader(p, sh(gl, gl.VERTEX_SHADER, VS));
      gl.attachShader(p, sh(gl, gl.FRAGMENT_SHADER, FS));
      gl.linkProgram(p); gl.useProgram(p);
    } catch (err) { console.error("[starry-intro]", err); return finish(); }
    const buf = (data, name, size) => {
      const b = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      const l = gl.getAttribLocation(p, name); gl.enableVertexAttribArray(l); gl.vertexAttribPointer(l, size, gl.FLOAT, false, 0, 0);
    };
    buf(R, "aR", 3); buf(Dd, "aD", 2); buf(C, "aC", 3); buf(E, "aE", 1);
    const U = n => gl.getUniformLocation(p, n);
    const u = { travel: U("uTravel"), speed: U("uSpeed"), morph: U("uMorph"), tilt: U("uTilt"), aspect: U("uAspect"),
      spin: U("uSpin"), pt: U("uPt"), mode: U("uMode"), center: U("uCenter"), rad: U("uRad") };
    gl.viewport(0, 0, cv.width, cv.height);
    gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE);
    gl.uniform1f(u.aspect, W / H);

    const ease = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
    const clamp = t => Math.max(0, Math.min(1, t));
    const T_WARP = 1900, T_FORM = 3500, T_FLY = 4600, T_END = 5400;
    let travel = 0, last = performance.now(), t0 = last;
    wrap.addEventListener("click", () => { t0 -= T_END; });

    function target() {
      const el = document.querySelector(".Root__now-playing-bar .main-nowPlayingWidget-coverArt .cover-art");
      const b = el?.getBoundingClientRect();
      if (!b || !b.width) return { cx: 0.62, cy: 0, rad: 0.3 };
      return { cx: ((b.left + b.width / 2) / W) * 2 - 1, cy: 1 - ((b.top + b.height / 2) / H) * 2, rad: (b.width / 2) / (H / 2) };
    }

    function frame(now) {
      if (done) return;
      // rAF timestamps can precede t0 by a few ms; a negative t would make pow() return NaN
      const t = Math.max(0, now - t0), dt = Math.max(0, Math.min(50, now - last)); last = Math.max(last, now);
      // speed: slow drift -> hard warp -> brake to a stop as the record forms
      const speed = t < T_WARP ? 0.25 + 5.5 * Math.pow(t / T_WARP, 2.2) : 5.75 * Math.pow(1 - clamp((t - T_WARP) / 900), 3);
      travel += speed * dt / 1000 * 0.35;
      const morph = ease(clamp((t - T_WARP + 150) / (T_FORM - T_WARP)));
      const fly = ease(clamp((t - T_FORM) / (T_FLY - T_FORM)));
      const tg = target();
      const cx = fly * tg.cx, cy = fly * tg.cy;
      const rad = 0.62 + (tg.rad - 0.62) * fly;
      const tilt = (1 - morph) * 1.25 + morph * (1.05 * (1 - fly));

      gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
      gl.uniform1f(u.travel, travel); gl.uniform1f(u.speed, speed); gl.uniform1f(u.morph, morph);
      gl.uniform1f(u.tilt, tilt); gl.uniform1f(u.spin, t / 1000 * 1.4);
      gl.uniform2f(u.center, cx, cy); gl.uniform1f(u.rad, rad);
      gl.uniform1f(u.mode, 0); gl.uniform1f(u.pt, 1); gl.drawArrays(gl.LINES, 0, N * 2);
      gl.uniform1f(u.mode, 1); gl.uniform1f(u.pt, (1.6 + 0.8 * (1 - fly)) * dpr); gl.drawArrays(gl.POINTS, 0, N * 2);

      wrap.style.setProperty("--title", String(clamp((t - 2300) / 500) * (1 - clamp((t - T_FORM) / 400))));
      if (t > T_FLY && !wrap.classList.contains("out")) {
        flash.style.left = `${(tg.cx + 1) / 2 * W}px`;
        flash.style.top = `${(1 - tg.cy) / 2 * H}px`;
        wrap.classList.add("out");
        document.body.classList.remove("sfx-intro-on");
      }
      if (t > T_END) return finish();
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  window.sfxIntro = run;
  document.addEventListener("keydown", e => {
    if (e.target.closest?.("input, textarea, [contenteditable='true']")) return;
    if ((e.key === "i" || e.key === "I") && !e.metaKey && !e.ctrlKey && !e.altKey) run();
  });
  if (!introOff) run();
})();
