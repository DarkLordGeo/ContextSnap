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

// Install details: one platform at a time, in the panel under both cards (so the cards keep one height).
const toggles = [...document.querySelectorAll("[data-details-toggle]")];
for (const toggle of toggles) {
  toggle.addEventListener("click", () => {
    const opening = toggle.getAttribute("aria-expanded") !== "true";
    for (const other of toggles) {
      const open = opening && other === toggle;
      other.setAttribute("aria-expanded", String(open));
      document.getElementById(other.getAttribute("aria-controls")).hidden = !open;
    }
    if (opening) document.getElementById(toggle.getAttribute("aria-controls")).scrollIntoView({ block: "nearest", behavior: "smooth" });
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
