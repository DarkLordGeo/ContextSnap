// The sample Windows desktop: windows, taskbar, Start and tray menus, a sample AI chat, and the whole
// Snapctx workflow played out on it: work → shortcut → copy → paste → ask → answer. The desktop
// state and the snapshot come from workspace-sim.js; the capture lights come from capture-fx.js.
// Everything is simulated in the page. Nothing on the visitor's computer is read (not even the real
// clipboard: pasting into the sample chat only ever inserts the sample snapshot), and no model or
// service is called.
(() => {
  const os = document.getElementById("os");
  const sim = window.contextSnapSim;
  const desk = sim?.desktop; // this demo's own sample desktop
  const fx = window.contextSnapFX;
  if (!os || !sim || !fx) return;

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

  // Models in the sample chat. Only the label changes: answers are scripted locally from the snapshot,
  // whichever model is picked. Keep this list current as model names change.
  const MODELS = [
    { provider: "OpenAI", mark: "openai", letter: "O", models: ["GPT-5", "GPT-5 mini", "o3"] },
    { provider: "Anthropic", mark: "anthropic", letter: "A", models: ["Claude Sonnet 5.5", "Claude Opus 5.5", "Claude Haiku 5.5"] },
    { provider: "Google", mark: "google", letter: "G", models: ["Gemini 2.5 Pro", "Gemini 2.5 Flash"] },
    { provider: "xAI", mark: "xai", letter: "x", models: ["Grok 4", "Grok 3"] },
    { provider: "DeepSeek", mark: "deepseek", letter: "D", models: ["DeepSeek-V3", "DeepSeek-R1"] },
    { provider: "Meta", mark: "meta", letter: "M", models: ["Llama 4 Maverick", "Llama 4 Scout"] },
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
  const editor = $(".editor");
  const highlight = $(".code-hl");
  const gutter = $(".gutter");
  const termOut = $(".term-out");
  const termCmds = $(".term-cmds");
  const filesList = $(".files-list");
  const sysinfo = $(".sysinfo");
  const taskButtons = $$(".tb-app");
  const maximized = new Set();

  const TOKENS = /(\/\/.*$)|("(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`)|\b(import|from|export|return|if|await|throw|async)\b|\b(const|let|function|new|true|false|null)\b|\b(\d+(?:\.\d+)?)\b|\b([A-Z][A-Za-z]*)\b|([A-Za-z_$][\w$]*)(?=\s*\()/gm;
  const TOKEN_CLASS = [null, "tk-com", "tk-str", "tk-ctl", "tk-kw", "tk-num", "tk-type", "tk-fn"];
  const renderCode = (text, json) => {
    const fragment = document.createDocumentFragment();
    let last = 0;
    for (const match of text.matchAll(TOKENS)) {
      if (match.index > last) fragment.append(text.slice(last, match.index));
      const group = match.findIndex((value, index) => index > 0 && value !== undefined);
      let className = TOKEN_CLASS[group];
      if (json && group === 2) className = /^\s*:/.test(text.slice(match.index + match[0].length)) ? "tk-key" : "tk-str";
      fragment.append(el("span", className, match[0]));
      last = match.index + match[0].length;
    }
    fragment.append(text.slice(last) + "\n");
    highlight.replaceChildren(fragment);
    gutter.textContent = text.split("\n").map((_, index) => index + 1).join("\n");
  };
  const syncScroll = () => {
    highlight.scrollTop = editor.scrollTop;
    highlight.scrollLeft = editor.scrollLeft;
    gutter.scrollTop = editor.scrollTop;
  };

  const termClass = (line) => {
    if (/^HTTP\/1\.1 [45]|^Cannot GET|modified:/.test(line)) return "t-err";
    if (/^HTTP\/1\.1 2|^Server listening/.test(line)) return "t-ok";
    if (/^> /.test(line)) return "t-dim";
    if (/^\{/.test(line)) return "t-str";
    return "";
  };
  const renderTerminal = (lines, typing) => {
    const fragment = document.createDocumentFragment();
    const promptLine = (command, caret) => {
      const line = el("span", "t-line");
      line.append(el("span", "t-prompt", `${sim.PROMPT} `));
      const [head, ...rest] = command.split(" ");
      line.append(el("span", "t-cmd", head));
      if (rest.length) line.append(el("span", "t-arg", ` ${rest.join(" ")}`));
      if (caret) line.append(el("span", "t-caret"));
      return line;
    };
    for (const text of lines) {
      if (text.startsWith(`${sim.PROMPT} `)) fragment.append(promptLine(text.slice(sim.PROMPT.length + 1)));
      else fragment.append(el("span", `t-line ${termClass(text)}`, text || " "));
    }
    fragment.append(promptLine(typing ?? "", true));
    termOut.replaceChildren(fragment);
    termOut.scrollTop = termOut.scrollHeight;
  };

  for (const command of ["npm run dev", sim.CURL, "git status", "clear"]) {
    const button = el("button", "cmd", command === sim.CURL ? "curl /api/users/42" : command);
    button.type = "button";
    button.dataset.command = command;
    termCmds.append(button);
  }
  for (const path of Object.keys(sim.FILES)) {
    const item = el("li");
    const button = el("button", "file");
    button.type = "button";
    button.dataset.path = path;
    const json = path.endsWith(".json");
    button.append(el("span", `file-ic${json ? " is-json" : ""}`, json ? "{}" : "TS"), el("span", "file-name", path.replace("src/", "")), el("span", "file-type", json ? "JSON File" : "TypeScript"));
    item.append(button);
    filesList.append(item);
  }
  $("[data-files-count]").textContent = `${Object.keys(sim.FILES).length} items`;

  let terminalTyping = null; // command being typed by the replay
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
    if (editor.value !== state.files[state.file]) editor.value = state.files[state.file];
    const json = state.file.endsWith(".json");
    renderCode(editor.value, json);
    syncScroll();
    $("[data-file-tab]").textContent = state.file.split("/").pop();
    $("[data-lang]").textContent = json ? "JSON" : "TypeScript";
    const badge = $("[data-lang-badge]");
    badge.textContent = json ? "{}" : "TS";
    badge.classList.toggle("is-json", json);
    renderTerminal(state.terminal, terminalTyping);
    for (const button of $$(".file", filesList)) {
      const selected = button.dataset.path === state.file;
      button.classList.toggle("is-selected", selected);
      if (selected) button.setAttribute("aria-current", "true");
      else button.removeAttribute("aria-current");
    }
    const rows = [["OS Name", "Windows 11 Pro"], ["System type", "x64-based PC"], ["Processors", "12 logical"], ["Memory", "31.7 GB"], ["Foreground", current ? desk.APPS[current].process : "None"]];
    sysinfo.replaceChildren(...rows.flatMap(([term, value]) => [el("dt", "", term), el("dd", "", value)]));
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
    if (!bar || event.target.closest(".cap") || narrow.matches || maximized.has(app) || event.button !== 0) return;
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
    } else if (target.closest(".file")) {
      const path = target.closest(".file").dataset.path;
      change((state) => { state.file = path; desk.focusApp("editor"); });
    } else if (target.closest(".cmd")) {
      const command = target.closest(".cmd").dataset.command;
      change(() => { desk.runCommand(command); desk.focusApp("terminal"); });
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
  editor.addEventListener("input", () => {
    takeOver();
    desk.update((state) => { state.files[state.file] = editor.value; });
  });
  editor.addEventListener("scroll", syncScroll);

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
  // Capture the active window → extract its context as layered sheets → assemble Snapctx.md in the
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
    announce("Snapctx is capturing the sample desktop.");
    const snap = desk.snapshot();
    pending = snap;
    // The sheets lift off while the bars are sinking back, so the two stages read as one motion.
    at(Math.max(0, fx.duration - 320), () => extract(snap, source));
  }

  // The sheets are the snapshot's parts, the same ones as the context cards on the page, at most five.
  const sheetsFor = (snap) => snap.parts.slice(0, 5).map((part) => ({
    label: part.id === "editor" ? `Code · ${snap.file}` : part.category,
    icon: part.source.icon,
    lines: sim.previewLines(part),
  }));
  const sheetElement = (sheet, w, h) => {
    const element = el("div", "sheet");
    element.style.width = `${w}px`;
    element.style.height = `${h}px`;
    const label = el("p", "sheet-label");
    label.append(sim.appIcon(sheet.icon), el("span", "", `## ${sheet.label}`));
    element.append(label, el("pre", "sheet-lines", sheet.lines.join("\n")));
    return element;
  };

  // Stage B and C: sheets lift out of the captured window in a shallow fan, fly onto a small clipboard
  // and slide under its clip, stacking into one document, Snapctx.md (the real app copies to the
  // clipboard). The clipboard then carries it to the chat, where it becomes the attachment.
  const TILT = "rotateX(16deg) rotateY(-12deg) rotateZ(-3deg)";
  const SPRING = "cubic-bezier(0.34, 1.4, 0.64, 1)";
  function extract(snap, source) {
    phase = "transfer";
    setStep("extract");
    setPill("Extracting context");
    announce("Extracting context into a Markdown snapshot.");
    const box = relTo(screen);
    const fromWin = source && desk.visible(source) ? relTo(wins[source]) : { x: box.w * 0.3, y: box.h * 0.3, w: box.w * 0.4, h: box.h * 0.4 };
    // The chat comes forward now, so the document has somewhere to go.
    scriptedChange(() => desk.focusApp("assistant"));
    if (reduce.matches) {
      clearOverlays();
      copied(snap);
      setPill("Assembling snapshot");
      at(250, () => attached(snap));
      return;
    }
    const sheets = sheetsFor(snap);
    const n = sheets.length;
    const w = Math.round(Math.min(190, Math.max(130, box.w * 0.15)));
    const h = Math.round(w * 0.64);
    // Origin: the middle of the captured window, kept far enough from the screen edges for the fan.
    const S = {
      x: Math.min(Math.max(fromWin.x + fromWin.w / 2, w * 1.8), box.w - w * 1.8),
      y: Math.min(Math.max(fromWin.y + fromWin.h / 2, h * 1.6), box.h - h * 0.9),
    };
    const slot = relTo(composer);
    const T = { x: slot.x + 8 + Math.min(200, slot.w - 16) / 2, y: slot.y + 26 };

    // The clipboard: halfway between the window and the chat, compact, tilted toward the viewer.
    const W = Math.round(Math.min(122, Math.max(92, box.w * 0.095)));
    const H = Math.round(W * 1.28);
    const C = {
      x: Math.min(Math.max(S.x + (T.x - S.x) * 0.5, W / 2 + 14), box.w - W / 2 - 14),
      y: Math.min(Math.max((S.y + T.y) / 2 - H * 0.3, H / 2 + 14), box.h - H / 2 - 14),
    };
    const holder = el("div", "clipb");
    holder.style.width = `${W}px`;
    holder.style.height = `${H}px`;
    holder.style.left = `${Math.round(C.x - W / 2)}px`;
    holder.style.top = `${Math.round(C.y - H / 2)}px`;
    const board = el("div", "clipb-board");
    const papers = el("div", "clipb-papers");
    const clip = el("span", "clipb-clip");
    const label = el("p", "clipb-label");
    label.append(el("b", "", "Snapctx.md"), el("span", "clipb-check"));
    board.append(papers, clip, label);
    holder.append(board);
    stage.append(holder);
    move(board, [
      { transform: `translateY(20px) ${TILT} scale(0.72)`, opacity: 0 },
      { transform: `${TILT} scale(1)`, opacity: 1 },
    ], { duration: 480, easing: SPRING });

    const c = (n - 1) / 2;
    const fan = sheets.map((_, i) => ({
      x: (i - c) * w * 0.66,
      y: -h * 0.62 + Math.abs(i - c) * 12,
      z: 36 + i * 22,
      ry: -14 + (i - c) * 4,
      rz: (i - c) * 2.6,
    }));
    const fanned = (f) => `translate3d(${f.x}px, ${f.y}px, ${f.z}px) rotateX(16deg) rotateY(${f.ry}deg) rotateZ(${f.rz}deg) scale(1)`;
    const elements = sheets.map((sheet, i) => {
      const element = sheetElement(sheet, w, h);
      element.style.left = `${Math.round(S.x - w / 2)}px`;
      element.style.top = `${Math.round(S.y - h / 2)}px`;
      element.style.zIndex = String(i + 2);
      stage.append(element);
      move(element, [
        { transform: "translate3d(0px, 14px, 0px) rotateX(0deg) scale(0.62)", opacity: 0 },
        { transform: fanned(fan[i]), opacity: 1 },
      ], { duration: 560, delay: 80 + i * 90, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" });
      return element;
    });
    const emerged = 80 + 560 + (n - 1) * 90;

    // Each sheet takes its own arc onto the clipboard, ends just above the paper, then slides under the clip.
    at(emerged + 140, () => {
      setPill("Assembling snapshot");
      const P = { x: C.x, y: C.y + H * 0.08 }; // the middle of the paper, below the clip
      const k = (W * 0.8) / w;
      const FLIGHT = 700;
      const STEP = 110;
      elements.forEach((element, i) => {
        const arc = 34 + i * 12;
        const side = (i - c) * w * 0.22;
        move(element, [
          { transform: fanned(fan[i]), opacity: 1 },
          { transform: `translate3d(${(fan[i].x + P.x - S.x) * 0.5 + side}px, ${(fan[i].y + P.y - S.y) * 0.5 - arc}px, ${fan[i].z * 0.6}px) rotateX(12deg) rotateY(${(fan[i].ry - 12) / 2}deg) rotateZ(${fan[i].rz}deg) scale(${(1 + k) / 2})`, opacity: 1, offset: 0.55 },
          { transform: `translate3d(${P.x - S.x}px, ${P.y - S.y - H * 0.16}px, 6px) ${TILT} scale(${k})`, opacity: 1, offset: 0.86 },
          { transform: `translate3d(${P.x - S.x}px, ${P.y - S.y}px, 2px) ${TILT} scale(${k})`, opacity: 1 },
        ], { duration: FLIGHT, delay: i * STEP, easing: "cubic-bezier(0.45, 0, 0.25, 1)" });
        // Landing: the sheet becomes a page of the stack, the clip snaps, the board gives a little.
        at(FLIGHT + i * STEP, () => {
          move(element, [{ opacity: 1 }, { opacity: 0 }], { duration: 90 });
          const page = el("span", "clipb-paper");
          page.style.setProperty("--i", String(papers.children.length));
          papers.append(page);
          move(clip, [{ transform: "scaleY(0.82)" }, { transform: "scaleY(1)" }], { duration: 220, easing: SPRING, fill: "none" });
          move(board, [{ transform: `translateY(2px) ${TILT} scale(0.985)` }, { transform: `${TILT} scale(1)` }], { duration: 260, easing: SPRING });
        });
      });

      // Stacked: the top page shows the start of the real Markdown, the clipboard confirms.
      at(FLIGHT + (n - 1) * STEP + 160, () => {
        const top = papers.lastElementChild;
        const preview = el("pre", "clipb-md");
        sim.renderMarkdown(preview, snap.markdown.split("\n").filter((line) => line.trim() && !line.startsWith("```")).slice(0, 7).join("\n"));
        top?.append(preview);
        label.classList.add("is-done");
        copied(snap);
        move(board, [{ transform: `${TILT} scale(1)` }, { transform: `${TILT} scale(1.04)`, offset: 0.4 }, { transform: `${TILT} scale(1)` }], { duration: 360, easing: "ease-out" });

        // Then the clipboard carries the document to the chat and hands it over as the attachment.
        at(620, () => {
          const dx = T.x - C.x;
          const dy = T.y - C.y;
          move(board, [{ transform: `${TILT} scale(1)` }, { transform: "none" }], { duration: 560, easing: "cubic-bezier(0.45, 0, 0.2, 1)" });
          move(holder, [
            { transform: "none", opacity: 1 },
            { transform: `translate(${dx * 0.9}px, ${dy * 0.9 - 14}px) scale(0.62)`, opacity: 1, offset: 0.75 },
            { transform: `translate(${dx}px, ${dy}px) scale(0.36)`, opacity: 0 },
          ], { duration: 640, easing: "cubic-bezier(0.45, 0, 0.2, 1)" });
          at(520, () => attached(snap));
        });
      });
    });
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
    announce("Snapctx.md is attached in the chat.");
    at(reduce.matches ? 0 : 160, () => {
      motions.forEach((motion) => motion.cancel());
      motions = [];
      stage.replaceChildren();
    });
    at(reduce.matches ? 300 : 750, () => {
      phase = "asking";
      setStep("ask");
      setPill("Ready to ask");
      if (mode === "auto") typeQuestion(sim.QUESTIONS[0].text);
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
      desk.state.terminal = desk.state.terminal.slice(0, 4);
      desk.focusApp("terminal");
    });
    announce("Replaying: a developer hits a 404, captures the terminal with Snapctx, and asks the AI chat about it.");
    const command = sim.CURL;
    const per = reduce.matches ? 0 : 26;
    for (let i = 1; i <= command.length; i++) {
      at(300 + i * per, () => { terminalTyping = command.slice(0, i); renderTerminal(desk.state.terminal, terminalTyping); });
    }
    const typed = 300 + command.length * per + 200;
    at(typed, () => {
      terminalTyping = null;
      scriptedChange(() => desk.runCommand(command));
    });
    at(typed + (reduce.matches ? 300 : 700), () => shortcut("auto"));
  }

  // The visitor took over (changed the desktop, typed, or chose something): stop driving the sequence.
  // A transfer in progress completes at once, so its snapshot is never lost.
  function takeOver() {
    if (mode !== "auto" && !running()) return;
    mode = "manual";
    cancelFlow();
    if (terminalTyping !== null) { terminalTyping = null; renderTerminal(desk.state.terminal, null); }
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
    const badge = el("span", `pv pv-${entry.mark}`, entry.letter);
    badge.setAttribute("aria-hidden", "true");
    return badge;
  };
  const renderModel = () => {
    const current = $("[data-model-mark]");
    current.className = `pv pv-${model.mark}`;
    current.textContent = model.letter;
    current.setAttribute("aria-hidden", "true");
    $("[data-model-name]").textContent = model.name;
    modelButton.setAttribute("aria-label", `Model: ${model.name} by ${model.provider}. Change model`);
    input.placeholder = `Message ${model.name}`;
    for (const option of $$("[role=option]", modelMenu)) option.setAttribute("aria-selected", String(option.dataset.model === model.name));
  };
  for (const entry of MODELS) {
    const group = el("div", "ca-group");
    group.setAttribute("role", "group");
    const label = el("p", "ca-group-label");
    label.append(mark(entry), entry.provider);
    group.setAttribute("aria-label", entry.provider);
    group.append(label);
    for (const name of entry.models) {
      const option = el("div", "ca-option", name);
      option.setAttribute("role", "option");
      option.tabIndex = -1;
      option.dataset.model = name;
      option.dataset.provider = entry.provider;
      group.append(option);
    }
    modelMenu.append(group);
  }
  modelMenu.append(el("p", "ca-menu-note", "Website demo: answers are scripted from the snapshot. No model is called."));
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

  const QUESTION_CHIPS = sim.QUESTIONS.map((question) => {
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
        at(reduce.matches ? 100 : 350, () => send(text, sim.QUESTIONS[0].id));
      }
    }, per || 1);
  }
  function stopTypingQuestion() {
    clearInterval(typingQuestion);
    typingQuestion = null;
  }

  // Which sample question a free-text message is closest to.
  const classify = (text) => {
    if (/404|not found|error|fail|route|request|api|why/i.test(text)) return "404";
    if (/chang|modif|git|diff|commit|edit/i.test(text)) return "changes";
    if (/secret|password|sensitive|key|token|leak|credential|private/i.test(text)) return "secrets";
    return "other";
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
      chip.append(el("span", "md-ic", "MD"), el("span", "", "Snapctx.md"));
      chip.setAttribute("aria-label", "Open the attached Snapctx.md");
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
    const blocks = [];
    if (!snap) {
      blocks.push({ p: sim.answerFor(id === "other" ? "404" : id, null) });
    } else if (id === "404") {
      blocks.push({ p: snap.reply });
      if (/Rename it to/.test(snap.reply)) {
        blocks.push({ p: "Fix the route in `src/api.ts`:" }, { code: 'router.get("/api/users/:id", async (req, res) => {' });
      }
      const evidence = [];
      const terminal = snap.textOf.terminal ?? "";
      const latest = [...terminal.matchAll(/HTTP\/1\.1 (\d{3}.*)/g)].pop()?.[1];
      const failed = /Cannot GET (\S+)/.exec(terminal);
      if (latest?.startsWith("2")) evidence.push(`Terminal: the latest request returned \`${latest.trim()}\``);
      else if (failed) evidence.push(`Terminal: \`GET ${failed[1]}\` returned \`404 Not Found\``);
      if (snap.file === "src/api.ts" && "editor" in snap.textOf) {
        const routes = [...snap.textOf.editor.matchAll(/router\.\w+\(\s*["'`]([^"'`]+)/g)].map((m) => `\`${m[1]}\``);
        if (routes.length) evidence.push(`src/api.ts defines ${routes.join(", ")}`);
      }
      if (evidence.length) blocks.push({ list: evidence });
    } else if (id === "changes") {
      blocks.push({ p: sim.answerFor("changes", snap) });
      if (snap.hasGit) blocks.push({ list: ["`src/api.ts` (modified)", "`src/client.ts` (modified)"] });
    } else if (id === "secrets") {
      blocks.push({ p: sim.answerFor("secrets", snap) });
    } else {
      const seen = snap.sources.filter((source) => source.status === "included").map((source) => source.label);
      blocks.push({ p: "Here is what your snapshot shows:" }, { list: seen }, { p: "Ask me about the failing request, the files you changed, or anything sensitive in it." });
    }
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
    const used = sim.usedFor(id === "other" ? "404" : id, snap);
    if (snap && used.length) {
      const footer = el("p", "ca-used");
      footer.append(el("span", "ca-used-label", "From Snapctx.md"));
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
  // Snapctx itself takes the C press when it is running (see keyboard-demo.js); only its release reaches us.
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
