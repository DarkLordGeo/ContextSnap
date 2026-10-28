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
        // program in it (releases before the rename to Snapctx are named ContextSnap).
        const file = decodeURIComponent(new URL(platform.file).pathname.split("/").pop());
        const program = file.toLowerCase().startsWith("snapctx") ? "Snapctx" : "ContextSnap";
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
      }
    })
    .catch(() => {});
}
