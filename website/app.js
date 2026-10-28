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

if (document.querySelector(".download")) {
  fetch("downloads.json", { cache: "no-store" })
    .then((response) => (response.ok ? response.json() : Promise.reject(new Error(response.statusText))))
    .then((data) => {
      for (const card of document.querySelectorAll(".download")) {
        const platform = data.platforms && data.platforms[card.dataset.platform];
        if (!platform) continue;
        if (platform.detail) card.querySelector(".detail").textContent = platform.detail;
        if (typeof platform.file !== "string" || !isReleaseAsset(platform.file)) continue;
        const spec = (name, value) => {
          const element = card.querySelector(`[data-spec="${name}"]`);
          if (element && value) element.textContent = value;
        };
        spec("version", data.version);
        spec("size", platform.size);
        spec("sha256", platform.sha256);
        // The install and checksum commands name the downloaded file, the folder it extracts to, and the
        // program in it (releases before the rename to Snapctx are named ContextSnap).
        const file = decodeURIComponent(new URL(platform.file).pathname.split("/").pop());
        const program = file.toLowerCase().startsWith("snapctx") ? "Snapctx" : "ContextSnap";
        for (const element of card.querySelectorAll("[data-file]")) element.textContent = file;
        for (const element of card.querySelectorAll("[data-exe]")) element.textContent = `${program}.exe`;
        // The Windows card explains the installer or, for releases before it, the ZIP.
        const variant = file.toLowerCase().endsWith(".exe") ? "installer" : "zip";
        for (const element of card.querySelectorAll("[data-variant]")) element.hidden = element.dataset.variant !== variant;
        const steps = card.querySelector("[data-linux-steps]");
        if (steps && file.endsWith(".tar.gz")) {
          const folder = file.slice(0, -".tar.gz".length).toLowerCase();
          steps.textContent = `tar -xzf ${file}\n./${folder}/${program.toLowerCase()} --install`;
        }
        // Set by the release workflow, for example how to set up the Linux command.
        card.querySelector(".install-note").textContent = typeof platform.note === "string" ? platform.note : "";
        const button = card.querySelector(".button");
        button.href = platform.file;
        button.textContent = `Download for ${platform.label}`;
        button.removeAttribute("aria-disabled");
      }
    })
    .catch(() => {});
}
