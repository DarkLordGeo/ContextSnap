// The capture effect, shared by the keyboard stage (the whole browser window) and the sample desktop
// (its screen). Bars of small squares grow inward from all four edges of the rectangle, each to its own
// length and on its own beat, hold for a moment, then shrink back the way they grew: same time, same
// curve, same order. One motion, no pulsing. Each bar fades from soft charcoal at the edge to white at
// its tip, slightly transparent. Bars never reach further than a sixth of the shorter side, so the
// middle stays clear. Meanwhile the content behind pulls back very slightly and returns.
//
// One <canvas> per run, drawn with requestAnimationFrame and removed when the run ends. Bar lengths
// come from a seeded generator: every run varies a little but stays deliberate. Starting a run on a
// layer stops the previous one on that layer.
window.contextSnapFX = (() => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  const GROW = 260; // ms to grow, and the same to shrink
  const SHRINK_AT = 400; // ms after a bar starts
  const STAGGER = 90; // ms: the latest a bar starts
  const DURATION = STAGGER + SHRINK_AT + GROW + 40; // ms from trigger until every bar is back in its edge
  const PEAK = 330; // ms, when most bars are at full length and the content is furthest back

  let seed = 20261028;
  const random = () => {
    seed = (seed * 1664525 + 1013904223) >>> 0;
    return seed / 4294967296;
  };
  const between = (min, max) => min + random() * (max - min);
  const smooth = (x) => x * x * (3 - 2 * x);

  // Grow, hold, then shrink as the exact mirror of the growth.
  const envelope = (t) => {
    if (t <= 0) return 0;
    if (t < GROW) return smooth(t / GROW);
    if (t < SHRINK_AT) return 1;
    if (t < SHRINK_AT + GROW) return smooth(1 - (t - SHRINK_AT) / GROW);
    return 0;
  };

  // Color: mostly white, a touch darker at the edge, a little transparent; a few bars are a shade
  // darker than their neighbors (TONES) so the edge does not read as one flat band.
  const TONES = [1, 0.93, 0.86];
  const shade = (k, rows, tone) => {
    const f = rows > 1 ? k / (rows - 1) : 1;
    const c = Math.round((205 + 50 * f) * TONES[tone]);
    return `rgba(${c}, ${c}, ${Math.min(255, c + 2)}, ${(0.55 + 0.35 * f).toFixed(3)})`;
  };

  // Bars for one rectangle: positions along each edge, a length each, and a start time each.
  const makeBars = (width, height, size, pitch, inset) => {
    const reach = Math.min(width, height) * 0.16; // never further in than this
    const maxPixels = Math.max(3, Math.floor(reach / pitch));
    const spacing = pitch * 2; // one column of bars, one empty column: separate bars, not a mesh
    const bars = [];
    const edge = (length, side) => {
      const count = Math.floor((length - pitch) / spacing);
      const offset = (length - (count - 1) * spacing - size) / 2;
      // Irregular lengths with some neighborly shape, never a pattern.
      const raw = Array.from({ length: count }, () => Math.pow(random(), 1.6));
      const shaped = raw.map((value, i) => (raw[i - 1] ?? value) * 0.25 + value * 0.5 + (raw[i + 1] ?? value) * 0.25);
      for (let i = 0; i < count; i++) {
        const along = Math.round(offset + i * spacing);
        // Near a corner a bar may only reach as deep as it is far from that corner, so bars from two
        // edges meet along the diagonal and never cross.
        const corner = Math.min(along, length - along - size);
        const room = Math.floor((corner - 2 - inset - size) / pitch) + 1;
        const length_ = Math.min(Math.max(1, Math.round((0.15 + shaped[i] * 0.7) * maxPixels)), room);
        if (length_ < 1) continue;
        bars.push({ side, along, length: length_, delay: between(0, STAGGER), tone: random() < 0.7 ? 0 : random() < 0.6 ? 1 : 2 });
      }
    };
    edge(width, "top");
    edge(width, "bottom");
    edge(height, "left");
    edge(height, "right");
    return { bars, rows: maxPixels };
  };

  const runs = new WeakMap(); // layer -> stop function of its current run

  const play = (layer, { width, height, zoom = null, origin = "50% 50%" }) => {
    runs.get(layer)?.();
    const quiet = reduce.matches;
    const scan = layer.querySelector(".capture-scan");
    const canvas = document.createElement("canvas");
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    scan.replaceChildren(canvas);
    const context = canvas.getContext("2d");
    context.scale(ratio, ratio);

    const size = width < 600 ? 4 : 5; // pixel
    const pitch = size + 2;
    const inset = 3;
    const { bars, rows } = makeBars(width, height, size, pitch, inset);
    // One fill color per (depth row, tone): index row * TONES.length + tone.
    const colors = Array.from({ length: (rows + 1) * TONES.length }, (_, i) => shade(Math.floor(i / TONES.length), rows, i % TONES.length));
    const total = quiet ? 700 : DURATION;

    // Where pixel n of a bar sits, counted from its edge inward.
    const place = (bar, n) => {
      const depth = inset + n * pitch;
      if (bar.side === "top") return [bar.along, depth];
      if (bar.side === "bottom") return [bar.along, height - depth - size];
      if (bar.side === "left") return [depth, bar.along];
      return [width - depth - size, bar.along];
    };

    const draw = (t) => {
      context.clearRect(0, 0, width, height);
      // Reduced motion: the bars appear at a fixed length and fade, without moving.
      const alpha = quiet ? Math.max(0, Math.min(1, t / 150, (total - t) / 200)) : 1;
      if (alpha <= 0) return;
      const solid = colors.map(() => new Path2D());
      const soft = colors.map(() => new Path2D()); // the partly grown pixel at a tip
      for (const bar of bars) {
        const level = quiet ? bar.length * 0.6 : bar.length * envelope(t - bar.delay);
        const full = Math.floor(level);
        for (let n = 0; n < full; n++) {
          const [x, y] = place(bar, n);
          solid[Math.min(n, rows) * TONES.length + bar.tone].rect(x, y, size, size);
        }
        if (level - full > 0.2) {
          const [x, y] = place(bar, full);
          soft[Math.min(full, rows) * TONES.length + bar.tone].rect(x, y, size, size);
        }
      }
      for (let k = 0; k < colors.length; k++) {
        context.fillStyle = colors[k];
        context.globalAlpha = alpha;
        context.fill(solid[k]);
        context.globalAlpha = alpha * 0.5;
        context.fill(soft[k]);
      }
      context.globalAlpha = 1;
    };

    layer.classList.add("is-on");
    const timers = [
      setTimeout(() => layer.classList.add("is-peak"), quiet ? 150 : PEAK - 80),
      setTimeout(() => layer.classList.remove("is-peak"), quiet ? 350 : PEAK + 200),
    ];
    let animation = null;
    if (zoom && !quiet) {
      zoom.style.transformOrigin = origin;
      zoom.style.willChange = "transform";
      animation = zoom.animate(
        [{ transform: "scale(1)", easing: "cubic-bezier(0.33, 0, 0.2, 1)" }, { transform: "scale(0.985)", offset: 0.45, easing: "cubic-bezier(0.4, 0, 0.25, 1)" }, { transform: "scale(1)" }],
        { duration: DURATION },
      );
    }

    const start = performance.now();
    let frame = 0;
    const tick = (now) => {
      const t = now - start;
      draw(Math.min(t, total));
      if (t < total) frame = requestAnimationFrame(tick);
      else stop();
    };
    frame = requestAnimationFrame(tick);

    function stop() {
      cancelAnimationFrame(frame);
      timers.forEach(clearTimeout);
      animation?.cancel();
      if (zoom) { zoom.style.transformOrigin = ""; zoom.style.willChange = ""; }
      scan.replaceChildren();
      layer.classList.remove("is-on", "is-peak");
      if (runs.get(layer) === stop) runs.delete(layer);
    }
    // Also on a timer: without rendered frames (a hidden tab) the run must still end and clean up.
    timers.push(setTimeout(stop, total + 120));
    runs.set(layer, stop);
    return stop;
  };

  return {
    play,
    stop: (layer) => runs.get(layer)?.(),
    get duration() { return reduce.matches ? 700 : DURATION; },
  };
})();
