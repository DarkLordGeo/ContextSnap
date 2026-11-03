// The sample Windows desktop: windows, taskbar, Start and tray menus, a sample AI chat, and the whole
// ContextSnap workflow played out on it: a research session (browser tabs, a paper, notes) → shortcut →
// capture → paste → ask → answer. The desktop state and the snapshot come from research-sim.js; the
// capture lights come from capture-fx.js.
// Everything is simulated in the page. Nothing on the visitor's computer is read (not even the real
// clipboard: pasting into the sample chat only ever inserts the sample snapshot), and no model or
// service is called.
(() => {
  const os = document.getElementById("os");
  const sim = window.contextSnapSim;
  const desk = sim?.research; // this demo's own research desktop (research-sim.js)
  const content = sim?.researchContent;
  const fx = window.contextSnapFX;
  if (!os || !desk || !content || !fx) return;
  const QUESTIONS = content.QUESTIONS;

  const section = document.getElementById("desktop");
  const inner = os.querySelector(".os-inner");
  const screen = os.querySelector(".os-screen");
  const layer = os.querySelector(".capture");
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  const narrow = window.matchMedia("(max-width: 767px)");
  const $ = (selector, root = os) => root.querySelector(selector);
  const $$ = (selector, root = os) => [...root.querySelectorAll(selector)];
  const wins = Object.fromEntries($$(".win").map((win) => [win.dataset.app, win]));
  const liveStatus = $("[data-os-status]", section);
  const announce = (text) => { liveStatus.textContent = text; };

  // Models in the sample chat. Picking one dresses the chat as that product (ChatGPT, Claude, or Gemini,
  // with its official mark); answers are still scripted locally from the snapshot, whichever is picked.
  // Keep this list current as model names change.
  const MODELS = [
    { provider: "OpenAI", product: "ChatGPT", mark: "chatgpt", placeholder: "Ask anything", models: ["ChatGPT"] },
    { provider: "Anthropic", product: "Claude", mark: "claude", placeholder: "Reply to Claude…", models: ["Claude"] },
    { provider: "Google", product: "Gemini", mark: "gemini", placeholder: "Ask Gemini", models: ["Gemini"] },
  ];

  // Small helpers --------------------------------------------------------------------------------------
  const el = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const relTo = (element, root = screen) => {
    const a = element.getBoundingClientRect();
    const b = root.getBoundingClientRect();
    return { x: a.left - b.left, y: a.top - b.top, w: a.width, h: a.height };
  };
  const isTyping = (target) => target instanceof Element && Boolean(target.closest("input, textarea, [contenteditable]:not([contenteditable='false'])"));

  // Desktop rendering ---------------------------------------------------------------------------------
  const taskButtons = $$(".tb-app");
  const maximized = new Set();
  const tabsEl = $("[data-br-tabs]");
  const urlEl = $("[data-br-url]");
  const pageEl = $("[data-br-page]");

  // Browser tabs, the paper, and the notes are sample content (research-sim.js), built as text.
  const tabButtons = content.TABS.map((tab, i) => {
    const button = el("button", "br-tab");
    button.type = "button";
    button.setAttribute("role", "tab");
    button.dataset.tab = String(i);
    button.append(el("span", "br-fav", tab.site[0].toUpperCase()), el("span", "br-tab-title", tab.title));
    return button;
  });
  tabsEl.append(...tabButtons);
  const paper = $("[data-pdf]");
  paper.append(el("p", "pdf-journal", "Energy Systems Letters · Vol. 12 · 2025"), el("h3", "pdf-title", content.PAPER.title), el("p", "pdf-authors", content.PAPER.authors.split(" · ")[0]));
  const para = (head, before, highlight, after) => {
    if (head) paper.append(el("p", "pdf-h", head));
    const p = el("p", "pdf-p");
    p.append(before, el("mark", "pdf-mark", highlight), after);
    paper.append(p);
  };
  para("Abstract", "We model curtailment on three island grids with high solar shares. ", content.PAPER.highlights[0], " Storage shifted surplus midday energy into the evening peak.");
  para("3. Results", "Curtailment fell fastest for the first increments of storage. ", content.PAPER.highlights[1], " Beyond that point, extra capacity mostly sat idle in winter.");
  para("4. Assumptions", "Costs follow 2023 market data. ", content.PAPER.highlights[2], " Lower prices would shorten payback periods.");
  $("[data-notes]").textContent = content.NOTES.join("\n");

  const render = () => {
    const state = desk.state;
    const current = desk.active();
    state.order.forEach((app, index) => {
      const win = wins[app];
      win.hidden = !desk.visible(app);
      win.classList.toggle("is-active", app === current);
      win.classList.toggle("is-max", maximized.has(app));
      win.style.zIndex = String(index + 1);
      $("[data-title]", win).textContent = desk.APPS[app].title();
      $("[data-max]", win).setAttribute("aria-label", maximized.has(app) ? "Restore" : "Maximize");
    });
    for (const button of taskButtons) {
      const app = button.dataset.app;
      button.classList.toggle("is-open", state.open.has(app));
      button.classList.toggle("is-active", app === current);
      button.setAttribute("aria-pressed", String(app === current));
    }
    const tab = content.TABS[state.tab];
    tabButtons.forEach((button, i) => {
      button.setAttribute("aria-selected", String(i === state.tab));
      button.tabIndex = i === state.tab ? 0 : -1;
    });
    urlEl.replaceChildren(el("span", "br-host", tab.site), tab.path);
    pageEl.replaceChildren(el("p", "br-site", tab.site), el("h3", "br-h", tab.heading), ...tab.text.map((line) => el("p", "br-p", line)));
    for (const button of $$(".level")) button.setAttribute("aria-checked", String(Number(button.dataset.level) === state.level));
    $$(".level").forEach((button) => { button.tabIndex = Number(button.dataset.level) === state.level ? 0 : -1; });
    $("[data-level-note]").textContent = `${sim.LEVELS[state.level].note} A website illustration; in the app you choose sources in Settings.`;
  };
  desk.subscribe(render);

  // The replay changes the desktop itself; any other change means the visitor took over.
  let scripted = false;
  const change = (mutate) => {
    if (!scripted) takeOver();
    desk.update(mutate);
  };
  const scriptedChange = (mutate) => {
    scripted = true;
    try { desk.update(mutate); } finally { scripted = false; }
  };
  const open = (app) => change(() => desk.focusApp(app));

  // Window management ---------------------------------------------------------------------------------
  let drag = null;
  os.addEventListener("pointerdown", (event) => {
    if (!event.target.closest(".start-menu, [data-start]")) closeMenu("start");
    if (!event.target.closest(".tray-menu, [data-tray]")) closeMenu("tray");
    if (!event.target.closest(".ca-menu, [data-model-button]")) closeModelMenu();
    const win = event.target.closest(".win");
    if (!win) return;
    const app = win.dataset.app;
    if (desk.active() !== app && !event.target.closest(".cap")) open(app);
    const bar = event.target.closest(".win-bar");
    if (!bar || event.target.closest(".cap, .br-tab") || narrow.matches || maximized.has(app) || event.button !== 0) return;
    const box = relTo(win);
    drag = { win, id: event.pointerId, dx: event.clientX - box.x, dy: event.clientY - box.y, area: screen.getBoundingClientRect(), width: box.w };
    try { bar.setPointerCapture(event.pointerId); } catch { /* not capturable */ }
  });
  os.addEventListener("pointermove", (event) => {
    if (!drag || event.pointerId !== drag.id) return;
    const x = Math.min(Math.max(event.clientX - drag.dx, 80 - drag.width), drag.area.width - 80);
    const y = Math.min(Math.max(event.clientY - drag.dy, 0), drag.area.height - 40);
    drag.win.style.left = `${Math.round(x)}px`;
    drag.win.style.top = `${Math.round(y)}px`;
  });
  const endDrag = () => { drag = null; };
  os.addEventListener("pointerup", endDrag);
  os.addEventListener("pointercancel", endDrag);
  os.addEventListener("dblclick", (event) => {
    const bar = event.target.closest(".win-bar");
    if (!bar || event.target.closest(".cap") || narrow.matches) return;
    toggleMax(bar.closest(".win").dataset.app);
  });
  const toggleMax = (app) => {
    if (maximized.has(app)) maximized.delete(app);
    else maximized.add(app);
    render();
  };
  // Keyboard focus inside a window brings it to the front, as a click would.
  os.addEventListener("focusin", (event) => {
    const win = event.target.closest(".win");
    if (win && desk.active() !== win.dataset.app && !scripted) open(win.dataset.app);
  });

  os.addEventListener("click", (event) => {
    const target = event.target;
    const win = target.closest(".win");
    const opener = target.closest("[data-open]");
    const task = target.closest(".tb-app");
    if (target.closest("[data-min]")) {
      change((state) => state.min.add(win.dataset.app));
    } else if (target.closest("[data-max]")) {
      toggleMax(win.dataset.app);
    } else if (target.closest("[data-close]")) {
      maximized.delete(win.dataset.app);
      change((state) => { state.open.delete(win.dataset.app); state.min.delete(win.dataset.app); });
    } else if (opener) {
      closeMenu("start");
      open(opener.dataset.open);
    } else if (task) {
      const app = task.dataset.app;
      if (desk.active() === app) change((state) => state.min.add(app));
      else open(app);
    } else if (target.closest(".br-tab")) {
      const index = Number(target.closest(".br-tab").dataset.tab);
      change((state) => { state.tab = index; desk.focusApp("browser"); });
    } else if (target.closest("[data-start]")) {
      toggleMenu("start");
    } else if (target.closest("[data-tray]")) {
      toggleMenu("tray");
    } else if (target.closest("[data-capture]")) {
      closeMenu("tray");
      shortcut("tray");
    } else if (target.closest(".level")) {
      const level = Number(target.closest(".level").dataset.level);
      change((state) => { state.level = level; });
    }
  });
  $(".levels").addEventListener("keydown", (event) => {
    if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
    event.preventDefault();
    const step = event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 1;
    change((state) => { state.level = ((state.level - 1 + step + 3) % 3) + 1; });
    $(`.level[data-level="${desk.state.level}"]`).focus();
  });

  // Menus
  const menus = { start: $(".start-menu"), tray: $(".tray-menu") };
  const menuButtons = { start: $("[data-start]"), tray: $("[data-tray]") };
  function closeMenu(name) {
    menus[name].hidden = true;
    menuButtons[name].setAttribute("aria-expanded", "false");
  }
  function toggleMenu(name) {
    const opening = menus[name].hidden;
    closeMenu("start");
    closeMenu("tray");
    if (!opening) return;
    menus[name].hidden = false;
    menuButtons[name].setAttribute("aria-expanded", "true");
    menus[name].querySelector("button")?.focus({ preventScroll: true });
  }

  // Clock (the visitor's local time, only shown here)
  const clock = () => {
    const now = new Date();
    $("[data-time]").textContent = now.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
    $("[data-date]").textContent = now.toLocaleDateString([], { month: "numeric", day: "numeric", year: "numeric" });
  };
  clock();
  setInterval(clock, 30000);

  // The workflow ---------------------------------------------------------------------------------------
  // Capture the active window → extract its context as layered sheets → assemble ContextSnap.md in the
  // chat → attach → ask → answer. Sheets, document, and answer are all built from the snapshot.
  const STEPS = ["work", "capture", "extract", "attach", "ask", "answer"];
  const stepItems = $$(".flow li", section);
  const setStep = (name) => {
    const index = STEPS.indexOf(name);
    stepItems.forEach((item, i) => {
      item.classList.toggle("is-active", i === index);
      item.classList.toggle("is-done", index >= 0 && i < index);
      if (i === index) item.setAttribute("aria-current", "step");
      else item.removeAttribute("aria-current");
    });
  };

  let token = 0;
  let timers = [];
  let mode = "idle"; // "auto" while the replay drives everything, "manual" otherwise
  let phase = "idle"; // work | shortcut | capture | transfer | asking | idle
  const running = () => phase === "shortcut" || phase === "capture" || phase === "transfer";
  const at = (ms, step) => {
    const run = token;
    timers.push(setTimeout(() => { if (run === token) step(); }, ms));
  };
  const cancelFlow = () => {
    token++;
    timers.forEach(clearTimeout);
    timers = [];
  };

  const keys = $$(".os-keys kbd");
  const pillText = $("[data-pill-text]");
  const trayButton = $("[data-tray]");
  const stage = $(".xfer");
  let clipboard = null; // the snapshot on the (simulated) clipboard
  let pending = null; // the snapshot of the capture in progress
  let motions = []; // Web Animations of the current transfer

  // The small status pill: Capturing → Extracting context → Assembling snapshot → Attached → Ready to ask.
  const setPill = (text) => {
    if (text) pillText.textContent = text;
    os.classList.toggle("is-status", Boolean(text));
  };
  const clearOverlays = () => {
    for (const win of Object.values(wins)) win.classList.remove("is-reading");
    os.classList.remove("is-keys");
    setPill("");
    keys.forEach((key) => key.classList.remove("is-down"));
    motions.forEach((motion) => motion.cancel());
    motions = [];
    stage.replaceChildren();
    fx.stop(layer);
    for (const side of ["left", "top", "width", "height"]) layer.style[side] = "";
  };
  const move = (element, keyframes, options) => {
    const motion = element.animate(keyframes, { fill: "forwards", ...options });
    motions.push(motion);
    return motion;
  };

  // Shortcut: the visitor pressed the keys, chose Capture in the tray menu, or the replay pressed them.
  // A capture that is still running is never interrupted by another one.
  function shortcut(source) {
    if (running() && source !== "auto") return;
    cancelFlow();
    clearOverlays();
    closeMenu("start");
    closeMenu("tray");
    if (source !== "auto") mode = "manual";
    phase = "shortcut";
    setStep("capture");
    os.classList.add("is-keys");
    const step = reduce.matches ? 0 : 110;
    keys.forEach((key, index) => at(index * step, () => key.classList.add("is-down")));
    at(keys.length * step + (reduce.matches ? 150 : 160), capture);
  }

  // Stage A: the equalizer runs around the active window while it pulls back slightly; the snapshot is
  // taken at this moment, from the desktop as it is.
  function capture() {
    phase = "capture";
    keys.forEach((key) => key.classList.remove("is-down"));
    os.classList.remove("is-keys");
    setPill("Capturing");
    // The bars run around the whole sample desktop, which pulls back slightly: the capture is of the
    // desktop, not of one window. The sheets still lift out of the active window afterwards.
    const source = desk.active();
    const area = relTo(screen, os);
    layer.style.left = `${Math.round(area.x)}px`;
    layer.style.top = `${Math.round(area.y)}px`;
    layer.style.width = `${Math.round(area.w)}px`;
    layer.style.height = `${Math.round(area.h)}px`;
    fx.play(layer, { width: Math.round(area.w), height: Math.round(area.h), zoom: inner, origin: "50% 45%" });
    announce("ContextSnap is capturing the sample desktop.");
    const snap = desk.snapshot();
    pending = snap;
    // The sheets lift off while the bars are sinking back, so the two stages read as one motion.
    at(Math.max(0, fx.duration - 320), () => extract(snap, source));
  }

  // Stage B: the clipboard collects each window in turn. The window glows, a card with its data rises
  // out of it and travels onto the clipboard, where it is stacked as a page with its source's icon.
  // Then the stacked clipboard goes into the chat. A fixed timeline; no randomness.
  const COLLECT = {
    browser: { label: "Browser context", icon: "chrome" },
    pdf: { label: "PDF document", icon: "pdf" },
    notes: { label: "Notes", icon: "notes" },
  };
  const BARS = [92, 76, 84, 58, 70];
  const TILT = "rotateX(16deg) rotateY(-12deg) rotateZ(-3deg)";
  const SPRING = "cubic-bezier(0.34, 1.4, 0.64, 1)";
  const EASE = "cubic-bezier(0.22, 0.8, 0.24, 1)";

  function extract(snap) {
    phase = "transfer";
    setStep("extract");
    setPill("");
    announce("ContextSnap is pulling the context out of each open window.");
    const apps = ["browser", "pdf", "notes"].filter((app) => app in snap.textOf);
    const box = relTo(screen);

    // The clipboard, in the middle of the screen, tilted toward the viewer.
    const W = Math.round(Math.min(130, Math.max(96, box.w * 0.1)));
    const H = Math.round(W * 1.28);
    const C = { x: box.w * 0.5, y: Math.min(box.h - H / 2 - 18, Math.max(H / 2 + 18, box.h * 0.5)) };
    const holder = el("div", "clipb");
    holder.style.width = `${W}px`;
    holder.style.height = `${H}px`;
    holder.style.left = `${Math.round(C.x - W / 2)}px`;
    holder.style.top = `${Math.round(C.y - H / 2)}px`;
    const board = el("div", "clipb-board");
    const papers = el("div", "clipb-papers");
    const clip = el("span", "clipb-clip");
    const label = el("p", "clipb-label");
    const labelText = el("span", "", "ContextSnap");
    label.append(el("span", "clipb-check"), labelText);
    board.append(papers, clip, label);
    holder.append(board);
    stage.append(holder);
    // The paper area of the clipboard (see .clipb-papers): where each card lands, at exactly its size.
    const paper = { w: W * 0.82, h: H * 0.67 };
    const P = { x: C.x, y: C.y - H / 2 + H * (0.16 + 0.67 / 2) };

    const addPage = (app) => {
      const page = el("span", "clipb-paper");
      page.style.setProperty("--i", String(papers.children.length));
      if (app) page.append(sim.appIcon(COLLECT[app].icon, "clipb-ic"));
      // Text-like bars, not readable text: it reads as data without being words.
      for (const width of BARS) {
        const bar = el("i", "clipb-bar");
        bar.style.width = `${width}%`;
        page.append(bar);
      }
      papers.append(page);
      move(clip, [{ transform: "scaleY(0.8)" }, { transform: "scaleY(1)" }], { duration: 220, easing: SPRING, fill: "none" });
      move(board, [{ transform: `translateY(3px) ${TILT} scale(0.97)` }, { transform: `${TILT} scale(1)` }], { duration: 300, easing: SPRING });
    };
    const finish = () => {
      labelText.textContent = `${apps.length} ${apps.length === 1 ? "source" : "sources"}`;
      label.classList.add("is-done");
      copied(snap);
    };

    if (reduce.matches || !apps.length) {
      apps.forEach((app) => addPage(app));
      finish();
      at(reduce.matches ? 500 : 300, () => handoff(snap, holder, board));
      return;
    }
    move(board, [
      { transform: `translateY(20px) ${TILT} scale(0.72)`, opacity: 0 },
      { transform: `${TILT} scale(1)`, opacity: 1 },
    ], { duration: 420, easing: SPRING });

    const STEP = 1050;
    apps.forEach((app, i) => {
      const start = 260 + i * STEP;
      const pull = start + 220;
      const PULL = 700;

      at(start, () => {
        scriptedChange(() => desk.focusApp(app));
        wins[app].classList.add("is-reading");
        const win = relTo(wins[app]);
        const tag = el("span", "cselect");
        tag.append(el("i"), "Collecting");
        tag.style.left = `${Math.round(win.x + 10)}px`;
        tag.style.top = `${Math.round(win.y + 40)}px`;
        tag.dataset.app = app;
        stage.append(tag);
        move(tag, [{ opacity: 0, transform: "translateY(-4px) scale(0.9)" }, { opacity: 1, transform: "none" }], { duration: 220, easing: EASE });
      });

      // The data card rises out of the window, then travels onto the clipboard.
      let card = null;
      at(pull, () => {
        const win = relTo(wins[app]);
        card = el("div", "cdata");
        const head = el("p", "cdata-head");
        head.append(sim.appIcon(COLLECT[app].icon, "cdata-ic"), el("b", "", COLLECT[app].label));
        card.append(head, ...BARS.slice(0, 3).map((width) => {
          const bar = el("i", "cdata-bar");
          bar.style.width = `${width}%`;
          return bar;
        }));
        // Page-shaped from the start: the clipboard page's proportions, larger while it travels.
        const cw = Math.round(Math.min(150, Math.max(110, win.w * 0.36)));
        const ch = Math.round(cw * (paper.h / paper.w));
        card.style.width = `${cw}px`;
        card.style.height = `${ch}px`;
        stage.append(card);
        const x0 = win.x + win.w / 2 - cw / 2;
        const y0 = win.y + win.h / 2 - ch / 2;
        card.style.left = `${Math.round(x0)}px`;
        card.style.top = `${Math.round(y0)}px`;
        const dx = P.x - (x0 + cw / 2);
        const dy = P.y - (y0 + ch / 2);
        const k = paper.w / cw; // lands at exactly the clipboard page's size, tilted like the board
        move(card, [
          { transform: "translate3d(0px, 10px, 0px) scale(0.9)", opacity: 0 },
          { transform: "translate3d(0px, -8px, 0px) scale(1)", opacity: 1, offset: 0.3 },
          { transform: `translate3d(${dx * 0.5}px, ${dy * 0.5 - 34}px, 20px) rotateX(8deg) rotateY(-6deg) scale(${(1 + k) / 2})`, opacity: 1, offset: 0.65 },
          { transform: `translate3d(${dx}px, ${dy}px, 2px) ${TILT} scale(${k})`, opacity: 1 },
        ], { duration: PULL, easing: "cubic-bezier(0.4, 0, 0.2, 1)" });
      });

      // In: the card becomes the top page of the stack, the window lets go.
      at(pull + PULL, () => {
        card?.remove();
        addPage(app);
        labelText.textContent = `${COLLECT[app].label} · ${i + 1}/${apps.length}`;
        label.classList.add("is-done");
        wins[app].classList.remove("is-reading");
        stage.querySelector(`.cselect[data-app="${app}"]`)?.remove();
      });
    });

    const done = 260 + (apps.length - 1) * STEP + 220 + 700 + 160;
    at(done, finish);
    at(done + 700, () => handoff(snap, holder, board));
  }

  // Stage C: the clipboard carries the collected context to the chat and hands it over as the attachment.
  function handoff(snap, holder, board) {
    scriptedChange(() => desk.focusApp("assistant"));
    if (reduce.matches) {
      holder.remove();
      attached(snap);
      return;
    }
    const from = relTo(holder);
    const slot = relTo(composer);
    const dx = slot.x + 24 + 60 - (from.x + from.w / 2);
    const dy = slot.y + 24 - (from.y + from.h / 2);
    move(board, [{ transform: `${TILT} scale(1)` }, { transform: "none" }], { duration: 520, easing: "cubic-bezier(0.45, 0, 0.2, 1)" });
    move(holder, [
      { transform: "none", opacity: 1 },
      { transform: `translate(${dx * 0.9}px, ${dy * 0.9 - 14}px) scale(0.62)`, opacity: 1, offset: 0.75 },
      { transform: `translate(${dx}px, ${dy}px) scale(0.36)`, opacity: 0 },
    ], { duration: 620, easing: "cubic-bezier(0.45, 0, 0.2, 1)" });
    at(480, () => attached(snap));
  }

  // On the clipboard now: the tray icon shows it, and Ctrl+V in the chat can paste it again.
  function copied(snap) {
    clipboard = snap;
    trayButton.classList.add("has-clip");
    trayButton.classList.remove("is-ping");
    void trayButton.offsetWidth;
    trayButton.classList.add("is-ping");
  }

  function attached(snap) {
    attach(snap);
    setStep("attach");
    setPill("Attached");
    announce("ContextSnap.md is attached in the chat.");
    at(reduce.matches ? 0 : 160, () => {
      motions.forEach((motion) => motion.cancel());
      motions = [];
      stage.replaceChildren();
    });
    at(reduce.matches ? 300 : 750, () => {
      phase = "asking";
      setStep("ask");
      setPill("Ready to ask");
      if (mode === "auto") typeQuestion(QUESTIONS[0].text);
      else input.focus({ preventScroll: true });
    });
    at(2400, () => setPill(""));
  }

  // Paste (Ctrl+V or a paste in the chat): attaches what is on the simulated clipboard.
  function paste() {
    if (running()) return;
    if (!clipboard) {
      chatNotice("Nothing to paste yet. Capture first with Ctrl + Alt + Shift + C.");
      return;
    }
    scriptedChange(() => desk.focusApp("assistant"));
    attach(clipboard);
    phase = "asking";
    setStep("ask");
    input.focus({ preventScroll: true });
  }

  // The replay: the whole sequence, start to finish, from a fresh desktop.
  function replay() {
    cancelFlow();
    clearOverlays();
    closeMenu("start");
    closeMenu("tray");
    closeModelMenu();
    clipboard = null;
    trayButton.classList.remove("has-clip");
    newChat();
    maximized.clear();
    for (const win of Object.values(wins)) { win.style.left = ""; win.style.top = ""; }
    mode = "auto";
    phase = "work";
    setStep("work");
    scriptedChange(() => {
      desk.reset();
      desk.focusApp("browser");
    });
    announce("Replaying: a research session in a browser, a paper, and notes is captured with ContextSnap and summarized by the AI chat.");
    // The desktop as it is for a moment, then the shortcut; the collection highlights each window in turn.
    at(reduce.matches ? 300 : 1400, () => shortcut("auto"));
  }

  // The visitor took over (changed the desktop, typed, or chose something): stop driving the sequence.
  // A transfer in progress completes at once, so its snapshot is never lost.
  function takeOver() {
    if (mode !== "auto" && !running()) return;
    mode = "manual";
    cancelFlow();
    if (phase === "transfer") {
      clearOverlays();
      copied(pending);
      attach(pending);
      setStep("ask");
      phase = "asking";
    } else if (phase === "asking") {
      if (typingQuestion) stopTypingQuestion();
    } else {
      clearOverlays();
      setStep(null);
      phase = "idle";
    }
  }

  for (const button of $$("[data-os-replay]", section)) button.addEventListener("click", replay);
  for (const button of $$("[data-os-reset]", section)) {
    button.addEventListener("click", () => {
      cancelFlow();
      clearOverlays();
      closeMenu("start");
      clipboard = null;
      trayButton.classList.remove("has-clip");
      newChat();
      maximized.clear();
      for (const win of Object.values(wins)) { win.style.left = ""; win.style.top = ""; }
      mode = "manual";
      phase = "idle";
      setStep(null);
      scriptedChange(() => desk.reset());
      announce("Sample desktop reset.");
    });
  }

  // The sample AI chat ----------------------------------------------------------------------------------
  const chatRoot = $(".ca");
  const log = $("[data-ca-log]");
  const empty = $("[data-ca-empty]");
  const form = $("[data-ca-form]");
  const composer = form;
  const input = $("[data-ca-input]");
  const attachBox = $("[data-ca-attach]");
  const suggest = $("[data-ca-suggest]");
  const inspector = $("[data-ca-inspector]");
  const modelButton = $("[data-model-button]");
  const modelMenu = $("[data-model-menu]");

  let model = { ...MODELS[0], name: MODELS[0].models[0] };
  const mark = (entry) => {
    const badge = el("span", `pv pv-${entry.mark}`);
    badge.setAttribute("aria-hidden", "true");
    badge.append(sim.appIcon(entry.mark, "pv-logo"));
    return badge;
  };
  const renderModel = () => {
    const current = $("[data-model-mark]");
    current.className = `pv pv-${model.mark}`;
    current.replaceChildren(sim.appIcon(model.mark, "pv-logo"));
    current.setAttribute("aria-hidden", "true");
    $(".ca").dataset.skin = model.mark;
    // The browser window is that product's tab: its title and icon follow (and so does the snapshot).
    desk.APPS.assistant.title = () => `${model.product} - Google Chrome`;
    desk.APPS.assistant.name = model.product;
    // Its window, taskbar button, and Start menu entry show that product's logo and name.
    $('[data-app="assistant"] .win-ic use').setAttribute("href", `#ic-${model.mark}`);
    const taskButton = $('.tb-app[data-app="assistant"]');
    taskButton.querySelector("use").setAttribute("href", `#ic-${model.mark}`);
    taskButton.setAttribute("aria-label", model.product);
    $('.start-app[data-open="assistant"] use').setAttribute("href", `#ic-${model.mark}`);
    $("[data-chat-app-name]").textContent = model.product;
    render();
    $("[data-model-name]").textContent = model.name;
    modelButton.setAttribute("aria-label", `Model: ${model.name} by ${model.provider} (shown as ${model.product}). Change model`);
    input.placeholder = model.placeholder;
    for (const option of $$("[role=option]", modelMenu)) option.setAttribute("aria-selected", String(option.dataset.model === model.name));
  };
  // One row per chat: its logo and name.
  for (const entry of MODELS) {
    for (const name of entry.models) {
      const option = el("div", "ca-option ca-option-product");
      option.append(mark(entry), el("span", "", name));
      option.setAttribute("role", "option");
      option.tabIndex = -1;
      option.dataset.model = name;
      option.dataset.provider = entry.provider;
      modelMenu.append(option);
    }
  }
  modelMenu.append(el("p", "ca-menu-note", "Demo: answers are scripted. No AI is called."));
  const options = () => $$("[role=option]", modelMenu);
  function closeModelMenu(focusButton) {
    if (modelMenu.hidden) return;
    modelMenu.hidden = true;
    modelButton.setAttribute("aria-expanded", "false");
    if (focusButton) modelButton.focus({ preventScroll: true });
  }
  const openModelMenu = () => {
    modelMenu.hidden = false;
    modelButton.setAttribute("aria-expanded", "true");
    (options().find((option) => option.dataset.model === model.name) ?? options()[0]).focus({ preventScroll: true });
  };
  const chooseModel = (option) => {
    const entry = MODELS.find((item) => item.provider === option.dataset.provider);
    model = { ...entry, name: option.dataset.model };
    renderModel();
    closeModelMenu(true);
    announce(`Model label set to ${model.name}. The demo answers the same way for every model.`);
  };
  modelButton.addEventListener("click", () => (modelMenu.hidden ? openModelMenu() : closeModelMenu()));
  modelMenu.addEventListener("click", (event) => {
    const option = event.target.closest("[role=option]");
    if (option) chooseModel(option);
  });
  modelMenu.addEventListener("keydown", (event) => {
    const list = options();
    const index = list.indexOf(document.activeElement);
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      list[(index + (event.key === "ArrowDown" ? 1 : -1) + list.length) % list.length].focus();
    } else if (event.key === "Home" || event.key === "End") {
      event.preventDefault();
      list[event.key === "Home" ? 0 : list.length - 1].focus();
    } else if ((event.key === "Enter" || event.key === " ") && index >= 0) {
      event.preventDefault();
      chooseModel(list[index]);
    } else if (event.key === "Escape" || event.key === "Tab") {
      closeModelMenu(event.key === "Escape");
    }
  });
  renderModel();

  // Attachment in the composer
  let attachment = null; // snapshot attached to the next message
  let conversationSnap = null; // the latest snapshot sent in this conversation
  function attach(snap) {
    if (!snap) return;
    attachment = snap;
    attachBox.hidden = false;
    attachBox.classList.remove("is-new");
    void attachBox.offsetWidth;
    attachBox.classList.add("is-new");
    $("[data-ca-attach-meta]").textContent = `${snap.parts.length} ${snap.parts.length === 1 ? "part" : "parts"} · ${(snap.markdown.length / 1000).toFixed(1)}k chars`;
  }
  const detach = () => {
    attachment = null;
    attachBox.hidden = true;
  };
  $("[data-ca-remove]").addEventListener("click", () => { detach(); input.focus({ preventScroll: true }); });
  const inspect = (snap) => {
    $("[data-ca-inspect-meta]").textContent = `${snap.parts.length} ${snap.parts.length === 1 ? "part" : "parts"} · ${sim.LEVELS[snap.level].name}`;
    sim.renderMarkdown($("[data-ca-inspect-md]"), snap.markdown);
    inspector.hidden = false;
    $("[data-ca-inspect-close]").focus({ preventScroll: true });
  };
  $("[data-ca-inspect]").addEventListener("click", () => attachment && inspect(attachment));
  $("[data-ca-inspect-close]").addEventListener("click", () => { inspector.hidden = true; });
  log.addEventListener("click", (event) => {
    const chip = event.target.closest("[data-snap-index]");
    if (chip) inspect(sentSnaps[Number(chip.dataset.snapIndex)]);
  });

  // Pasting into the chat inserts the sample snapshot only; the visitor's clipboard is never read.
  input.addEventListener("paste", (event) => {
    event.preventDefault();
    if (clipboard) paste();
    else chatNotice("This demo only pastes the sample snapshot. Capture first with Ctrl + Alt + Shift + C.");
  });

  // Composer: Enter sends, Shift+Enter adds a line, the box grows with its text.
  const grow = () => {
    input.style.height = "auto";
    input.style.height = `${Math.min(input.scrollHeight, 120)}px`;
  };
  input.addEventListener("input", () => { takeOver(); grow(); });
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      form.requestSubmit();
    }
  });
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    send(input.value);
  });

  const QUESTION_CHIPS = QUESTIONS.map((question) => {
    const button = el("button", "ca-chip", question.text);
    button.type = "button";
    button.addEventListener("click", () => { takeOver(); send(question.text, question.id); });
    return button;
  });
  suggest.append(...QUESTION_CHIPS);

  const sentSnaps = [];
  let chatToken = 0;
  let chatTimers = [];
  const later = (ms, step) => {
    const run = chatToken;
    chatTimers.push(setTimeout(() => { if (run === chatToken) step(); }, ms));
  };
  function newChat() {
    chatToken++;
    chatTimers.forEach(clearTimeout);
    chatTimers = [];
    stopTypingQuestion();
    log.replaceChildren(empty);
    empty.hidden = false;
    inspector.hidden = true;
    detach();
    conversationSnap = null;
    sentSnaps.length = 0;
    input.value = "";
    grow();
    suggest.hidden = false;
  }
  $("[data-new-chat]").addEventListener("click", () => { takeOver(); newChat(); input.focus({ preventScroll: true }); });

  function chatNotice(text) {
    const note = el("p", "ca-notice", text);
    log.append(note);
    log.scrollTop = log.scrollHeight;
    setTimeout(() => note.remove(), 4000);
  }

  let typingQuestion = null;
  function typeQuestion(text) {
    stopTypingQuestion();
    const per = reduce.matches ? 0 : 24;
    let index = 0;
    typingQuestion = setInterval(() => {
      index = Math.min(text.length, index + 1);
      input.value = text.slice(0, index);
      grow();
      if (index === text.length || per === 0) {
        input.value = text;
        stopTypingQuestion();
        at(reduce.matches ? 100 : 350, () => send(text, QUESTIONS[0].id));
      }
    }, per || 1);
  }
  function stopTypingQuestion() {
    clearInterval(typingQuestion);
    typingQuestion = null;
  }

  // Which sample question a free-text message is closest to.
  const classify = (text) => {
    if (/source|cite|back|support|3\.1|evidence|where/i.test(text)) return "sources";
    if (/miss|gap|open|todo|next|left|unknown/i.test(text)) return "gaps";
    return "summary";
  };

  function send(text, id = classify(text)) {
    const question = text.trim();
    if (!question && !attachment) return;
    stopTypingQuestion();
    empty.hidden = true;
    const message = el("div", "ca-msg ca-me");
    if (attachment) {
      sentSnaps.push(attachment);
      conversationSnap = attachment;
      const chip = el("button", "ca-sent-file");
      chip.type = "button";
      chip.dataset.snapIndex = String(sentSnaps.length - 1);
      chip.append(el("span", "md-ic", "MD"), el("span", "", "ContextSnap.md"));
      chip.setAttribute("aria-label", "Open the attached ContextSnap.md");
      message.append(chip);
      detach();
    }
    if (question) message.append(el("p", "ca-bubble", question));
    log.append(message);
    input.value = "";
    grow();
    log.scrollTop = log.scrollHeight;
    if (!question) return;
    const snap = conversationSnap;
    const thinking = el("div", "ca-msg ca-ai ca-thinking");
    thinking.append(mark(model), el("span", "ca-dots"));
    thinking.setAttribute("aria-label", `${model.name} is typing`);
    later(250, () => {
      log.append(thinking);
      log.scrollTop = log.scrollHeight;
    });
    later(reduce.matches ? 400 : 1100, () => {
      thinking.remove();
      log.append(reply(id, snap));
      log.scrollTop = log.scrollHeight;
      if (mode === "auto" || phase === "asking") {
        setStep("answer");
        phase = "idle";
        mode = "manual";
      }
      announce("The sample assistant answered from the snapshot.");
    });
  }

  // The answer, as small blocks: paragraphs with inline code, a code block, a list, and the parts of
  // the snapshot it used. All of it is derived from the snapshot that was pasted.
  const reply = (id, snap) => {
    const { blocks, used } = content.answer(id, snap);
    const message = el("div", "ca-msg ca-ai");
    message.append(mark(model));
    const body = el("div", "ca-body");
    for (const block of blocks) {
      if (block.p) {
        const paragraph = el("p");
        sim.fillText(paragraph, block.p);
        body.append(paragraph);
      } else if (block.code) {
        const pre = el("pre", "ca-code");
        pre.append(el("span", "ca-code-lang", "ts"), el("code", "", block.code));
        body.append(pre);
      } else if (block.list) {
        const list = el("ul");
        for (const item of block.list) {
          const li = el("li");
          sim.fillText(li, item);
          list.append(li);
        }
        body.append(list);
      }
    }
    if (snap && used.length) {
      const footer = el("p", "ca-used");
      footer.append(el("span", "ca-used-label", "From ContextSnap.md"));
      for (const label of used) footer.append(el("span", "ca-used-item", label));
      body.append(footer);
    }
    message.append(body);
    return message;
  };

  // Keyboard: the shortcut and Ctrl+V while the desktop is on screen --------------------------------------
  let inView = false;
  new IntersectionObserver(([entry]) => { inView = entry.isIntersecting && entry.intersectionRatio >= 0.5; }, { threshold: [0, 0.5, 1] }).observe(os);
  document.addEventListener("keydown", (event) => {
    if (!inView) return;
    const isShortcut = event.ctrlKey && event.altKey && event.shiftKey && (event.code === "KeyC" || event.key.toLowerCase() === "c");
    if (isShortcut) {
      event.preventDefault();
      if (!event.repeat) shortcut("physical");
      return;
    }
    if (event.key === "Escape") {
      closeMenu("start");
      closeMenu("tray");
      inspector.hidden = true;
    }
    const pasteKey = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.code === "KeyV";
    if (pasteKey && clipboard && !attachment && !isTyping(event.target)) {
      event.preventDefault();
      paste();
    }
  }, true);
  // ContextSnap itself takes the C press when it is running (see keyboard-demo.js); only its release reaches us.
  let cDown = false;
  document.addEventListener("keydown", (event) => { if (event.code === "KeyC") cDown = true; }, true);
  document.addEventListener("keyup", (event) => {
    const handedOff = event.code === "KeyC" && event.ctrlKey && event.altKey && event.shiftKey && !cDown;
    if (event.code === "KeyC") cDown = false;
    if (handedOff && inView) shortcut("physical");
  }, true);

  // The replay plays once by itself the first time the desktop is mostly on screen.
  let played = false;
  new IntersectionObserver(([entry]) => {
    if (played || !entry.isIntersecting) return;
    played = true;
    replay();
  }, { threshold: 0.6 }).observe(os);

  window.contextSnapOS = { ownsKeyboard: () => inView, replay };
  render();
})();
