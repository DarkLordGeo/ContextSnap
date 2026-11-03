// Fills the download cards from downloads.json. A card links only when it lists a GitHub Release
// asset URL (written by the release workflow after it verified the public download); otherwise the
// card keeps its disabled "not yet available" state.
const isReleaseAsset = (value) => {
  try {
    const url = new URL(value);
    return url.protocol === "https:" && url.hostname === "github.com" && /^\/[^/]+\/[^/]+\/releases\/download\/[^/]+\/[^/]+$/.test(url.pathname);
  } catch {
    return false;
  }
};

// Install details: one platform at a time, opening out of the bottom of its card (the cards keep one
// height). Opening and closing animate the panel's height; switching closes one while the other opens.
const toggles = [...document.querySelectorAll("[data-details-toggle]")];
const stillMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const panelOf = (toggle) => document.getElementById(toggle.getAttribute("aria-controls"));
// From nothing to the panel's full height (or back), padding included.
const frames = (panel) => {
  const style = getComputedStyle(panel);
  return [
    { height: "0px", paddingTop: "0px", paddingBottom: "0px", opacity: 0 },
    { height: `${panel.offsetHeight}px`, paddingTop: style.paddingTop, paddingBottom: style.paddingBottom, opacity: 1 },
  ];
};
const setOpen = (toggle, open) => {
  const panel = panelOf(toggle);
  const card = toggle.closest(".download");
  if (open === (toggle.getAttribute("aria-expanded") === "true")) return;
  toggle.setAttribute("aria-expanded", String(open));
  panel.getAnimations().forEach((animation) => animation.cancel());
  if (open) {
    card.classList.add("is-expanded");
    panel.hidden = false;
    if (!stillMotion.matches) panel.animate(frames(panel), { duration: 360, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" });
    return;
  }
  const done = () => {
    panel.hidden = true;
    card.classList.remove("is-expanded");
  };
  if (stillMotion.matches) {
    done();
    return;
  }
  panel.animate(frames(panel).reverse(), { duration: 260, easing: "cubic-bezier(0.4, 0, 0.8, 0.6)" }).onfinish = done;
};
for (const toggle of toggles) {
  toggle.addEventListener("click", () => {
    const opening = toggle.getAttribute("aria-expanded") !== "true";
    for (const other of toggles) setOpen(other, opening && other === toggle);
  });
}

// Download counting (convex/http.ts): a click on a download button sends the platform, the release
// version, and a random per-browser ID to the Convex deployment named in the page's
// <meta name="contextsnap-downloads"> tag. It is a download attempt, not a completed download. The link
// itself is untouched, so the download never waits for this or depends on it: the beacon is queued and
// the browser follows the link as usual. A visitor who declined analytics in the cookie banner is still
// counted, but without the ID.
const countDownload = (() => {
  const endpoint = (() => {
    try {
      const url = new URL("/downloads", document.querySelector('meta[name="contextsnap-downloads"]')?.content);
      const local = url.hostname === "127.0.0.1" || url.hostname === "localhost";
      return url.protocol === "https:" || (local && location.hostname === url.hostname) ? url.href : null;
    } catch {
      return null; // not configured
    }
  })();
  const VISITOR_KEY = "contextsnap-download-visitor";
  const declined = () => {
    try { return localStorage.getItem("contextsnap-analytics-consent") === "denied"; } catch { return false; }
  };
  const newId = () => {
    if (crypto.randomUUID) return crypto.randomUUID();
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  };
  const visitorId = () => {
    try {
      if (declined()) {
        localStorage.removeItem(VISITOR_KEY);
        return undefined;
      }
      let id = localStorage.getItem(VISITOR_KEY);
      if (!id || !/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(id)) {
        id = newId();
        localStorage.setItem(VISITOR_KEY, id);
      }
      return id;
    } catch {
      return undefined; // storage blocked: counted without an ID
    }
  };
  // A second click on the same button within a few seconds (a double click) is not counted again.
  const lastSent = new Map();
  return (platform, version) => {
    if (!endpoint) return;
    try {
      const now = Date.now();
      if (now - (lastSent.get(platform) ?? 0) < 4000) return;
      lastSent.set(platform, now);
      const body = JSON.stringify({ platform, version, visitorId: visitorId() });
      // text/plain keeps it a simple request (no CORS preflight); the beacon outlives the page.
      const sent = navigator.sendBeacon?.(endpoint, new Blob([body], { type: "text/plain;charset=UTF-8" }));
      if (!sent) fetch(endpoint, { method: "POST", body, keepalive: true, mode: "no-cors", headers: { "Content-Type": "text/plain;charset=UTF-8" } }).catch(() => {});
    } catch {
      /* counting never gets in the way of the download */
    }
  };
})();

if (document.querySelector(".download")) {
  fetch("downloads.json", { cache: "no-store" })
    .then((response) => (response.ok ? response.json() : Promise.reject(new Error(response.statusText))))
    .then((data) => {
      for (const card of document.querySelectorAll(".download")) {
        const platform = data.platforms && data.platforms[card.dataset.platform];
        if (!platform) continue;
        const details = document.querySelector(`[data-details="${card.dataset.platform}"]`);
        const all = (selector) => [...card.querySelectorAll(selector), ...(details ? details.querySelectorAll(selector) : [])];
        if (platform.detail) card.querySelector(".detail").textContent = platform.detail;
        if (typeof platform.file !== "string" || !isReleaseAsset(platform.file)) continue;
        const spec = (name, value) => {
          for (const element of all(`[data-spec="${name}"]`)) if (value) element.textContent = value;
        };
        spec("version", data.version);
        spec("size", platform.size);
        spec("sha256", platform.sha256);
        // The install and checksum commands name the downloaded file, the folder it extracts to, and the
        // program in it. Release files named Snapctx-* (an interim name) are matched too.
        const file = decodeURIComponent(new URL(platform.file).pathname.split("/").pop());
        const program = file.toLowerCase().startsWith("snapctx") ? "Snapctx" : "ContextSnap"; // the program inside the download
        for (const element of all("[data-file]")) element.textContent = file;
        for (const element of all("[data-exe]")) element.textContent = `${program}.exe`;
        // The Windows card explains the installer or, for releases before it, the ZIP.
        const variant = file.toLowerCase().endsWith(".exe") ? "installer" : "zip";
        for (const element of all("[data-variant]")) element.hidden = element.dataset.variant !== variant;
        const steps = all("[data-linux-steps]")[0];
        if (steps && file.endsWith(".tar.gz")) {
          const folder = file.slice(0, -".tar.gz".length).toLowerCase();
          steps.textContent = `tar -xzf ${file}\n./${folder}/${program.toLowerCase()} --install`;
        }
        // Set by the release workflow, for example how to set up the Linux command.
        all(".install-note")[0].textContent = typeof platform.note === "string" ? platform.note : "";
        const button = card.querySelector(".button");
        button.href = platform.file;
        button.textContent = `Download for ${platform.label}`;
        button.removeAttribute("aria-disabled");
        const platformName = card.dataset.platform;
        if (button.dataset.counted === undefined && (platformName === "windows" || platformName === "linux") && typeof data.version === "string") {
          button.dataset.counted = "";
          const count = (event) => { if (event.type === "click" || event.button === 1) countDownload(platformName, data.version); };
          button.addEventListener("click", count);
          button.addEventListener("auxclick", count); // middle click opens it in a new tab
        }
      }
    })
    .catch(() => {});
}
