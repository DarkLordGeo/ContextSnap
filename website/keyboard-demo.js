// The keyboard in the hero. Every key can be clicked or tapped; Ctrl, Alt and Shift latch like sticky
// keys, and C runs the shortcut (adding any modifier that is not held yet). While this page has focus,
// the physical keys are mirrored and the real Ctrl+Alt+Shift+C runs the same demo.
//
// The demo is honest by construction: the snapshot it shows is fixed sample content in the page. A
// website cannot read the desktop or reach the installed app, and if ContextSnap is running it takes
// the shortcut before the browser sees it.
(() => {
  const kb = document.getElementById("kb");
  if (!kb) return;

  const demo = document.getElementById("demo");
  const chat = document.getElementById("chat");
  const capture = document.querySelector(".capture:not(.is-local, .is-icon)"); // full-window capture layer
  const main = document.getElementById("main");
  const fx = window.contextSnapFX;
  const status = document.getElementById("demo-status");
  const sim = window.contextSnapSim;

  const keys = [...kb.querySelectorAll(".key")];
  const byCode = new Map(keys.map((key) => [key.dataset.code, key]));
  const combo = Object.fromEntries(keys.filter((key) => key.dataset.combo).map((key) => [key.dataset.combo, key]));
  const ORDER = ["ctrl", "alt", "shift", "c"];
  const MODIFIERS = ["ctrl", "alt", "shift"];
  const LABEL = { ctrl: "Ctrl", alt: "Alt", shift: "Shift", c: "C" };

  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");

  // Sound ------------------------------------------------------------------------------------------
  // Short switch sounds synthesized once with the Web Audio API: a high transient for the switch, a
  // damped low body for the bottom-out, and a faint keycap resonance. Sound is always on, but nothing
  // can play until the visitor first interacts (browsers require a gesture). Voices are capped.
  const Sound = (() => {
    let ctx = null;
    let master = null;
    let banks = null;
    const voices = [];
    const lastPlayed = new Map();

    const random = (seed) => () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);

    const render = (sampleRate, spec, seed) => {
      const rnd = random(seed);
      const length = Math.round(sampleRate * spec.length);
      const data = new Float32Array(length);
      let previousNoise = 0;
      let lowNoise = 0;
      const jitter = 1 + (rnd() - 0.5) * 0.08;
      const body = spec.body * jitter;
      const ping = spec.ping * (1 + (rnd() - 0.5) * 0.1);
      for (let i = 0; i < length; i++) {
        const t = i / sampleRate;
        const noise = rnd() * 2 - 1;
        const high = noise - previousNoise;
        previousNoise = noise;
        lowNoise += (noise - lowNoise) * 0.12;
        const attack = 1 - Math.exp(-t / 0.0006);
        let sample = high * Math.exp(-t / 0.0011) * spec.click;
        sample += lowNoise * Math.exp(-t / 0.007) * spec.plastic;
        sample += Math.sin(2 * Math.PI * ping * t) * Math.exp(-t / 0.009) * spec.pingGain;
        const hit = (offset, gain) => {
          const u = t - offset;
          if (u < 0) return 0;
          const env = Math.exp(-u / spec.decay) * (1 - Math.exp(-u / 0.0008));
          return (Math.sin(2 * Math.PI * body * u) + 0.45 * Math.sin(2 * Math.PI * body * 2.07 * u)) * env * gain;
        };
        sample += hit(spec.bottomOut, spec.thock);
        if (spec.rattle) sample += hit(spec.rattle, spec.thock * 0.35);
        data[i] = sample * attack;
      }
      let peak = 0;
      for (const value of data) peak = Math.max(peak, Math.abs(value));
      const scale = peak > 0 ? 0.9 / peak : 1;
      const fadeStart = Math.round(length * 0.8);
      for (let i = 0; i < length; i++) data[i] *= scale * (i > fadeStart ? (length - i) / (length - fadeStart) : 1);
      const buffer = ctx.createBuffer(1, length, sampleRate);
      buffer.copyToChannel(data, 0);
      return buffer;
    };

    const SPECS = {
      down: { length: 0.11, click: 0.55, plastic: 0.5, ping: 1850, pingGain: 0.1, body: 205, decay: 0.03, thock: 1, bottomOut: 0.004 },
      up: { length: 0.07, click: 0.4, plastic: 0.3, ping: 2500, pingGain: 0.07, body: 290, decay: 0.016, thock: 0.5, bottomOut: 0 },
      spaceDown: { length: 0.16, click: 0.4, plastic: 0.6, ping: 950, pingGain: 0.08, body: 135, decay: 0.05, thock: 1, bottomOut: 0.005, rattle: 0.014 },
      spaceUp: { length: 0.1, click: 0.3, plastic: 0.35, ping: 1300, pingGain: 0.05, body: 190, decay: 0.025, thock: 0.5, bottomOut: 0 },
    };

    const unlock = () => {
      if (!ctx) {
        const AudioContextType = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextType) return;
        try {
          ctx = new AudioContextType({ latencyHint: "interactive" });
        } catch {
          return;
        }
        master = ctx.createGain();
        master.gain.value = 0.42;
        master.connect(ctx.destination);
        banks = {};
        let seed = 7;
        for (const [name, spec] of Object.entries(SPECS)) {
          const count = name.startsWith("space") ? 2 : 5;
          banks[name] = Array.from({ length: count }, () => render(ctx.sampleRate, spec, seed++ * 7919));
        }
      }
      if (ctx.state === "suspended") ctx.resume().catch(() => {});
    };

    const play = (kind, key) => {
      if (!ctx || ctx.state !== "running") return;
      const id = `${key.dataset.code}:${kind}`;
      const now = ctx.currentTime;
      if (now - (lastPlayed.get(id) ?? -1) < 0.03) return; // one sound per movement, even when events double up
      lastPlayed.set(id, now);
      const space = key.classList.contains("space");
      const bank = banks[(space ? "space" : "") + (space ? (kind === "down" ? "Down" : "Up") : kind)];
      const source = ctx.createBufferSource();
      source.buffer = bank[Math.floor(Math.random() * bank.length)];
      source.playbackRate.value = 0.96 + Math.random() * 0.08;
      const gain = ctx.createGain();
      gain.gain.value = (kind === "down" ? 1 : 0.55) * (0.85 + Math.random() * 0.15) * (key.classList.contains("hot") ? 1 : 0.8);
      let output = gain;
      if (ctx.createStereoPanner) {
        const panner = ctx.createStereoPanner();
        const plate = key.offsetParent;
        const center = plate ? (key.offsetLeft + key.offsetWidth / 2) / plate.offsetWidth : 0.5;
        panner.pan.value = (center - 0.5) * 0.5;
        gain.connect(panner);
        output = panner;
      }
      source.connect(gain);
      output.connect(master);
      source.start();
      voices.push(source);
      source.onended = () => {
        const index = voices.indexOf(source);
        if (index >= 0) voices.splice(index, 1);
      };
      while (voices.length > 8) {
        try { voices.shift().stop(); } catch { /* already ended */ }
      }
    };

    return { unlock, play };
  })();

  // Key movement -----------------------------------------------------------------------------------
  const isDown = (key) => key.classList.contains("is-down");
  const press = (key, sound = true) => {
    if (isDown(key)) return;
    key.classList.add("is-down");
    if (sound) Sound.play("down", key);
  };
  const release = (key, sound = true) => {
    if (!isDown(key)) return;
    key.classList.remove("is-down");
    if (sound) Sound.play("up", key);
  };

  // The shortcut keys drawn in the chat light up while they are held, here or on a real keyboard.
  const latched = new Set();
  const physicalModifiers = { ctrl: false, alt: false, shift: false };
  const hintKeys = [...chat.querySelectorAll(".slot-keys kbd")];
  function syncHintKeys() {
    for (const kbd of hintKeys) {
      const name = Object.keys(LABEL).find((key) => LABEL[key] === kbd.textContent);
      kbd.classList.toggle("is-held", Boolean(name && (latched.has(name) || physicalModifiers[name] || isDown(combo[name]))));
    }
  }

  // The shortcut run -------------------------------------------------------------------------------
  // Each run gets a token; starting another run invalidates every step still scheduled for the last
  // one and resets the frame and the chat, so repeated presses never stack effects or messages.
  let token = 0;
  let timers = [];
  let busy = false; // a run is in progress
  const at = (ms, run, step) => timers.push(setTimeout(() => { if (run === token) step(); }, ms));
  const cancelRun = () => {
    token++;
    for (const id of timers) clearTimeout(id);
    timers = [];
  };

  const resetDemo = () => {
    fx.stop(capture);
    demo.dataset.state = "idle";
    for (const name of ORDER) combo[name].classList.remove("is-lit");
  };

  const showResult = (run, source) => {
    resetDemo();
    sim.showPreview();
    sim.startCapture();
    const snap = sim.home.snapshot(); // taken now, from this demo's own sample desktop
    for (const name of ORDER) combo[name].classList.add("is-lit");
    at(800, run, () => { for (const name of ORDER) combo[name].classList.remove("is-lit"); });

    // 1. Capture: lights along the window edges while the page pulls back slightly (capture-fx.js).
    demo.dataset.state = "capturing";
    status.textContent = "Shortcut demo: capturing the sample desktop.";
    const middle = window.scrollY + window.innerHeight / 2 - main.offsetTop;
    fx.play(capture, { width: document.documentElement.clientWidth, height: window.innerHeight, zoom: main, origin: `50% ${Math.round(middle)}px` });

    // 2. Copied: only once the effect has finished.
    const clearAt = fx.duration;
    at(clearAt, run, () => {
      // If the shortcut ran while the chat was off screen (for example from the sample desktop), bring it
      // into view so the snapshot is seen arriving.
      const box = chat.getBoundingClientRect();
      if (box.bottom < 80 || box.top > window.innerHeight - 80) chat.scrollIntoView({ behavior: reduce.matches ? "auto" : "smooth", block: "center" });
      demo.dataset.state = "copied";
      sim.collector.collect(snap, demo); // the page's context collector receives this demo's snapshot
    });

    // 3. The snapshot is pasted into the chat, the question typed and sent, and the answer streams in. It
    // starts as the collector's cards (collector.js) stack up and leave the middle of the screen.
    const pasteAt = clearAt + (reduce.matches ? 250 : 1300);
    at(clearAt + 250, run, () => { demo.dataset.state = "idle"; });
    at(pasteAt, run, () => {
      const length = sim.deliver(snap);
      at(length, run, () => {
        busy = false;
        status.textContent = `Sample snapshot with ${snap.count} sources pasted into a sample chat, with an answer based only on it. Nothing was read from your computer.`;
      });
    });
  };

  const runShortcut = (source) => {
    cancelRun();
    busy = true;
    const run = token;
    let t = 0;
    if (source !== "physical") {
      // Add whatever is not held yet, in the usual order, as a person would.
      for (const name of ORDER) {
        if (isDown(combo[name])) continue;
        at(t, run, () => press(combo[name]));
        t += reduce.matches ? 60 : 95;
      }
    }
    // The keys go down and C comes back up before the capture dims the window, so the press is seen.
    const releaseAt = t + (reduce.matches ? 80 : 140);
    at(source === "physical" ? 180 : releaseAt + 60, run, () => showResult(run, source));
    // Release the clicked keys. A physical run leaves real keys to keyup and only drops clicked latches.
    const toRelease = source === "physical" ? [...latched] : [...ORDER].reverse();
    {
      toRelease.forEach((name, index) => at(releaseAt + index * 60, run, () => {
        latched.delete(name);
        combo[name].setAttribute("aria-pressed", "false");
        release(combo[name]);
        syncHintKeys();
      }));
    }
  };

  const toggleLatch = (name) => {
    const key = combo[name];
    if (latched.has(name)) {
      latched.delete(name);
      release(key);
    } else {
      latched.add(name);
      press(key);
    }
    if (key.hasAttribute("aria-pressed")) key.setAttribute("aria-pressed", String(latched.has(name)));
    syncHintKeys();
    if (latched.size === MODIFIERS.length) status.textContent = "Ctrl, Alt and Shift are held. Press C.";
  };

  const activateComboKey = (name) => {
    if (name === "c") {
      press(combo.c);
      runShortcut("click");
    } else {
      toggleLatch(name);
    }
  };

  // Pointer and touch --------------------------------------------------------------------------------
  const pointerKeys = new Map();
  kb.addEventListener("pointerdown", (event) => {
    const key = event.target.closest(".key");
    if (!key || (event.pointerType === "mouse" && event.button !== 0)) return;
    event.preventDefault(); // no text selection or focus jump; keyboard focus still works through Tab
    Sound.unlock();
    if (key.dataset.combo) {
      activateComboKey(key.dataset.combo);
      return;
    }
    press(key);
    pointerKeys.set(event.pointerId, key);
    try { key.setPointerCapture(event.pointerId); } catch { /* not capturable */ }
  });
  const endPointer = (event) => {
    const key = pointerKeys.get(event.pointerId);
    if (!key) return;
    pointerKeys.delete(event.pointerId);
    release(key);
  };
  kb.addEventListener("pointerup", endPointer);
  kb.addEventListener("pointercancel", endPointer);
  kb.addEventListener("lostpointercapture", endPointer);

  // Keyboard users reach the four shortcut keys with Tab and press them with Enter or Space.
  kb.addEventListener("click", (event) => {
    const key = event.target.closest(".key[data-combo]");
    if (!key || event.detail !== 0) return;
    Sound.unlock();
    activateComboKey(key.dataset.combo);
  });

  // Physical keyboard -------------------------------------------------------------------------------
  let inView = true;
  if ("IntersectionObserver" in window) {
    new IntersectionObserver(([entry]) => { inView = entry.isIntersecting; }, { threshold: 0.25 }).observe(demo);
  }
  const QUIET = new Set(["Tab", "Enter", "Space", "MetaLeft", "MetaRight"]);
  const physicalKeys = new Set();
  const isTyping = (target) => target instanceof Element
    && (target.isContentEditable || Boolean(target.closest("input, textarea, select, [contenteditable]:not([contenteditable='false'])")));

  const syncPhysicalModifiers = (event) => {
    physicalModifiers.ctrl = event.ctrlKey;
    physicalModifiers.alt = event.altKey;
    physicalModifiers.shift = event.shiftKey;
    syncHintKeys();
  };

  let cDown = false; // a C keydown reached the page (see the keyup handler)
  document.addEventListener("keydown", (event) => {
    if (event.code === "KeyC") cDown = true;
    if (isTyping(event.target)) return;
    if (window.contextSnapOS?.ownsKeyboard()) return; // the sample desktop is on screen and handles it
    Sound.unlock();
    const isShortcut = event.ctrlKey && event.altKey && event.shiftKey && (event.code === "KeyC" || event.key.toLowerCase() === "c");
    if (isShortcut) {
      event.preventDefault(); // only this exact combination, which browsers do not use for anything
      if (!event.repeat) runShortcut("physical");
      if (!inView) return;
    }
    if (!inView) return;
    syncPhysicalModifiers(event);
    const key = byCode.get(event.code);
    if (key && !event.repeat && !(key.dataset.combo && latched.has(key.dataset.combo))) {
      physicalKeys.add(event.code);
      press(key, !QUIET.has(event.code));
    }
  });

  document.addEventListener("keyup", (event) => {
    // When ContextSnap itself is running, Windows hands it Ctrl+Alt+Shift+C (RegisterHotKey) and the page
    // only sees C come back up. That release still means the shortcut was pressed, so show the press.
    const handedOff = event.code === "KeyC" && event.ctrlKey && event.altKey && event.shiftKey && !cDown;
    if (event.code === "KeyC") cDown = false;
    if (handedOff && !isTyping(event.target) && !window.contextSnapOS?.ownsKeyboard()) {
      runShortcut("physical");
      if (inView) {
        press(combo.c);
        setTimeout(() => release(combo.c), 160);
      }
    }
    syncPhysicalModifiers(event);
    if (!physicalKeys.has(event.code)) return;
    physicalKeys.delete(event.code);
    release(byCode.get(event.code), !QUIET.has(event.code));
  });

  const releasePhysical = () => {
    for (const code of physicalKeys) release(byCode.get(code), false);
    physicalKeys.clear();
    physicalModifiers.ctrl = physicalModifiers.alt = physicalModifiers.shift = false;
    syncHintKeys();
  };
  window.addEventListener("blur", releasePhysical);
  document.addEventListener("visibilitychange", () => { if (document.hidden) releasePhysical(); });
})();
