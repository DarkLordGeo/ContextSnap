// Fills the download cards from downloads.json. A card links only when a real, verified file is listed.
fetch("downloads.json", { cache: "no-store" })
  .then((response) => response.json())
  .then((data) => {
    for (const card of document.querySelectorAll(".card")) {
      const platform = data.platforms[card.dataset.platform];
      if (!platform) continue;
      card.querySelector(".detail").textContent = platform.detail;
      if (!platform.file) continue;
      const button = card.querySelector(".button");
      card.querySelector(".meta").textContent =
        `Version ${data.version} · ${platform.size}` + (platform.sha256 ? ` · SHA-256 ${platform.sha256}` : "");
      button.href = platform.file;
      button.textContent = `Download for ${platform.label}`;
      button.removeAttribute("aria-disabled");
    }
  })
  .catch(() => {});
