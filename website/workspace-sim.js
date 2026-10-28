// Sample desktops and the snapshots they produce. Each demo owns its own desktop: the keyboard-and-chat
// stage at the top of the page (keyboard-demo.js) uses `home`, whose captures go to the page's context
// collector (collector.js); the sample Windows desktop (os-demo.js) uses `desktop`, whose context never
// leaves it. Only the helpers and the mock scenario are shared. Everything here is simulated in the page: no real files, terminal, clipboard, or system information are read,
// and nothing is sent anywhere. The snapshot uses the real app's Markdown format, but it only ever
// contains text that is visible in the sample windows, because that is all the app can read (it never
// opens files).
(() => {
  const chat = document.getElementById("chat");
  if (!chat) return;

  // Sample project ------------------------------------------------------------------------------------
  const FILES = {
    "src/api.ts": [
      'import { Router } from "express";',
      'import { getUser, listInvoices } from "./db";',
      "",
      "export const router = Router();",
      "",
      'router.get("/api/user/:id", async (req, res) => {',
      "  const user = await getUser(req.params.id);",
      '  if (!user) return res.status(404).json({ error: "Not found" });',
      "  res.json(user);",
      "});",
      "",
      'router.get("/api/invoices", async (_req, res) => {',
      "  res.json(await listInvoices());",
      "});",
    ].join("\n"),
    "src/client.ts": [
      "export async function loadUser(id: number) {",
      "  const response = await fetch(`/api/users/${id}`);",
      "  if (!response.ok) throw new Error(`Request failed: ${response.status}`);",
      "  return response.json();",
      "}",
    ].join("\n"),
    "src/server.ts": [
      'import express from "express";',
      'import { router } from "./api";',
      "",
      "const app = express();",
      "app.use(express.json());",
      "app.use(router);",
      'app.listen(3000, () => console.log("Server listening on http://localhost:3000"));',
    ].join("\n"),
    "src/db.ts": [
      'import { Pool } from "pg";',
      "",
      'const pool = new Pool({ connectionString: "postgres://app:s3cret-pass@localhost/billing" });',
      "",
      "export const getUser = (id: string) =>",
      '  pool.query("select * from users where id = $1", [id]).then((r) => r.rows[0]);',
      'export const listInvoices = () => pool.query("select * from invoices").then((r) => r.rows);',
    ].join("\n"),
    "package.json": [
      "{",
      '  "name": "billing-api",',
      '  "version": "1.4.2",',
      '  "scripts": { "dev": "tsx watch src/server.ts" },',
      '  "dependencies": { "express": "^4.21.0", "pg": "^8.13.0" }',
      "}",
    ].join("\n"),
  };
  const PROMPT = "PS C:\\source\\billing-api>";
  const CURL = "curl.exe -i localhost:3000/api/users/42";
  const START_TERMINAL = [
    `${PROMPT} npm run dev`,
    "> billing-api@1.4.2 dev",
    "> tsx watch src/server.ts",
    "Server listening on http://localhost:3000",
    `${PROMPT} ${CURL}`,
    "HTTP/1.1 404 Not Found",
    "Content-Type: text/html; charset=utf-8",
    "",
    "Cannot GET /api/users/42",
  ];
  const LEVELS = {
    1: { name: "Active window", note: "Only the window you are looking at." },
    2: { name: "Working context", note: "The active window plus your editor, terminal, and Git status." },
    3: { name: "Workspace", note: "Every visible window, the window list, and system details." },
  };
  // A few representative lines of a part, for a card or a sheet: the end of the terminal, the routes
  // in code, the start of anything else.
  const previewLines = (part, max = 4) => {
    const lines = part.text.split("\n").filter((line) => line.trim());
    if (part.id === "terminal") return lines.slice(-max);
    if (part.id === "editor") {
      const key = lines.filter((line) => /router\.|fetch\(|export |connectionString|"name"/.test(line));
      return (key.length ? key : lines).slice(0, max);
    }
    return lines.slice(0, max);
  };

  // One sample desktop: its own windows, files, terminal, and context level, and the snapshots taken
  // from it. Two instances never share state.
  const createDesktop = () => {
    // The mock apps of the sample desktop. `app` and `icon` (a symbol in the page's SVG sprite) identify
    // where each part of a snapshot came from, on the cards, the sheets, and in the detail view.
    const APPS = {
      editor: { process: "Code", name: "Code", app: "Visual Studio Code", icon: "code", title: () => `${state.file.split("/").pop()} - billing-api - Visual Studio Code` },
      terminal: { process: "WindowsTerminal", name: "Terminal", app: "Windows Terminal", icon: "term", title: () => "Windows PowerShell" },
      files: { process: "explorer", name: "File Explorer", app: "File Explorer", icon: "folder", title: () => "src - File Explorer" },
      system: { process: "msinfo32", name: "System Information", app: "System Information", icon: "info", title: () => "System Information" },
      assistant: { process: "msedge", name: "Assistant", app: "Microsoft Edge", icon: "chat", title: () => "Assistant - Sample chat - Microsoft Edge" },
    };
    const sourceOf = (app) => ({ name: APPS[app].app, icon: APPS[app].icon });

    const initialState = () => ({
      files: { ...FILES },
      file: "src/api.ts",
      terminal: [...START_TERMINAL],
      open: new Set(["editor", "terminal", "files", "assistant"]),
      min: new Set(), // minimized: open, but nothing on screen to read
      order: ["system", "files", "assistant", "editor", "terminal"], // back to front; the last visible one is active
      level: 2,
    });
    let state = initialState();
    const visible = (app) => state.open.has(app) && !state.min.has(app);
    const active = () => [...state.order].reverse().find(visible) ?? null;
    const focusApp = (app) => {
      state.open.add(app);
      state.min.delete(app);
      state.order = [...state.order.filter((name) => name !== app), app];
    };

    // Simulated server: a request succeeds only if the current src/api.ts defines a matching route.
    const routesIn = (text) => [...text.matchAll(/router\.(get|post|put|delete)\(\s*(["'`])([^"'`]+)\2/g)].map((m) => m[3]);
    const matches = (route, path) => new RegExp(`^${route.replace(/:[^/]+/g, "[^/]+")}$`).test(path);
    const COMMANDS = {
      "npm run dev": () => ["> billing-api@1.4.2 dev", "> tsx watch src/server.ts", "Server listening on http://localhost:3000"],
      [CURL]: () => {
        const ok = routesIn(state.files["src/api.ts"]).some((route) => matches(route, "/api/users/42"));
        return ok
          ? ["HTTP/1.1 200 OK", "Content-Type: application/json; charset=utf-8", "", '{"id":42,"name":"Dana Ruiz","email":"[email]"}']
          : ["HTTP/1.1 404 Not Found", "Content-Type: text/html; charset=utf-8", "", "Cannot GET /api/users/42"];
      },
      "git status": () => ["On branch main", "Your branch is ahead of 'origin/main' by 1 commit.", "Changes not staged for commit:", "  modified:   src/api.ts", "  modified:   src/client.ts"],
      clear: null,
    };
    const runCommand = (command) => {
      const run = COMMANDS[command];
      state.terminal = run === null ? [] : [...state.terminal, `${PROMPT} ${command}`, ...run()].slice(-40);
    };

    // Every change to this desktop goes through update(), which tells this desktop's own listeners.
    const listeners = new Set();
    const update = (mutate) => {
      mutate(state);
      for (const listener of listeners) listener();
    };

    // Snapshot generation -------------------------------------------------------------------------------
    // Redaction mirrors the app's rules for the cases in this sample: credentials in URLs and key=value
    // secrets. The app has many more rules; this is only what the sample needs.
    const redact = (text) => text
      .replace(/(\w+:\/\/[^:\s/]+:)[^@\s"]+@/g, "$1[REDACTED]@")
      .replace(/((?:api[_-]?key|token|secret|password)\s*[=:]\s*)["']?[^\s"',]+/gi, "$1[REDACTED]");
    const visibleText = (app) => {
      if (app === "editor") return state.files[state.file];
      if (app === "terminal") return state.terminal.slice(-12).join("\n");
      if (app === "files") return ["src", ...Object.keys(FILES).filter((path) => path.startsWith("src/")).map((path) => `  ${path.slice(4)}`), "package.json"].join("\n");
      if (app === "assistant") return "Assistant\nSample chat in the website demo";
      return ["OS: Windows 11 Pro", `Active app: ${active() ? APPS[active()].process : "None"}`].join("\n");
    };
    const pad = (n) => String(n).padStart(2, "0");
    const stamp = () => {
      const now = new Date();
      const offset = -now.getTimezoneOffset();
      const sign = offset >= 0 ? "+" : "-";
      return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())} (UTC${sign}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)})`;
    };

    const SOURCE_LABELS = { editor: "Code editor text", terminal: "Terminal output", files: "File manager text", system: "System window text", assistant: "Browser text" };

    // A snapshot is built from parts. The Markdown, the sheets and clipboard in the desktop demo, and the
    // cards in the page's context collector all come from the same parts, each with its source app.
    const snapshot = () => {
      const level = state.level;
      const current = active();
      const parts = [];
      const windowPart = (app, group) => {
        const text = redact(visibleText(app));
        const name = `${APPS[app].process} · ${APPS[app].title()}`;
        parts.push({
          id: app, group, category: SOURCE_LABELS[app], title: APPS[app].title(), text, source: sourceOf(app),
          description: group === "active" ? "Visible text of the window you were using, read from the screen." : "Visible text of another open window, read from the screen.",
          lines: [group === "active" ? `### Active window: ${name}` : `### ${name}`, "", `_Source: UI Automation · ${text.length.toLocaleString("en-US")} characters_`, "", "```text", text, "```"],
        });
      };
      if (current) {
        parts.push({
          id: "foreground", group: "head", category: "Active window", title: APPS[current].title(), source: sourceOf(current),
          description: "Which app and window you were using when you pressed the shortcut.",
          text: `Process: ${APPS[current].process}\nWindow: ${APPS[current].title()}`,
          lines: [`**Foreground window:** ${APPS[current].process} · ${APPS[current].title()}`],
        });
        windowPart(current, "active");
      }
      const others = (level === 1 ? [] : level === 2 ? ["editor", "terminal"] : ["editor", "terminal", "files", "system", "assistant"])
        .filter((app) => app !== current && visible(app));
      for (const app of others) windowPart(app, "other");
      if (level >= 2) {
        parts.push({
          id: "git", group: "system", category: "Git status", title: "billing-api · main", source: { name: "Git", icon: "git" },
          description: "Branch and changed file names of the repository open in the editor. No file contents or diffs.",
          text: ["Branch: main → origin/main (ahead 1)", "Working tree: 2 unstaged", " M src/api.ts", " M src/client.ts"].join("\n"),
          lines: ["### Git repositories (1)", "", "#### billing-api · `~\\source\\billing-api`", "", "- Branch: `main` → `origin/main` (ahead 1)", "- Working tree: 2 unstaged", "- Changed files:", "  - ` M` `src/api.ts`", "  - ` M` `src/client.ts`"],
        });
      }
      if (level >= 3) {
        const open = [...state.order].reverse().filter((app) => state.open.has(app));
        const note = (app) => (app === current ? " (foreground)" : state.min.has(app) ? " (minimized)" : "");
        parts.push({
          id: "windows", group: "system", category: "Window list", title: `${open.length} open windows`, source: { name: "Windows", icon: "start" },
          description: "Titles and process names of the open windows, most recently used first.",
          text: open.map((app) => `${APPS[app].process}: ${APPS[app].title()}${note(app)}`).join("\n"),
          lines: [`### Open windows (${open.length}, most recently used first)`, "", ...open.map((app) => `- **${APPS[app].process}**: ${APPS[app].title()}${app === current ? " _(foreground)_" : state.min.has(app) ? " _(minimized)_" : ""}`)],
        });
        parts.push({
          id: "system-summary", group: "system", category: "System summary", title: "Windows 11 Pro", source: { name: "System Information", icon: "info" },
          description: "Operating system and hardware summary.",
          text: "Windows 11 Pro · X64\n12 logical processors\n31.7 GB memory",
          lines: ["### System", "", "- Windows 11 Pro · X64 · 12 logical processors · 31.7 GB memory"],
        });
      }

      const group = (name) => parts.filter((part) => part.group === name);
      const lines = ["# Context Snapshot", "", `_Captured ${stamp()} by Snapctx. Sample data from the website demo._`];
      for (const part of group("head")) lines.push("", ...part.lines);
      lines.push("", "## Visible text");
      if (group("active").length) for (const part of group("active")) lines.push("", ...part.lines);
      else lines.push("", current ? "_The active window's text was left out of this snapshot._" : "_No visible text could be extracted; no window is on screen._");
      if (group("other").length) {
        lines.push("", "## Other visible windows");
        for (const part of group("other")) lines.push("", ...part.lines);
      }
      if (group("system").length) {
        lines.push("", "## System context");
        for (const part of group("system")) lines.push("", ...part.lines);
      }

      const textOf = Object.fromEntries(parts.filter((part) => part.group === "active" || part.group === "other").map((part) => [part.id, part.text]));
      const sources = []; // { id, label, status: "included" | "excluded" | "closed" | "never" }
      for (const app of ["editor", "terminal", "files", "system", "assistant"]) {
        if (app === "assistant" && !parts.some((part) => part.id === app)) continue;
        const status = !visible(app) ? "closed" : app in textOf ? "included" : "excluded";
        sources.push({ id: app, label: SOURCE_LABELS[app] + (app === current ? " (active)" : ""), status });
      }
      sources.push({ id: "git", label: "Git status", status: level >= 2 ? "included" : "excluded" });
      sources.push({ id: "windows", label: "Window list", status: level >= 3 ? "included" : "excluded" });
      sources.push({ id: "system-summary", label: "System summary", status: level >= 3 ? "included" : "excluded" });
      sources.push({ id: "unopened", label: "Files not on screen", status: "never" });

      const markdown = lines.join("\n") + "\n";
      return {
        markdown, sources, level, textOf, parts,
        active: current,
        file: state.file,
        count: sources.filter((source) => source.status === "included").length,
        reply: answer(textOf, level),
        hasGit: parts.some((part) => part.id === "git"),
        redactions: markdown.split("[REDACTED]").length - 1,
      };
    };

    // The sample assistant: deterministic, and it only uses evidence that is in the snapshot -----------
    const answer = (textOf, level) => {
      const terminal = textOf.terminal ?? "";
      const editorFile = "editor" in textOf ? state.file : null;
      // The most recent response in the terminal decides: an old 404 above a newer 200 is history.
      const latest = [...terminal.matchAll(/HTTP\/1\.1 (\d{3})/g)].pop()?.[1];
      const succeeded = latest?.startsWith("2") ?? false;
      const failed = succeeded ? null : /404 Not Found[\s\S]*Cannot GET (\S+)/.exec(terminal);
      const routes = editorFile === "src/api.ts" ? routesIn(textOf.editor) : [];
      const clientCall = editorFile === "src/client.ts" ? /fetch\(`([^`]+)`/.exec(textOf.editor) : null;
      const listing = "files" in textOf;
      const noise = level === 3 ? " The window list and system details were not needed for this one." : "";

      if (failed && routes.length) {
        const path = failed[1];
        const hit = routes.find((route) => matches(route, path));
        if (hit) {
          return `src/api.ts now defines \`${hit}\`, which matches \`${path}\`. The 404 in the terminal is from an earlier request: run the curl command again to confirm.${noise}`;
        }
        const close = routes.find((route) => route.includes("user")) ?? routes[0];
        return `The terminal shows \`GET ${path}\` returning 404, and src/api.ts has no route for it. The closest one is \`${close}\` (singular \`user\`). Rename it to \`/api/users/:id\`, or request \`/api/user/42\`.${noise}`;
      }
      if (failed) {
        const parts = [`The terminal shows \`GET ${failed[1]}\` returning 404, but the snapshot does not include your route definitions, so I cannot tell which route is missing.`];
        if (clientCall) parts.push(`src/client.ts requests \`${clientCall[1]}\`, so the server needs a matching route.`);
        if (listing) parts.push("The file manager lists src/api.ts, which probably defines the routes: open it and capture again.");
        else parts.push("Open the file that defines your routes and capture again.");
        return parts.join(" ") + noise;
      }
      if (succeeded) return `The latest request in the terminal returned 200 OK, so the route works now.${noise}`;
      if (routes.length) {
        return `I can see the routes in src/api.ts (${routes.map((route) => `\`${route}\``).join(", ")}), but not the request that failed. Which URL returned 404? Include the terminal output and capture again.${noise}`;
      }
      return `This snapshot shows neither the failing request nor your routes, so I cannot say why it returns 404. Capture again with the terminal output and src/api.ts visible.${noise}`;
    };

    return {
      get state() { return state; },
      update,
      subscribe(listener) { listeners.add(listener); },
      reset() { update(() => { state = { ...initialState(), level: state.level }; }); },
      visible, active, focusApp, runCommand, snapshot, APPS,
    };
  };

  const QUESTIONS = [
    { id: "404", text: "Why is this API request returning a 404?" },
    { id: "changes", text: "Which files did I change?" },
    { id: "secrets", text: "Is anything sensitive in here?" },
  ];
  const answerFor = (id, snap) => {
    if (!snap) return "I can't see your screen yet. Press `Ctrl` `Alt` `Shift` `C`, and I'll answer from the snapshot.";
    if (id === "404") return snap.reply;
    if (id === "changes") {
      return snap.hasGit
        ? "Git shows 2 unstaged changes on `main`: `src/api.ts` and `src/client.ts`. The branch is 1 commit ahead of `origin/main`."
        : "This snapshot has no Git status, so I can't tell. Capture again with more context.";
    }
    if (snap.redactions) {
      return `Snapctx already redacted ${snap.redactions === 1 ? "one value" : `${snap.redactions} values`} before copying: the password in the database URL in \`src/db.ts\` shows as \`[REDACTED]\`. File names and window titles are still visible.`;
    }
    return "I don't see credentials. Snapctx redacts keys, tokens, passwords, and email addresses before copying; file names and window titles stay visible.";
  };
  // Which parts of the snapshot an answer relied on, shown under the answer.
  const usedFor = (id, snap) => {
    if (!snap) return [];
    if (id === "changes") return snap.hasGit ? ["Git status"] : [];
    if (id === "secrets") return snap.redactions ? ["Redacted code editor text"] : [];
    const used = [];
    if (/Cannot GET|200 OK/.test(snap.textOf.terminal ?? "")) used.push("Terminal output");
    if ("editor" in snap.textOf) used.push(snap.file);
    if (!used.length && "files" in snap.textOf) used.push("File manager text");
    return used;
  };

  // Text rendering (text only, never HTML from the editor) -------------------------------------------
  const renderMarkdown = (pre, markdown) => {
    let fenced = false;
    pre.replaceChildren(...markdown.trimEnd().split("\n").map((line) => {
      const span = document.createElement("span");
      span.className = "ln";
      const fence = line.startsWith("```");
      if (fence) fenced = !fenced;
      if (fence || (!fenced && /^_.*_$/.test(line))) span.classList.add("is-muted");
      else if (!fenced && /^#{1,4} /.test(line)) span.classList.add("is-heading");
      else if (!fenced && line.startsWith("**")) span.classList.add("is-strong");
      const redacted = line.split("[REDACTED]");
      redacted.forEach((part, index) => {
        if (index) {
          const mark = document.createElement("mark");
          mark.textContent = "[REDACTED]";
          span.append(mark);
        }
        span.append(part);
      });
      if (!line) span.textContent = " ";
      return span;
    }));
  };
  // Inline code in replies is built as <code> elements from text, never parsed as HTML.
  const fillText = (element, text) => {
    element.replaceChildren(...text.split("`").map((part, index) => {
      if (index % 2 === 0) return document.createTextNode(part);
      const code = document.createElement("code");
      code.textContent = part;
      return code;
    }));
  };

  const home = createDesktop(); // behind the keyboard demo at the top of the page
  const desktop = createDesktop(); // behind the sample Windows desktop

  // The page's context collector. Only the keyboard demo at the top of the page is connected to it;
  // the Windows demo has no way to reach it.
  const collector = (() => {
    let collected = null;
    const listeners = new Set();
    return {
      collect(snap, from = null) {
        collected = snap;
        for (const listener of listeners) listener(snap, from);
      },
      get collected() { return collected; },
      onCollect(listener) { listeners.add(listener); },
    };
  })();

  // The chat at the top of the page ---------------------------------------------------------------------
  // It behaves like a real AI chat: the snapshot is pasted into the composer as an attachment, the
  // question is typed and sent, the assistant thinks for a moment and its answer streams in. Everything
  // is sample content in the page; each new capture starts the exchange over.
  const log = chat.querySelector("[data-log]");
  const slot = chat.querySelector("[data-slot]"); // the shortcut hint before a capture
  const thread = chat.querySelector("[data-thread]"); // the messages the demo adds
  const box = chat.querySelector("[data-composer-box]");
  const attach = chat.querySelector("[data-attach]");
  const attachMeta = chat.querySelector("[data-attach-meta]");
  const composer = chat.querySelector("[data-composer]");
  const suggest = chat.querySelector("[data-suggest]");
  const PLACEHOLDER = composer.textContent;
  const motionless = window.matchMedia("(prefers-reduced-motion: reduce)");

  let timers = [];
  let lastSnap = null;
  let busy = false;
  let attachments = 0;
  const later = (ms, fn) => timers.push(window.setTimeout(fn, ms));
  const cancel = () => {
    timers.forEach(clearTimeout);
    timers = [];
  };
  const toBottom = () => { log.scrollTop = log.scrollHeight; };
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const metaOf = (snap) => `${snap.count} ${snap.count === 1 ? "source" : "sources"} · ${LEVELS[snap.level].name}`;

  const setComposer = (text) => {
    composer.textContent = text || PLACEHOLDER;
    composer.classList.toggle("is-typed", Boolean(text));
    box.classList.toggle("is-ready", Boolean(text) || !attach.hidden);
  };

  // The sent snapshot: a Markdown attachment that opens to show the whole file.
  const attachment = (snap) => {
    const id = `chat-md-${++attachments}`;
    const card = node("div", "ctx");
    const head = node("button", "ctx-head ctx-toggle");
    head.type = "button";
    head.setAttribute("aria-expanded", "false");
    head.setAttribute("aria-controls", id);
    const icon = node("span", "ctx-icon", "#");
    icon.setAttribute("aria-hidden", "true");
    const chevron = node("span", "ctx-chevron");
    chevron.setAttribute("aria-hidden", "true");
    head.append(icon, node("b", "", "Snapctx.md"), node("span", "ctx-meta", metaOf(snap)), chevron);
    const pre = node("pre", "md ctx-md");
    pre.id = id;
    pre.tabIndex = 0;
    pre.setAttribute("aria-label", "Captured snapshot in Markdown");
    renderMarkdown(pre, snap.markdown);
    head.addEventListener("click", () => {
      const open = head.getAttribute("aria-expanded") !== "true";
      head.setAttribute("aria-expanded", String(open));
      pre.classList.toggle("is-open", open);
    });
    card.append(head, pre);
    return card;
  };

  const add = (message) => {
    thread.append(message);
    while (thread.children.length > 12) thread.firstElementChild.remove();
    toBottom();
  };

  // Typing into the composer from `t` (ms), a little unevenly, like a person. Returns when it ends.
  const typeAt = (t, text) => {
    if (motionless.matches) {
      later(t, () => setComposer(text));
      return t;
    }
    let at = t;
    for (let i = 1; i <= text.length; i++) {
      at += text[i - 1] === " " ? 70 : 34 + ((i * 7) % 5) * 7;
      const typed = text.slice(0, i);
      later(at, () => setComposer(typed));
    }
    return at;
  };

  // The answer from `t`, streamed word by word into a new assistant message. Returns when it ends.
  const streamAt = (t, text, dots) => {
    const message = node("div", "msg msg-ai msg-reply");
    const paragraph = node("p");
    message.append(paragraph);
    fillText(paragraph, text);
    const words = [];
    for (const child of [...paragraph.childNodes]) {
      if (child.nodeType !== Node.TEXT_NODE) {
        child.classList.add("tok");
        words.push(child);
        continue;
      }
      const pieces = child.textContent.split(/(\s+)/).filter(Boolean)
        .map((piece) => (/^\s+$/.test(piece) ? document.createTextNode(piece) : node("span", "tok", piece)));
      child.replaceWith(...pieces);
      words.push(...pieces.filter((piece) => piece.nodeType === Node.ELEMENT_NODE));
    }
    const quiet = motionless.matches;
    if (!quiet) words.forEach((word) => word.classList.add("is-pending"));
    later(t, () => {
      dots.remove();
      add(message);
    });
    if (quiet) return t;
    let at = t;
    words.forEach((word, index) => {
      at += 42 + (index % 3) * 10;
      later(at, () => {
        word.classList.remove("is-pending");
        if (index % 3 === 0) toBottom();
      });
    });
    later(at + 20, toBottom);
    return at;
  };

  const typingDots = () => {
    const dots = node("div", "msg msg-ai msg-typing");
    dots.setAttribute("aria-hidden", "true");
    dots.append(node("span"), node("span"), node("span"));
    return dots;
  };

  // One exchange. With `paste`, the snapshot goes into the composer first and is sent with the question.
  // Returns its length in ms.
  const exchange = (question, paste) => {
    cancel();
    busy = true;
    setChips(true);
    const quiet = motionless.matches;
    let t = 0;
    later(0, () => {
      box.classList.add("is-focused");
      if (paste) {
        attachMeta.textContent = metaOf(paste);
        attach.hidden = false;
        setComposer("");
      }
    });
    t = typeAt(paste && !quiet ? 500 : 120, question.text) + (quiet ? 0 : 420);
    later(t, () => {
      const message = node("div", "msg msg-user");
      if (paste) message.append(attachment(paste));
      message.append(node("p", "question", question.text));
      attach.hidden = true;
      setComposer("");
      box.classList.remove("is-focused");
      slot.hidden = true;
      add(message);
    });
    const dots = typingDots();
    later(t + 300, () => add(dots));
    t = streamAt(t + (quiet ? 300 : paste ? 1300 : 900), answerFor(question.id, lastSnap), dots);
    later(t, () => {
      busy = false;
      setChips(false);
    });
    return t;
  };

  // Suggested questions work at any time: before a capture the assistant says it cannot see anything,
  // which is the point of the product. One exchange at a time.
  const chips = QUESTIONS.map((question) => {
    const button = node("button", "chip", question.text);
    button.type = "button";
    button.addEventListener("click", () => { if (!busy) exchange(question, null); });
    return button;
  });
  suggest?.append(...chips);
  const setChips = (disabled) => chips.forEach((chip) => { chip.disabled = disabled; });
  toBottom(); // a short screen shows the latest messages and the shortcut tip

  window.contextSnapSim = {
    // The top chat: back to the start, held while a capture runs, then the snapshot is pasted and asked
    // about (returns how long that takes, in ms).
    showPreview() {
      cancel();
      busy = false;
      lastSnap = null;
      thread.replaceChildren();
      attach.hidden = true;
      setComposer("");
      box.classList.remove("is-focused");
      slot.hidden = false;
      setChips(false);
      toBottom();
    },
    startCapture() {
      cancel();
      busy = true;
      setChips(true);
    },
    deliver(snap) {
      lastSnap = snap;
      return exchange(QUESTIONS[0], snap);
    },
    // The two desktops and the collector (see the top of this file).
    home, desktop, collector,
    // A small app icon from the page's SVG sprite (#ic-code, #ic-term, ...), built as DOM, never HTML.
    appIcon(icon, className = "src-ic") {
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      svg.setAttribute("class", className);
      svg.setAttribute("aria-hidden", "true");
      const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
      use.setAttribute("href", `#ic-${icon}`);
      svg.append(use);
      return svg;
    },
    previewLines,
    FILES, PROMPT, CURL, LEVELS, QUESTIONS, answerFor, usedFor, renderMarkdown, fillText,
  };
})();
