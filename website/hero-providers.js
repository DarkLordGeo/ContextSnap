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
  // keyboard, compress together into one small stack beside the chat, and the stack hops straight into the
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

    const CW = 140;
    const CH = 38;
    const G = { x: room - CW / 2 - 30, y: area.height * 0.42 }; // where they compress, beside the chat
    const to = composerBox.getBoundingClientRect();
    const T = { x: to.left - area.left + 60, y: to.top - area.top + 22 }; // the composer's attachment spot
    // Each run they rise from different spots over the keyboard, near the stack rather than far away.
    const spot = () => ({
      x: Math.min(room - CW - 8, Math.max(8, room * (0.18 + Math.random() * 0.5) - CW / 2)),
      y: area.height * (0.16 + Math.random() * 0.42) - CH / 2,
    });
    // One continuous motion per card, no hand-over between animations: rise, a short hold, compress into
    // the stack (all together), then the stack hops into the chat as one.
    const RISE_STEP = 80;
    const RISE = 240;
    const COMPRESS_AT = 560;
    const HOP_AT = 940;
    const END = 1460;
    const frac = (ms, d, s0) => Math.min(1, Math.max(0, (ms - s0) / d));
    SOURCES.forEach((source, i) => {
      const card = node("div", "hc-card");
      card.append(sim.appIcon(source.icon, "hc-ic"), node("b", "", source.label));
      const p0 = spot();
      card.style.left = `${Math.round(p0.x)}px`;
      card.style.top = `${Math.round(p0.y)}px`;
      layer.append(card);
      const x = p0.x + CW / 2;
      const y = p0.y + CH / 2;
      const sx = G.x - x + (i - 1) * 3; // stacked, a few pixels apart
      const sy = G.y - y - i * 3;
      const ex = T.x - x;
      const ey = T.y - y;
      const start = i * RISE_STEP;
      const d = END - start;
      card.animate([
        { offset: 0, transform: "translate(0px, 16px) scale(0.9)", opacity: 0, easing: "cubic-bezier(0.22, 0.8, 0.24, 1)" },
        { offset: frac(start + RISE, d, start), transform: "translate(0px, 0px) scale(1)", opacity: 1 },
        { offset: frac(COMPRESS_AT, d, start), transform: "translate(0px, 0px) scale(1)", opacity: 1, easing: "cubic-bezier(0.5, 0, 0.2, 1)" },
        { offset: frac(HOP_AT, d, start), transform: `translate(${sx}px, ${sy}px) scale(0.62)`, opacity: 1, easing: "cubic-bezier(0.3, 0, 0.6, 1)" },
        { offset: frac((HOP_AT + END) / 2, d, start), transform: `translate(${(sx + ex) / 2}px, ${Math.min(sy, ey) - 46}px) scale(0.48)`, opacity: 1, easing: "cubic-bezier(0.4, 0, 0.7, 1)" },
        { offset: 1, transform: `translate(${ex}px, ${ey}px) scale(0.28)`, opacity: 0 },
      ], { duration: d, delay: start, fill: "both" });
    });
    later2(END + 40, () => {
      layer.remove();
      if (collectLayer === layer) collectLayer = null;
      done();
    });
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
