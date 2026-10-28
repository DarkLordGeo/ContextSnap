// The page's context collector: a small icon at the bottom right. Each capture of the keyboard demo at
// the top of the page hands it that snapshot (the Windows demo is not connected to it). A row of
// document cards made from the snapshot appears in the middle of the screen, lifts and fans, settles,
// gathers into a stack, and the stack is filed into the icon card by card, as if tucked under its clip.
// Opening the icon sends the same cards back out of it into the overlay.
// Clicking the icon opens the collected parts as cards over a blurred page. Everything shown is the
// sample desktop's data; nothing is read from the visitor's computer.
(() => {
  const button = document.querySelector("[data-collector]");
  const sim = window.contextSnapSim;
  if (!button || !sim) return;

  const badge = button.querySelector("[data-collector-count]");
  const flyLayer = document.querySelector(".collect-fly");
  const dialog = document.querySelector("[data-collect]");
  const meta = dialog.querySelector("[data-collect-meta]");
  const empty = dialog.querySelector("[data-collect-empty]");
  const list = dialog.querySelector("[data-collect-cards]");
  const detail = dialog.querySelector("[data-collect-detail]");
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  const TILTS = [-1.6, 1.2, -0.7, 1.7, -1.2, 0.8, -1.8, 1.4, -0.9];

  const el = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const plural = (n) => `${n} ${n === 1 ? "part" : "parts"}`;

  // Icon --------------------------------------------------------------------------------------------
  const setCount = (snap) => {
    const n = snap?.parts.length ?? 0;
    badge.hidden = n === 0;
    badge.textContent = String(n);
    button.setAttribute("aria-label", n ? `Collected context: ${plural(n)}. Open` : "Collected context: nothing yet");
  };

  // Collection ---------------------------------------------------------------------------------------
  // One capture plays as one short sequence (about 2.6 s). The badge and the overlay change only when the
  // last card is in. A capture that arrives while one is playing waits its turn.
  let delivered = null; // the snapshot the icon holds: what the badge and the overlay show
  let queue = [];
  let playing = false;
  let timers = [];
  const later = (ms, step) => timers.push(setTimeout(step, ms));
  const stop = () => {
    timers.forEach(clearTimeout);
    timers = [];
    flyLayer.replaceChildren();
    playing = false;
  };

  // What a card shows: a short label (file, source, or branch) and up to three lines as captured.
  const cardContent = (part, snap) => {
    const lines = part.text.split("\n").filter((line) => line.trim());
    if (part.id === "editor") return { label: snap.file.split("/").pop(), lines: sim.previewLines(part, 6) };
    if (part.id === "terminal") return { label: "Terminal", lines: lines.filter((line) => !line.startsWith(sim.PROMPT)).slice(-6) };
    if (part.id === "files") return { label: "src", lines: lines.slice(1, 7).map((name) => name.trim()) };
    if (part.id === "foreground") return { label: "Active window", lines: lines.map((line) => line.replace(/^\w+: /, "")) };
    if (part.id === "git") return { label: "Git · main", lines: lines.map((line) => line.trim()) };
    return { label: part.category, lines: sim.previewLines(part, 6) };
  };
  // A few representative parts: the windows' text first, then Git, then the active window.
  const pick = (snap) => [
    ...snap.parts.filter((part) => part.group === "active" || part.group === "other"),
    ...snap.parts.filter((part) => part.group === "system"),
    ...snap.parts.filter((part) => part.group === "head"),
  ];
  const flyCard = (part, snap, width) => {
    const { label, lines } = cardContent(part, snap);
    const card = el("div", "fly-card");
    card.style.width = `${width}px`;
    const head = el("p", "fly-label");
    head.append(sim.appIcon(part.source.icon), el("span", "", label));
    card.append(head, el("pre", "fly-lines", lines.join("\n")));
    return card;
  };

  // A card going into the icon: it squashes a little, then springs up. The last one bumps harder.
  const bump = (big = false) => {
    if (reduce.matches) return;
    button.animate([
      { transform: "translateY(0) scale(1)" },
      { transform: `translateY(2px) scale(${big ? 0.88 : 0.92})`, offset: 0.25 },
      { transform: `translateY(-3px) scale(${big ? 1.16 : 1.09})`, offset: 0.6 },
      { transform: "translateY(0) scale(1)" },
    ], { duration: big ? 480 : 320, easing: "ease-out" });
  };

  // The timeline, in ms from the moment the cards appear.
  const T = { appear: 280, inStep: 60, read: 700, gather: 1050, gatherStep: 50, hold: 140, travel: 820, travelStep: 90 };
  const SOFT = "cubic-bezier(0.2, 0.8, 0.2, 1)";
  const SWAY = "cubic-bezier(0.45, 0, 0.55, 1)";
  const PATH = "cubic-bezier(0.35, 0, 0.45, 1)";

  const play = (snap, from) => {
    playing = true;
    const box = from?.getBoundingClientRect?.();
    if (reduce.matches || !box || !button.offsetWidth) {
      arrive(snap);
      return;
    }
    const vw = document.documentElement.clientWidth;
    const vh = window.innerHeight;
    const narrow = vw < 600;
    const parts = pick(snap).slice(0, narrow ? 3 : 4);
    const n = parts.length;
    const c = (n - 1) / 2;
    // Large cards (about 340px); side by side when they fit, otherwise overlapping in the row.
    const w = Math.round(Math.min(340, vw * (narrow ? 0.62 : 0.36)));
    const step = n > 1 ? Math.min(w + 16, (vw - 48 - w) / (n - 1)) : 0;
    const cards = parts.map((part) => {
      const card = flyCard(part, snap, w);
      flyLayer.append(card);
      return card;
    });
    const h = Math.max(...cards.map((card) => card.offsetHeight));
    cards.forEach((card) => { card.style.height = `${h}px`; });

    // The row sits in the middle of the screen, where it can be read.
    const row = w + (n - 1) * step;
    const X = vw / 2;
    const Y = vh / 2;
    const icon = button.getBoundingClientRect();
    const tuck = { x: icon.left + icon.width / 2, y: icon.top + icon.height / 2 + 4 }; // into the board, under the clip
    const above = { x: tuck.x, y: icon.top - 6 }; // just above the clip

    const pose = (x, y, rotate, scale) => `translate(${Math.round(x)}px, ${Math.round(y)}px) rotate(${rotate}deg) scale(${scale})`;
    let finish = 0;
    cards.forEach((card, i) => {
      const left = X - row / 2 + i * step;
      const center = { x: left + w / 2, y: Y };
      card.style.left = `${Math.round(left)}px`;
      card.style.top = `${Math.round(Y - h / 2)}px`;
      card.style.zIndex = String(i + 1);
      const to = (point) => ({ x: point.x - center.x, y: point.y - center.y });
      const stack = to({ x: X + (i - c) * 3, y: Y - 6 - i * 3 });
      const end = to(above);
      const tucked = to(tuck);
      // The curve to the icon: up and over, a little different for each card.
      const bend = { x: (stack.x + end.x) / 2 + (i - c) * 14, y: Math.max(Math.min(stack.y, end.y) - 110 - i * 8, 24 - center.y) };
      const t0 = i * T.inStep;
      const gathered = T.gather + i * T.gatherStep;
      const leave = gathered + T.hold + (n - 1 - i) * T.travelStep; // the top of the stack goes first
      const done = leave + T.travel;
      finish = Math.max(finish, done);
      const at = (t) => (t - t0) / (leave - t0);
      const stacked = pose(stack.x, stack.y, (i - c) * 2, 0.94);
      // In place: appear in the row, stay a moment to be read, then stack on each other.
      card.animate([
        { offset: 0, transform: pose(0, 12, 0, 0.92), opacity: 0, easing: SOFT },
        { offset: at(t0 + T.appear), transform: pose(0, 0, 0, 1), opacity: 1 },
        { offset: at(T.read), transform: pose(0, 0, 0, 1), opacity: 1, easing: SWAY },
        { offset: at(gathered), transform: stacked, opacity: 1 },
        { offset: 1, transform: stacked, opacity: 1 },
      ], { duration: leave - t0, delay: t0, fill: "forwards" });
      // To the icon: one easing over the whole trip (no stop halfway), along the curve, then down under
      // the clip. It starts exactly where the stack left the card.
      const curve = [0, 0.2, 0.4, 0.6, 0.8, 1].map((t) => {
        const u = 1 - t;
        return {
          offset: t * 0.93,
          transform: pose(u * u * stack.x + 2 * u * t * bend.x + t * t * end.x, u * u * stack.y + 2 * u * t * bend.y + t * t * end.y, (i - c) * 2 * u, 0.94 - 0.74 * t),
          opacity: 1,
        };
      });
      card.animate([...curve, { offset: 1, transform: pose(tucked.x, tucked.y, 0, 0.14), opacity: 0 }],
        { duration: T.travel, delay: leave, easing: PATH, fill: "forwards" });
      // Each card that slides in bumps the icon; card 0, the bottom of the stack, lands last and
      // arrive() gives it the bigger bump.
      later(done - 60, () => { if (i > 0) bump(); });
      later(done, () => card.remove());
    });
    later(finish + 20, () => arrive(snap));
  };

  // Arrival: the snapshot is now the collector's; the badge updates and the icon bounces. Then the next
  // waiting capture, if any.
  function arrive(snap) {
    timers.forEach(clearTimeout);
    timers = [];
    flyLayer.replaceChildren();
    delivered = snap;
    setCount(snap);
    render();
    bump(true);
    playing = false;
    later(reduce.matches ? 0 : 320, next);
  }

  function next() {
    if (playing) return;
    const item = queue.shift();
    if (item) play(item.snap, item.from);
  }

  const collect = (snap, from) => {
    if (!snap) {
      queue = [];
      stop();
      delivered = null;
      setCount(null);
      render();
      return;
    }
    queue.push({ snap, from });
    if (queue.length > 2) queue.splice(0, queue.length - 2); // keep the latest two waiting
    next();
  };
  sim.collector.onCollect(collect);

  // Overlay -------------------------------------------------------------------------------------------
  let openId = null; // the part shown in the detail view
  const render = () => {
    const snap = delivered;
    empty.hidden = Boolean(snap);
    meta.textContent = snap ? `${plural(snap.parts.length)} · ${snap.markdown.length.toLocaleString("en-US")} characters · the same snapshot as Snapctx.md` : "";
    list.replaceChildren(...(snap?.parts ?? []).map((part, i) => {
      const item = el("li");
      const card = el("button", "collect-card");
      card.type = "button";
      card.dataset.id = part.id;
      card.style.setProperty("--i", String(i));
      card.style.setProperty("--r", `${TILTS[i % TILTS.length]}deg`);
      const cat = el("span", "ccard-cat");
      cat.append(sim.appIcon(part.source.icon), el("span", "", part.category), el("span", "ccard-app", `· ${part.source.name}`));
      card.append(cat, el("span", "ccard-title", part.title), el("pre", "ccard-preview", sim.previewLines(part, 6).join("\n")));
      card.addEventListener("click", () => showDetail(part.id));
      item.append(card);
      return item;
    }));
    const part = snap?.parts.find((item) => item.id === openId);
    if (openId && !part) showList();
    else if (part) fillDetail(part);
    list.hidden = !snap || Boolean(openId);
  };

  const fillDetail = (part) => {
    detail.querySelector("[data-detail-src]").replaceChildren(sim.appIcon(part.source.icon), el("span", "", `${part.source.name} · ${part.category}`));
    detail.querySelector("[data-detail-title]").textContent = part.title;
    detail.querySelector("[data-detail-desc]").textContent = part.description;
    sim.renderMarkdown(detail.querySelector("[data-detail-text]"), part.lines.join("\n"));
  };
  function showDetail(id) {
    const part = delivered?.parts.find((item) => item.id === id);
    if (!part) return;
    openId = id;
    fillDetail(part);
    list.hidden = true;
    detail.hidden = false;
    detail.querySelector("[data-collect-back]").focus();
  }
  function showList() {
    const id = openId;
    openId = null;
    detail.hidden = true;
    list.hidden = !delivered;
    if (id) list.querySelector(`[data-id="${id}"]`)?.focus();
  }
  detail.querySelector("[data-collect-back]").addEventListener("click", showList);

  // Opening: copies of the cards fly out of the icon to their places in the overlay (the list itself
  // scrolls and would clip them), then the real cards take over.
  const outLayer = dialog.querySelector(".collect-out");
  let outTimer = 0;
  const endFlyOut = () => {
    clearTimeout(outTimer);
    outLayer.replaceChildren();
    list.classList.remove("is-arriving");
  };
  const flyOut = () => {
    const cards = [...list.querySelectorAll(".collect-card")];
    if (reduce.matches || !cards.length) return;
    const icon = button.getBoundingClientRect();
    const from = { x: icon.left + icon.width / 2, y: icon.top + icon.height / 2 };
    list.classList.add("is-arriving");
    let last = 0;
    cards.forEach((card, i) => {
      const box = card.getBoundingClientRect();
      const copy = card.cloneNode(true);
      copy.removeAttribute("data-id");
      copy.tabIndex = -1;
      copy.style.width = `${card.offsetWidth}px`;
      copy.style.left = `${Math.round(box.left + box.width / 2 - card.offsetWidth / 2)}px`;
      copy.style.top = `${Math.round(box.top + box.height / 2 - card.offsetHeight / 2)}px`;
      outLayer.append(copy);
      const tilt = TILTS[i % TILTS.length];
      const dx = from.x - (box.left + box.width / 2);
      const dy = from.y - (box.top + box.height / 2);
      const delay = 40 + i * 55;
      last = Math.max(last, delay + 560);
      copy.animate([
        { transform: `translate(${Math.round(dx)}px, ${Math.round(dy)}px) rotate(0deg) scale(0.14)`, opacity: 0 },
        { transform: `translate(${Math.round(dx * 0.75)}px, ${Math.round(dy * 0.75)}px) rotate(0deg) scale(0.3)`, opacity: 1, offset: 0.2 },
        { transform: `translate(0px, 0px) rotate(${tilt}deg) scale(1)`, opacity: 1 },
      ], { duration: 560, delay, easing: "cubic-bezier(0.22, 1.2, 0.36, 1)", fill: "both" });
    });
    outTimer = setTimeout(endFlyOut, last + 20);
  };

  button.addEventListener("click", () => {
    openId = null;
    detail.hidden = true;
    render();
    dialog.showModal(); // modal: the page behind is inert, Escape closes
    (list.querySelector(".collect-card") ?? dialog.querySelector("[data-collect-close]")).focus();
    flyOut();
  });
  // Closing plays the opening in reverse: the cards shrink back into the icon (the top card first) while
  // the blur fades, then the overlay closes. Escape, the close button, and a click on the page all do this.
  let closing = 0;
  const requestClose = () => {
    if (closing || !dialog.open) return;
    endFlyOut();
    if (!detail.hidden) {
      openId = null;
      detail.hidden = true;
      list.hidden = !delivered;
    }
    const cards = list.hidden ? [] : [...list.querySelectorAll(".collect-card")];
    if (reduce.matches || !cards.length) {
      dialog.close();
      return;
    }
    const icon = button.getBoundingClientRect();
    const to = { x: icon.left + icon.width / 2, y: icon.top + icon.height / 2 };
    let last = 0;
    cards.forEach((card, i) => {
      const box = card.getBoundingClientRect();
      const copy = card.cloneNode(true);
      copy.removeAttribute("data-id");
      copy.tabIndex = -1;
      copy.style.width = `${card.offsetWidth}px`;
      copy.style.left = `${Math.round(box.left + box.width / 2 - card.offsetWidth / 2)}px`;
      copy.style.top = `${Math.round(box.top + box.height / 2 - card.offsetHeight / 2)}px`;
      outLayer.append(copy);
      const tilt = TILTS[i % TILTS.length];
      const dx = to.x - (box.left + box.width / 2);
      const dy = to.y - (box.top + box.height / 2);
      const delay = (cards.length - 1 - i) * 55;
      last = Math.max(last, delay + 480);
      setTimeout(() => bump(i === 0), delay + 420);
      copy.animate([
        { transform: `translate(0px, 0px) rotate(${tilt}deg) scale(1)`, opacity: 1 },
        { transform: `translate(${Math.round(dx * 0.75)}px, ${Math.round(dy * 0.75)}px) rotate(0deg) scale(0.3)`, opacity: 1, offset: 0.8 },
        { transform: `translate(${Math.round(dx)}px, ${Math.round(dy)}px) rotate(0deg) scale(0.14)`, opacity: 0 },
      ], { duration: 480, delay, easing: "cubic-bezier(0.55, 0, 0.75, 0.2)", fill: "both" });
    });
    list.classList.add("is-arriving");
    dialog.classList.add("is-closing");
    closing = setTimeout(() => dialog.close(), last + 20);
  };
  dialog.addEventListener("cancel", (event) => { event.preventDefault(); requestClose(); }); // Escape
  dialog.querySelector("[data-collect-close]").addEventListener("click", requestClose);
  // No panel around the cards: a click on the blurred page around them closes the overlay.
  dialog.addEventListener("click", (event) => { if (!event.target.closest(".collect-card, .collect-detail, button")) requestClose(); });
  dialog.addEventListener("close", () => {
    clearTimeout(closing);
    closing = 0;
    dialog.classList.remove("is-closing");
    endFlyOut();
    openId = null;
    button.focus();
  });

  setCount(null);
  render();
})();
