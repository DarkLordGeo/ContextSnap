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

  // The collection beside the keyboard, with the same clipboard as the sample desktop below: it appears
  // beside the keyboard, three page-shaped mock cards (browser, PDF, notes) rise from the keyboard in a
  // quick ripple and land on it at the page's size, stacking; then it carries the stack into the chat.
  // Illustrative cards only, not windows. Returns false (and does nothing) when there is no room beside
  // the keyboard (narrow layouts), so the caller falls back to the card from the C key.
  const SOURCES = [
    { label: "Browser context", icon: "chrome" },
    { label: "PDF document", icon: "pdf" },
    { label: "Notes", icon: "notes" },
  ];
  const TILT = "rotateX(16deg) rotateY(-12deg) rotateZ(-3deg)";
  const SPRING = "cubic-bezier(0.34, 1.4, 0.64, 1)";
  const BARS = [92, 76, 84, 58, 70];
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

    // The clipboard: against the chat, in the middle of the desk.
    const W = 104;
    const H = Math.round(W * 1.28);
    const C = { x: room - W / 2 - 34, y: area.height * 0.46 };
    const holder = node("div", "clipb");
    holder.style.width = `${W}px`;
    holder.style.height = `${H}px`;
    holder.style.left = `${Math.round(C.x - W / 2)}px`;
    holder.style.top = `${Math.round(C.y - H / 2)}px`;
    const board = node("div", "clipb-board");
    const papers = node("div", "clipb-papers");
    const clip = node("span", "clipb-clip");
    const label = node("p", "clipb-label");
    const labelText = node("span", "", "ContextSnap");
    label.append(node("span", "clipb-check"), labelText);
    board.append(papers, clip, label);
    holder.append(board);
    layer.append(holder);
    const paper = { w: W * 0.82, h: H * 0.67 };
    const P = { x: C.x, y: C.y - H / 2 + H * (0.16 + 0.67 / 2) };
    board.animate([{ transform: `translateY(20px) ${TILT} scale(0.72)`, opacity: 0 }, { transform: `${TILT} scale(1)`, opacity: 1 }], { duration: 420, easing: SPRING, fill: "forwards" });

    const addPage = (source) => {
      const page = node("span", "clipb-paper");
      page.style.setProperty("--i", String(papers.children.length));
      page.append(sim.appIcon(source.icon, "clipb-ic"));
      for (const width of BARS) {
        const bar = node("i", "clipb-bar");
        bar.style.width = `${width}%`;
        page.append(bar);
      }
      papers.append(page);
      clip.animate([{ transform: "scaleY(0.8)" }, { transform: "scaleY(1)" }], { duration: 220, easing: SPRING });
      board.animate([{ transform: `translateY(3px) ${TILT} scale(0.97)` }, { transform: `${TILT} scale(1)` }], { duration: 300, easing: SPRING, fill: "forwards" });
    };

    // The cards: page-shaped, rising from the keyboard's top rows, one right after another.
    const cw = 112;
    const ch = Math.round(cw * (paper.h / paper.w));
    const STEP = 170;
    const PULL = 560;
    SOURCES.forEach((source, i) => {
      const card = node("div", "cdata");
      card.style.width = `${cw}px`;
      card.style.height = `${ch}px`;
      const head = node("p", "cdata-head");
      head.append(sim.appIcon(source.icon, "cdata-ic"), node("b", "", source.label));
      card.append(head, ...BARS.slice(0, 3).map((width) => {
        const bar = node("i", "cdata-bar");
        bar.style.width = `${width}%`;
        return bar;
      }));
      layer.append(card);
      const x0 = room * (0.16 + i * 0.2) - cw / 2;
      const y0 = area.height * 0.34 - ch / 2;
      card.style.left = `${Math.round(x0)}px`;
      card.style.top = `${Math.round(y0)}px`;
      const dx = P.x - (x0 + cw / 2);
      const dy = P.y - (y0 + ch / 2);
      const k = paper.w / cw;
      card.animate([
        { transform: "translate3d(0px, 14px, 0px) scale(0.9)", opacity: 0 },
        { transform: "translate3d(0px, -6px, 0px) scale(1)", opacity: 1, offset: 0.3 },
        { transform: `translate3d(${dx * 0.5}px, ${dy * 0.5 - 30}px, 20px) rotateX(8deg) rotateY(-6deg) scale(${(1 + k) / 2})`, opacity: 1, offset: 0.65 },
        { transform: `translate3d(${dx}px, ${dy}px, 2px) ${TILT} scale(${k})`, opacity: 1 },
      ], { duration: PULL, delay: i * STEP, easing: "cubic-bezier(0.4, 0, 0.2, 1)", fill: "both" });
      later2(i * STEP + PULL, () => {
        card.remove();
        addPage(source);
        labelText.textContent = `${source.label} · ${i + 1}/${SOURCES.length}`;
        label.classList.add("is-done");
      });
    });

    // Complete, then the clipboard carries the stack into the composer.
    const complete = (SOURCES.length - 1) * STEP + PULL + 120;
    later2(complete, () => {
      labelText.textContent = `${SOURCES.length} sources`;
      board.animate([{ transform: `${TILT} scale(1)` }, { transform: `${TILT} scale(1.05)`, offset: 0.4 }, { transform: `${TILT} scale(1)` }], { duration: 340, easing: "ease-out", fill: "forwards" });
    });
    later2(complete + 480, () => {
      const to = composerBox.getBoundingClientRect();
      const from = holder.getBoundingClientRect();
      const dx = to.left + 70 - (from.left + from.width / 2);
      const dy = to.top + 24 - (from.top + from.height / 2);
      board.animate([{ transform: `${TILT} scale(1)` }, { transform: "none" }], { duration: 520, easing: "cubic-bezier(0.45, 0, 0.2, 1)", fill: "forwards" });
      holder.animate([
        { transform: "none", opacity: 1 },
        { transform: `translate(${dx * 0.9}px, ${dy * 0.9 - 14}px) scale(0.62)`, opacity: 1, offset: 0.75 },
        { transform: `translate(${dx}px, ${dy}px) scale(0.36)`, opacity: 0 },
      ], { duration: 620, easing: "cubic-bezier(0.45, 0, 0.2, 1)", fill: "forwards" });
    });
    later2(complete + 480 + 600, () => {
      layer.remove();
      if (collectLayer === layer) collectLayer = null;
      done();
    });
    return true;
  };

  // Flies a ContextSnap.md card into the composer, then calls `done`: through the clipboard beside the
  // keyboard when there is room, otherwise from the C key. Without motion (or when either end is not
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
