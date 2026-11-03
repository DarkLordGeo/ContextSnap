// The hero's chat shown as four popular AI chats in turn (illustrations; ContextSnap copies to the clipboard
// and works with any chat). Each turn tells the whole story: the shortcut is tapped on the keyboard, a
// ContextSnap.md card lifts off the C key and flies into that chat's composer, the question is sent, and the
// answer streams in (workspace-sim.js). Then the next chat fades in.
//
// The rotation pauses while the pointer is over the chat, while the visitor uses the shortcut or a
// suggested question, when the hero is off screen, and when the tab is hidden. With reduced motion it does
// not rotate; the tabs switch instantly.
(() => {
  const demo = document.getElementById("demo");
  const chat = document.getElementById("chat");
  const sim = window.contextSnapSim;
  if (!demo || !chat || !sim) return;

  const PROVIDERS = [
    { id: "chatgpt", name: "ChatGPT", title: "ChatGPT", placeholder: "Ask anything" },
    { id: "claude", name: "Claude", title: "Billing API 404s", placeholder: "Reply to Claude…" },
    { id: "gemini", name: "Gemini", title: "Gemini", placeholder: "Ask Gemini" },
    { id: "cursor", name: "Cursor", title: "Billing API 404s", placeholder: "Plan, search, build anything" },
  ];
  const HOLD = 2600; // ms the finished answer stays before the next chat
  const FLIGHT = 900; // ms for the card from the keyboard to the composer

  const still = window.matchMedia("(prefers-reduced-motion: reduce)");
  const titleEl = chat.querySelector("[data-chat-title]");
  const markEl = chat.querySelector("[data-chat-mark]");
  const composerBox = chat.querySelector("[data-composer-box]");
  const tabs = [...chat.querySelectorAll("[data-provider-tab]")];
  const fly = demo.querySelector(".snap-fly");

  let index = 0;
  let timer = 0;
  let flight = null;
  let heldUntil = 0;
  let inView = true;

  const later = (fn, ms) => {
    clearTimeout(timer);
    timer = window.setTimeout(fn, ms);
  };
  const canPlay = () => !still.matches && inView && !chat.matches(":hover") && !document.hidden && Date.now() >= heldUntil && !sim.busy;
  const progressOf = (i) => tabs[i].querySelector(".provider-progress");

  // Headline: "Give [logo] ChatGPT the context it's missing." The name cycles on its own clock, independent
  // of the chat: the logo and name fade over to the next AI while the slot eases to the new width, so the
  // line never jumps. With reduced motion it stays "your AI".
  const slot = document.querySelector("[data-h1-ai]");
  const NS = "http://www.w3.org/2000/svg";
  const nameItem = (provider) => {
    const item = document.createElement("span");
    item.className = "h1-ai-item";
    item.dataset.provider = provider.id;
    const logo = document.createElementNS(NS, "svg");
    logo.setAttribute("class", "h1-ai-logo");
    const use = document.createElementNS(NS, "use");
    use.setAttribute("href", `#pv-${provider.id}`);
    logo.append(use);
    const text = document.createElement("span");
    text.className = "h1-ai-text";
    text.textContent = provider.name;
    item.append(logo, text);
    return item;
  };
  const headline = (provider, animate) => {
    if (!slot) return;
    const old = slot.querySelector(".h1-ai-item:not(.is-leaving)");
    const item = nameItem(provider);
    if (!animate || still.matches || !old) {
      slot.replaceChildren(item);
      return;
    }
    slot.querySelectorAll(".is-leaving").forEach((node) => node.remove());
    const from = slot.getBoundingClientRect().width;
    old.classList.add("is-leaving");
    slot.append(item);
    const to = item.getBoundingClientRect().width;
    const ease = "cubic-bezier(0.2, 0.8, 0.2, 1)";
    slot.animate([{ width: `${from}px` }, { width: `${to}px` }], { duration: 520, easing: ease });
    old.animate([
      { opacity: 1, transform: "none", filter: "blur(0)" },
      { opacity: 0, transform: "translateY(-0.3em)", filter: "blur(6px)" },
    ], { duration: 320, easing: "cubic-bezier(0.4, 0, 1, 1)", fill: "forwards" }).onfinish = () => old.remove();
    item.animate([
      { opacity: 0, transform: "translateY(0.3em)", filter: "blur(6px)" },
      { opacity: 1, transform: "none", filter: "blur(0)" },
    ], { duration: 520, delay: 140, easing: ease, fill: "backwards" });
  };

  // Skin -----------------------------------------------------------------------------------------------
  const apply = (i) => {
    const provider = PROVIDERS[i];
    chat.dataset.provider = provider.id;
    titleEl.textContent = provider.title;
    markEl.setAttribute("href", `#pv-${provider.id}`);
    tabs.forEach((tab, k) => {
      tab.setAttribute("aria-selected", String(k === i));
      tab.tabIndex = k === i ? 0 : -1;
      progressOf(k).getAnimations().forEach((animation) => animation.cancel());
    });
    sim.showPreview();
    sim.setPlaceholder(provider.placeholder);
  };

  // Switching chats swaps the skin in place: the window does not move; its colors ease over (CSS).
  const show = (i) => {
    cancelFlight();
    index = i;
    apply(i);
  };

  // The card from the keyboard to the composer ---------------------------------------------------------
  let collectTimers = [];
  let collectLayer = null;
  function cancelFlight() {
    flight?.cancel();
    flight = null;
    fly.hidden = true;
    collectTimers.forEach(clearTimeout);
    collectTimers = [];
    collectLayer?.remove();
    collectLayer = null;
  }

  // The collection beside the keyboard: three compact mock cards (browser, PDF, notes) rise from the
  // keyboard, compress together into one stack beside the chat, and the stack glides straight into the
  // chat's composer. Illustrative cards only, not windows; no clipboard here (the sample desktop
  // below has that). Returns false (and does nothing) when there is no room beside the keyboard.
  const SOURCES = [
    { label: "Browser context", icon: "chrome" },
    { label: "PDF document", icon: "pdf" },
    { label: "Notes", icon: "notes" },
  ];
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const collect = (done) => {
    const area = demo.getBoundingClientRect();
    const chatBox = chat.getBoundingClientRect();
    const room = chatBox.left - area.left; // the keyboard's visible part, left of the chat
    if (room < 300 || chatBox.top > area.top + 40) return false;
    const later2 = (ms, fn) => collectTimers.push(window.setTimeout(fn, ms));
    const layer = node("div", "hc");
    layer.setAttribute("aria-hidden", "true");
    demo.append(layer);
    collectLayer = layer;

    const CW = 160;
    const CH = 42;
    const G = { x: room - CW / 2 - 30, y: area.height * 0.42 }; // where they compress, beside the chat
    // The attachment chip as it will look once shown: its details filled in as the chat will fill them,
    // measured with it briefly shown (before paint, without its pop-in), and copied as the face the stack
    // turns into. Measured just before the glide, once the capture's pulse on the chat has settled.
    const attach = composerBox.querySelector("[data-attach]");
    const LOOK = ["background-color", "color", "box-shadow", "border-radius", "padding", "font", "gap", "display", "align-items", "flex-direction", "white-space", "overflow", "text-overflow"];
    const copyLook = (from, to) => {
      const style = getComputedStyle(from);
      for (const name of LOOK) to.style.setProperty(name, style.getPropertyValue(name));
      [...from.children].forEach((child, k) => to.children[k] && copyLook(child, to.children[k]));
    };
    const measureChip = () => {
      const snapNow = sim.home.snapshot();
      attach.querySelector("[data-attach-meta]").textContent = `${snapNow.count} ${snapNow.count === 1 ? "source" : "sources"} · ${sim.LEVELS[snapNow.level].name}`;
      const wasHidden = attach.hidden;
      attach.hidden = false;
      attach.style.animation = "none";
      const rect = attach.getBoundingClientRect();
      // The copy lives outside the chat, so it takes the real chip's computed look (each AI skin styles it).
      const face = attach.cloneNode(true);
      copyLook(attach, face);
      attach.style.animation = "";
      attach.hidden = wasHidden;
      face.removeAttribute("data-attach");
      face.hidden = false;
      face.style.animation = "";
      face.classList.add("hc-face");
      return { rect, face };
    };
    // Each run they rise from different spots over the keyboard, near the stack rather than far away.
    const spot = () => ({
      x: Math.min(room - CW - 8, Math.max(8, room * (0.18 + Math.random() * 0.5) - CW / 2)),
      y: area.height * (0.16 + Math.random() * 0.42) - CH / 2,
    });
    // Three smooth moves per card, each with a single easing so nothing speeds up or stalls midway: rise,
    // compress into the stack (all together), then the stack glides straight into the chat as one. Each move
    // starts exactly where the last one ended, and a later move only takes over once it begins (its fill
    // is forwards only), so there is never a jump between them.
    const RISE_STEP = 80;
    const RISE = 260;
    const COMPRESS_AT = 560;
    const COMPRESS = 380;
    const GLIDE_AT = COMPRESS_AT + COMPRESS;
    const GLIDE = 520;
    const cards = [];
    SOURCES.forEach((source, i) => {
      const card = node("div", "hc-card");
      card.append(sim.appIcon(source.icon, "hc-ic"), node("b", "", source.label));
      const p0 = spot();
      card.style.left = `${Math.round(p0.x)}px`;
      card.style.top = `${Math.round(p0.y)}px`;
      layer.append(card);
      const x = p0.x + CW / 2;
      const y = p0.y + CH / 2;
      const sx = Math.round(G.x - x + (i - 1) * 6); // stacked at full size, each a little offset so the stack reads
      const sy = Math.round(G.y - y - i * 6);
      const rest = "translate(0px, 0px) scale(1)";
      const stacked = `translate(${sx}px, ${sy}px) scale(1)`;
      card.animate([{ transform: "translate(0px, 16px) scale(0.92)", opacity: 0 }, { transform: rest, opacity: 1 }],
        { duration: RISE, delay: i * RISE_STEP, easing: "cubic-bezier(0.22, 0.8, 0.24, 1)", fill: "both" });
      card.animate([{ transform: rest }, { transform: stacked }],
        { duration: COMPRESS, delay: COMPRESS_AT, easing: "cubic-bezier(0.65, 0, 0.35, 1)", fill: "forwards" });
      cards.push({ card, p0, stacked });
    });
    later2(GLIDE_AT, () => {
      const box = demo.getBoundingClientRect();
      const { rect: chip, face } = measureChip();
      const glide = { duration: GLIDE, easing: "cubic-bezier(0.55, 0, 0.3, 1)", fill: "forwards" };
      cards.forEach(({ card, p0, stacked }, i) => {
        // The landing: every card's top-left on the chip's top-left (the cards behind are hidden by the top one).
        const ex = Math.round(chip.left - box.left - p0.x);
        const ey = Math.round(chip.top - box.top - p0.y);
        const move = card.animate([{ transform: stacked }, { transform: `translate(${ex}px, ${ey}px) scale(1)` }], glide);
        if (i === cards.length - 1) {
          move.finished.then(handoff, () => {});
          later2(GLIDE + 150, handoff); // in case animation frames are held back (a background tab)
          // The top card becomes the attachment on the way: it takes the chip's size, and the chip's face
          // fades in over its own, so it arrives looking exactly like the attachment.
          card.animate([{ width: `${CW}px`, height: `${CH}px` }, { width: `${chip.width}px`, height: `${chip.height}px` }], glide);
          // Its own label fades out first, then the face fades in, so the two never show through each other.
          for (const part of card.querySelectorAll(".hc-ic, b")) {
            part.animate([{ opacity: 1 }, { opacity: 0 }], { duration: GLIDE * 0.3, delay: GLIDE * 0.15, easing: "ease-in", fill: "forwards" });
          }
          card.append(face);
          face.animate([{ opacity: 0 }, { opacity: 1 }], { duration: GLIDE * 0.45, delay: GLIDE * 0.45, easing: "ease-out", fill: "both" });
        } else {
          // The cards behind tuck under the top one as it narrows to the chip.
          card.animate([{ opacity: 1 }, { opacity: 0 }], { duration: GLIDE * 0.5, delay: GLIDE * 0.45, easing: "ease-in", fill: "forwards" });
        }
      });
    });
    // Landed on the chip's spot (the moment the glide finishes): the attachment appears in place at once
    // (no pop-in) and the stack, which by now looks exactly like it, is swapped for it.
    let handedOff = false;
    const handoff = () => {
      if (collectLayer !== layer || handedOff) return; // cancelled meanwhile, or already handed off
      handedOff = true;
      attach.classList.add("is-handoff");
      done(); // shows the attachment on the next tick
      later2(32, () => {
        layer.remove();
        if (collectLayer === layer) collectLayer = null;
      });
      later2(600, () => attach.classList.remove("is-handoff"));
    };
    return true;
  };

  // Brings the context into the composer, then calls `done`: as the compressed cards beside the keyboard
  // when there is room, otherwise as a ContextSnap.md card from the C key. Without motion (or when either end is not
  // visible) it calls `done` straight away.
  const flyIn = (done) => {
    cancelFlight();
    if (still.matches) {
      done();
      return;
    }
    if (!collect(done)) flyFrom(null, done);
  };
  const flyFrom = (panelRect, done) => {
    const key = document.querySelector('#kb [data-code="KeyC"]');
    const area = demo.getBoundingClientRect();
    const from = panelRect ?? key?.getBoundingClientRect();
    const to = composerBox.getBoundingClientRect();
    if (!from || !from.width || !to.width) {
      cancelFlight();
      done();
      return;
    }
    if (collectLayer) {
      const layer = collectLayer;
      layer.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 260, easing: "ease-out", fill: "forwards" }).onfinish = () => layer.remove();
      collectLayer = null;
    }
    fly.hidden = false;
    const w = fly.offsetWidth;
    const h = fly.offsetHeight;
    const start = panelRect
      ? { x: from.left + from.width / 2 - area.left - w / 2, y: from.top + from.height / 2 - area.top - h / 2 }
      : { x: from.left + from.width / 2 - area.left - w / 2, y: from.top - area.top - h * 0.7 };
    const end = { x: to.left + 24 - area.left - w * 0.2, y: to.top + 8 - area.top - h * 0.2 };
    const lift = { x: start.x + 10, y: start.y - 46 };
    const pose = (p, scale, rotate, opacity) => ({ transform: `translate(${Math.round(p.x)}px, ${Math.round(p.y)}px) rotate(${rotate}deg) scale(${scale})`, opacity });
    // Rise off the key, then one smooth arc into the composer, shrinking into the attachment chip.
    const arc = [0.25, 0.5, 0.75].map((t) => {
      const u = 1 - t;
      const bend = { x: (lift.x + end.x) / 2, y: Math.min(lift.y, end.y) - 60 };
      return pose({ x: u * u * lift.x + 2 * u * t * bend.x + t * t * end.x, y: u * u * lift.y + 2 * u * t * bend.y + t * t * end.y }, 1 - 0.35 * t, -4 * (1 - t), 1);
    });
    flight = fly.animate([
      { ...pose(start, 0.6, 0, 0), offset: 0 },
      { ...pose(lift, 1, -4, 1), offset: 0.28 },
      { ...arc[0], offset: 0.46 },
      { ...arc[1], offset: 0.64 },
      { ...arc[2], offset: 0.82 },
      { ...pose(end, 0.4, 0, 0), offset: 1 },
    ], { duration: FLIGHT, easing: "cubic-bezier(0.45, 0, 0.3, 1)" });
    flight.onfinish = () => {
      fly.hidden = true;
      flight = null;
      done();
    };
  };

  // The rotation ---------------------------------------------------------------------------------------
  const cycle = () => {
    if (!canPlay()) {
      later(cycle, 800);
      return;
    }
    const tapped = window.contextSnapKeyboard?.tap() ?? 0;
    later(() => {
      if (!canPlay()) {
        later(cycle, 800);
        return;
      }
      flyIn(() => {
        const length = sim.deliver(sim.home.snapshot());
        progressOf(index).animate([{ transform: "scaleX(0)" }, { transform: "scaleX(1)" }], { duration: length + HOLD, easing: "linear", fill: "forwards" });
        later(next, length + HOLD);
      });
    }, tapped);
  };
  const next = () => {
    if (!canPlay()) {
      later(next, 800);
      return;
    }
    show((index + 1) % PROVIDERS.length);
    later(cycle, 900);
  };

  // The visitor takes over: stop the current turn and stay on this chat for a while.
  const hold = (ms = 14000) => {
    heldUntil = Date.now() + ms;
    cancelFlight();
    progressOf(index).getAnimations().forEach((animation) => animation.cancel());
    later(next, ms + 600);
  };

  tabs.forEach((tab, i) => {
    tab.addEventListener("click", () => {
      if (i !== index) show(i);
      heldUntil = Date.now() + 4000;
      later(cycle, 1100);
    });
    tab.addEventListener("keydown", (event) => {
      if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
      event.preventDefault();
      const target = (i + (event.key === "ArrowRight" ? 1 : PROVIDERS.length - 1)) % PROVIDERS.length;
      tabs[target].focus();
      tabs[target].click();
    });
  });
  chat.querySelector("[data-suggest]")?.addEventListener("click", (event) => {
    if (event.target.closest(".chip")) hold();
  });
  if ("IntersectionObserver" in window) {
    new IntersectionObserver(([entry]) => { inView = entry.isIntersecting; }, { threshold: 0.4 }).observe(demo);
  }

  window.contextSnapHero = { flyIn, hold };

  apply(0);
  if (!still.matches) {
    let named = 0;
    headline(PROVIDERS[named], false);
    window.setInterval(() => {
      if (document.hidden) return;
      named = (named + 1) % PROVIDERS.length;
      headline(PROVIDERS[named], true);
    }, 2600);
  }
  if (!still.matches) later(cycle, 1400);
})();
