// The research desktop behind the sample Windows desktop (os-demo.js): a browser with four tabs on one
// topic, a paper open in a PDF reader, a notes window, and an AI chat. Same shape as the desktops in
// workspace-sim.js (state, focus, snapshot), so the capture, sheets, and chat work unchanged. All sources,
// names, and figures are sample content written for this demo.
(() => {
  const sim = window.contextSnapSim;
  if (!sim) return;

  const TABS = [
    {
      title: "Solar curtailment, explained", site: "energybasics.org", path: "/solar-curtailment",
      heading: "Why grids throw away solar power",
      text: [
        "Curtailment happens when a grid cannot use or store all the solar power produced at midday, so operators switch panels off.",
        "It peaks in spring, when sunshine is strong but demand for heating and cooling is low.",
      ],
    },
    {
      title: "Grid batteries: 2025 cost update", site: "gridwatch.news", path: "/battery-costs-2025",
      heading: "Four-hour batteries keep getting cheaper",
      text: [
        "Installed four-hour lithium-ion systems averaged about $210/kWh in 2025, down from $280/kWh two years earlier.",
        "Developers now pair new solar farms with storage by default in most tenders.",
      ],
    },
    {
      title: "The duck curve", site: "en.wikipedia.org", path: "/wiki/Duck_curve",
      heading: "Duck curve",
      text: [
        "The duck curve shows the gap between electricity demand and solar output over a day: net demand dips at midday and ramps up steeply at sunset.",
        "Storage and flexible demand flatten the curve by moving midday energy into the evening.",
      ],
    },
    {
      title: "Transmission vs storage: what's cheaper?", site: "renewablesreview.com", path: "/transmission-vs-storage",
      heading: "New lines or more batteries?",
      text: [
        "New transmission moves surplus solar to other regions but takes 7 to 10 years to permit and build.",
        "Batteries can be installed in under two years, but only shift energy by a few hours.",
      ],
    },
  ];

  const PAPER = {
    file: "okafor-2025-storage-curtailment.pdf",
    title: "Storage-Led Reduction of Solar Curtailment: Evidence from Three Island Grids",
    authors: "M. Okafor, L. Brandt, S. Ito · Energy Systems Letters, 2025",
    highlights: [
      "Adding four-hour storage equal to 25% of solar capacity cut curtailment from 11.8% to 3.1%.",
      "Returns diminish above a 40% storage-to-solar ratio.",
      "The model assumes an installed battery cost of $280/kWh.",
    ],
  };

  const NOTES = [
    "Research question: can batteries replace new transmission for curtailment?",
    "",
    "- Curtailment peaks midday in spring (duck curve)",
    "- Okafor 2025: 25% storage cut curtailment 11.8% → 3.1%",
    "- GridWatch: batteries ~$210/kWh in 2025",
    "- TODO: compare with transmission build times",
  ];

  const QUESTIONS = [
    { id: "summary", text: "Summarize my research so far" },
    { id: "gaps", text: "What is still missing?" },
    { id: "sources", text: "Which sources back the 3.1% figure?" },
  ];

  const createResearchDesktop = () => {
    const listeners = new Set();
    const APPS = {
      browser: { process: "chrome", name: "Chrome", app: "Google Chrome", icon: "chrome", title: () => `${TABS[state.tab].title} - Google Chrome` },
      pdf: { process: "PdfReader", name: "PDF Reader", app: "PDF Reader", icon: "pdf", title: () => `${PAPER.file} - PDF Reader` },
      notes: { process: "Notepad", name: "Notepad", app: "Notepad", icon: "notes", title: () => "research-notes.txt - Notepad" },
      assistant: { process: "chrome", name: "Assistant", app: "Google Chrome", icon: "chat", title: () => "ChatGPT - Google Chrome" },
    };
    const SOURCE_LABELS = { browser: "Browser tabs", pdf: "PDF reader text", notes: "Notes", assistant: "AI chat" };
    const sourceOf = (app) => ({ name: APPS[app].app, icon: APPS[app].icon });

    const initialState = () => ({
      tab: 1,
      open: new Set(["browser", "pdf", "notes", "assistant"]),
      min: new Set(),
      order: ["assistant", "notes", "pdf", "browser"], // back to front; the last visible one is active
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
    const update = (mutate) => {
      mutate(state);
      for (const listener of listeners) listener();
    };

    const visibleText = (app) => {
      if (app === "browser") {
        const tab = TABS[state.tab];
        return [`${tab.site}${tab.path}`, tab.heading, ...tab.text, "", "Open tabs:", ...TABS.map((item, i) => `${i === state.tab ? "> " : "  "}${item.title} (${item.site})`)].join("\n");
      }
      if (app === "pdf") return [PAPER.title, PAPER.authors, "", "Highlighted:", ...PAPER.highlights.map((line) => `- ${line}`)].join("\n");
      if (app === "notes") return NOTES.join("\n");
      return "ChatGPT\nNew chat";
    };
    const pad = (n) => String(n).padStart(2, "0");
    const stamp = () => {
      const now = new Date();
      const offset = -now.getTimezoneOffset();
      const sign = offset >= 0 ? "+" : "-";
      return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())} (UTC${sign}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)})`;
    };

    const snapshot = () => {
      const level = state.level;
      const current = active();
      const parts = [];
      const windowPart = (app, group) => {
        const text = visibleText(app);
        const name = `${APPS[app].process} · ${APPS[app].title()}`;
        parts.push({
          id: app, group, category: SOURCE_LABELS[app], title: APPS[app].title(), text, source: sourceOf(app),
          description: group === "active" ? "Visible text of the window you were using, read from the screen." : "Visible text of another open window, read from the screen.",
          lines: [group === "active" ? `### Active window: ${name}` : `### ${name}`, "", "```text", text, "```"],
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
      if (level >= 2) for (const app of ["browser", "pdf", "notes"]) if (app !== current && visible(app)) windowPart(app, "other");
      if (level >= 3) {
        const open = [...state.order].reverse().filter((app) => state.open.has(app));
        parts.push({
          id: "windows", group: "system", category: "Window list", title: `${open.length} open windows`, source: { name: "Windows", icon: "start" },
          description: "Titles and process names of the open windows, most recently used first.",
          text: open.map((app) => `${APPS[app].process}: ${APPS[app].title()}`).join("\n"),
          lines: [`### Open windows (${open.length}, most recently used first)`, "", ...open.map((app) => `- **${APPS[app].process}**: ${APPS[app].title()}`)],
        });
      }

      const group = (name) => parts.filter((part) => part.group === name);
      const lines = ["# Context Snapshot", "", `_Captured ${stamp()} by Snapctx. Sample data from the website demo._`];
      for (const part of group("head")) lines.push("", ...part.lines);
      lines.push("", "## Visible text");
      for (const part of group("active")) lines.push("", ...part.lines);
      if (group("other").length) {
        lines.push("", "## Other visible windows");
        for (const part of group("other")) lines.push("", ...part.lines);
      }
      if (group("system").length) {
        lines.push("", "## System context");
        for (const part of group("system")) lines.push("", ...part.lines);
      }

      const textOf = Object.fromEntries(parts.filter((part) => part.group === "active" || part.group === "other").map((part) => [part.id, part.text]));
      const sources = ["browser", "pdf", "notes"].map((app) => ({
        id: app, label: SOURCE_LABELS[app] + (app === current ? " (active)" : ""), status: !visible(app) ? "closed" : app in textOf ? "included" : "excluded",
      }));
      sources.push({ id: "windows", label: "Window list", status: level >= 3 ? "included" : "excluded" });
      const markdown = lines.join("\n") + "\n";
      return {
        markdown, sources, level, textOf, parts, active: current, file: null,
        count: sources.filter((source) => source.status === "included").length,
        hasGit: false, redactions: 0,
      };
    };

    return {
      get state() { return state; },
      update,
      subscribe(listener) { listeners.add(listener); },
      reset() { update(() => { state = { ...initialState(), level: state.level }; }); },
      visible, active, focusApp, snapshot, APPS,
    };
  };

  // The sample assistant's answers, built only from what the snapshot contains, as blocks for the chat:
  // { p } paragraphs (with `code`), { list } bullets, and `used` for the "From Snapctx.md" footer.
  const answer = (id, snap) => {
    if (!snap) return { blocks: [{ p: "I can't see your research yet. Press `Ctrl` `Alt` `Shift` `C` and paste the snapshot here." }], used: [] };
    const has = (app) => app in snap.textOf;
    const used = [has("browser") && "Browser tabs", has("pdf") && "Okafor et al. 2025 (PDF)", has("notes") && "Your notes"].filter(Boolean);
    if (id === "sources") {
      const list = [];
      if (has("pdf")) list.push("Okafor et al. 2025 (PDF): storage at 25% of solar capacity cut curtailment from 11.8% to 3.1%.");
      if (has("notes")) list.push("Your notes repeat the figure and cite the same paper.");
      if (!list.length) return { blocks: [{ p: "The 3.1% figure is not in this snapshot. Capture with the paper visible." }], used };
      return { blocks: [{ p: "Only the paper itself measures it:" }, { list }, { p: "None of the browser tabs give an independent number, so it rests on one study." }], used };
    }
    if (id === "gaps") {
      const list = [];
      if (has("notes")) list.push("Your notes' open TODO: a cost comparison with transmission.");
      if (has("browser")) list.push("Transmission: your Renewables Review tab is open, but its text wasn't on screen, so I can't compare yet. Open it and capture again.");
      if (has("pdf") && has("browser")) list.push("The paper assumes $280/kWh batteries; GridWatch reports about $210/kWh in 2025, so its economics may be conservative.");
      list.push("Evidence beyond island grids: all three cases in the paper are islands.");
      return { blocks: [{ p: "What the snapshot doesn't settle yet:" }, { list }], used };
    }
    const findings = [];
    if (has("browser")) findings.push("Your tabs on solar curtailment and the duck curve frame the problem: too much solar at midday, not enough in the evening.");
    if (has("pdf")) findings.push("Okafor et al. (2025): four-hour storage equal to 25% of solar capacity cut curtailment from 11.8% to 3.1%, with diminishing returns above 40%.");
    if (has("browser")) findings.push("GridWatch: installed batteries fell to about $210/kWh in 2025, below the paper's $280/kWh assumption.");
    const blocks = [{ p: "Here's your research across the sources you had open:" }, { list: findings }];
    if (has("notes")) blocks.push({ p: "Open question from your notes: how storage compares with new transmission. You have a tab on exactly that, but its text wasn't on screen; open it and capture again so I can compare." });
    else blocks.push({ p: "Your notes weren't in this capture, so I don't know which question you're answering." });
    return { blocks, used };
  };

  sim.research = createResearchDesktop();
  sim.researchContent = { TABS, PAPER, NOTES, QUESTIONS, answer };
})();
