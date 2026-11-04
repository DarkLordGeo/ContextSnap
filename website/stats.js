// The private download dashboard. It reads GET /stats from the Convex deployment named in the page's
// <meta name="contextsnap-downloads"> tag, with the deployment's STATS_TOKEN as a bearer token. The token
// is typed in by the owner and kept in this tab's sessionStorage only; the page holds no data without it.
(() => {
  const $ = (selector) => document.querySelector(selector);
  const TOKEN_KEY = "contextsnap-stats-token";
  const form = $("[data-unlock]");
  const input = $("#token");
  const status = $("[data-status]");
  const report = $("[data-report]");
  const range = $("[data-range]");
  const number = new Intl.NumberFormat("en");
  const NS = "http://www.w3.org/2000/svg";

  const endpoint = (() => {
    try {
      const url = new URL("/stats", $('meta[name="contextsnap-downloads"]')?.content);
      const local = url.hostname === "127.0.0.1" || url.hostname === "localhost";
      return url.protocol === "https:" || local ? url.href : null;
    } catch {
      return null;
    }
  })();

  const readToken = () => { try { return sessionStorage.getItem(TOKEN_KEY); } catch { return null; } };
  const saveToken = (value) => { try { sessionStorage.setItem(TOKEN_KEY, value); } catch { /* this load only */ } };
  const forgetToken = () => { try { sessionStorage.removeItem(TOKEN_KEY); } catch { /* nothing kept */ } };
  let token = readToken();

  const say = (text, error = false) => {
    status.textContent = text;
    status.classList.toggle("is-error", error);
  };
  const el = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const svg = (tag, attributes) => {
    const element = document.createElementNS(NS, tag);
    for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, String(value));
    return element;
  };

  const load = async () => {
    if (!endpoint) {
      say("Analytics is not configured: the page has no Convex URL.", true);
      return;
    }
    say("Loading…");
    let response;
    try {
      response = await fetch(`${endpoint}?days=${range.value}`, { headers: { Authorization: `Bearer ${token}` }, cache: "no-store" });
    } catch {
      say("Could not reach the Convex deployment. Check that it is running and the functions are deployed.", true);
      return;
    }
    if (response.status === 401) {
      forgetToken();
      token = null;
      report.hidden = true;
      form.hidden = false;
      say("That token was not accepted.", true);
      return;
    }
    if (!response.ok) {
      const message = await response.json().then((body) => body.error).catch(() => null);
      say(message || `The deployment answered ${response.status}. Are the functions deployed?`, true);
      return;
    }
    const stats = await response.json();
    form.hidden = true;
    report.hidden = false; // shown first, so the chart is drawn at its real width
    render(stats);
    say(`Updated ${new Date().toLocaleTimeString()}.`);
  };

  const render = (stats) => {
    $("[data-total]").textContent = number.format(stats.totalRequests);
    $("[data-unique]").textContent = number.format(stats.uniqueRequesters);

    // Windows vs Linux: one split bar plus a labeled legend (identity is never color alone).
    const { windows, linux } = stats.byPlatform;
    const total = windows + linux;
    const split = $("[data-split]");
    split.replaceChildren();
    for (const [name, value] of [["windows", windows], ["linux", linux]]) {
      if (!value) continue;
      const part = el("span", `s-${name}`);
      part.style.flex = String(value);
      split.append(part);
    }
    const share = (value) => (total ? ` (${Math.round((value / total) * 100)}%)` : "");
    const platforms = $("[data-platforms]");
    platforms.replaceChildren();
    for (const [label, name, value] of [["Windows", "windows", windows], ["Linux", "linux", linux]]) {
      const item = el("li");
      item.append(el("span", `swatch s-${name}`), `${label} `, el("b", "", number.format(value)), share(value));
      platforms.append(item);
    }

    drawBars($("[data-chart]"), stats.daily.map((day) => ({ key: day.day, tick: day.day.slice(5), ...day })), "Download requests per day, Windows and Linux stacked");
    $("[data-daily-rows]").replaceChildren(...[...stats.daily].reverse().map((day) => tableRow([day.day, day.windows, day.linux, day.windows + day.linux])));

    drawBars($("[data-monthly-chart]"), stats.monthly.map((month) => ({ key: month.month, tick: monthTick(month.month), ...month })), "Download requests per month, Windows and Linux stacked");
    $("[data-monthly-rows]").replaceChildren(...[...stats.monthly].reverse().map((month) => tableRow([month.month, month.windows, month.linux, month.windows + month.linux])));

    const versions = $("[data-versions]");
    versions.replaceChildren(...(stats.byVersion.length
      ? stats.byVersion.map((row) => tableRow([row.version, row.windows, row.linux, row.total]))
      : [emptyRow("No download requests yet.")]));
  };

  const tableRow = (cells) => {
    const row = el("tr");
    cells.forEach((cell, i) => row.append(el(i ? "td" : "th", "", typeof cell === "number" ? number.format(cell) : cell)));
    row.firstChild.setAttribute("scope", "row");
    return row;
  };
  const emptyRow = (text) => {
    const row = el("tr");
    const cell = el("td", "empty", text);
    cell.colSpan = 4;
    row.append(cell);
    return row;
  };

  // "2026-11" → "Nov 26" (UTC month, so no time zone shift).
  const monthTick = (month) => new Date(`${month}-01T00:00:00Z`).toLocaleDateString("en", { month: "short", year: "2-digit", timeZone: "UTC" });

  // Stacked bars, one per day or month (Windows below, Linux above, a 2px gap between), one y-axis from
  // zero, a hover tooltip per bar over a hit area wider than the bar. Each item: {key, tick, windows, linux}.
  const drawBars = (chart, days, ariaLabel) => {
    chart.replaceChildren();
    const width = Math.max(320, chart.clientWidth || 640);
    const height = 220;
    const pad = { top: 8, right: 4, bottom: 22, left: 32 };
    const plotW = width - pad.left - pad.right;
    const plotH = height - pad.top - pad.bottom;
    const peak = Math.max(1, ...days.map((day) => day.windows + day.linux));
    const step = Math.max(1, Math.ceil(peak / 4));
    const top = step * 4;
    const y = (value) => pad.top + plotH - (value / top) * plotH;
    const slot = plotW / days.length;
    const barW = Math.max(2, Math.min(28, slot * 0.62));

    const root = svg("svg", { viewBox: `0 0 ${width} ${height}`, role: "img", "aria-label": ariaLabel });
    for (let i = 0; i <= 4; i++) {
      const value = step * i;
      root.append(svg("line", { class: "grid", x1: pad.left, x2: width - pad.right, y1: y(value), y2: y(value) }));
      const label = svg("text", { class: "axis", x: pad.left - 6, y: y(value) + 4, "text-anchor": "end" });
      label.textContent = number.format(value);
      root.append(label);
    }
    const every = Math.ceil(days.length / 7);
    const tip = el("div", "tip");
    tip.hidden = true;

    days.forEach((day, i) => {
      const x = pad.left + slot * i + (slot - barW) / 2;
      const baseline = y(0);
      const hW = y(0) - y(day.windows);
      const hL = y(0) - y(day.linux);
      const radius = Math.min(4, barW / 2);
      // Windows sits on the baseline; Linux sits above it with a 2px gap. Each segment's data end is rounded.
      if (day.windows) root.append(svg("path", { class: "bar-windows", d: roundedTop(x, baseline - hW, barW, hW, day.linux ? 0 : radius) }));
      if (day.linux) {
        const start = baseline - hW - (day.windows ? 2 : 0);
        root.append(svg("path", { class: "bar-linux", d: roundedTop(x, start - hL, barW, hL, radius) }));
      }
      if (i % every === 0 || i === days.length - 1) {
        const label = svg("text", { class: "axis", x: pad.left + slot * i + slot / 2, y: height - 6, "text-anchor": "middle" });
        label.textContent = day.tick;
        root.append(label);
      }
      const hit = svg("rect", { class: "hit", x: pad.left + slot * i, y: pad.top, width: slot, height: plotH, rx: 4 });
      const show = () => {
        tip.replaceChildren(el("b", "", day.key), el("span", "", `Windows ${number.format(day.windows)} · Linux ${number.format(day.linux)}`));
        const box = chart.getBoundingClientRect();
        const scale = box.width / width;
        tip.style.left = `${Math.min(box.width - 80, Math.max(80, (pad.left + slot * (i + 0.5)) * scale))}px`;
        tip.style.top = `${Math.max(16, y(day.windows + day.linux) * scale)}px`;
        tip.hidden = false;
      };
      hit.addEventListener("pointerenter", show);
      hit.addEventListener("pointerleave", () => { tip.hidden = true; });
      root.append(hit);
    });
    chart.append(root, tip);
  };

  // A bar of height h from (x, y) down, with only its top corners rounded.
  const roundedTop = (x, y, w, h, r) => {
    const radius = Math.min(r, h);
    return `M${x},${y + h}V${y + radius}Q${x},${y} ${x + radius},${y}H${x + w - radius}Q${x + w},${y} ${x + w},${y + radius}V${y + h}Z`;
  };

  form.addEventListener("submit", (event) => {
    event.preventDefault();
    token = input.value.trim();
    input.value = "";
    if (!token) return;
    saveToken(token);
    load();
  });
  range.addEventListener("change", load);
  $("[data-lock]").addEventListener("click", () => {
    forgetToken();
    token = null;
    report.hidden = true;
    form.hidden = false;
    say("Token forgotten.");
  });
  let resizeTimer;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => { if (!report.hidden && token) load(); }, 250);
  });

  if (!endpoint) say("Analytics is not configured: the page has no Convex URL.", true);
  else if (token) load();
})();
