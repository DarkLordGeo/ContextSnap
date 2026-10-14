// Initialize Vercel Web Analytics
import { inject } from '@vercel/analytics';
inject();

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

fetch("downloads.json", { cache: "no-store" })
  .then((response) => (response.ok ? response.json() : Promise.reject(new Error(response.statusText))))
  .then((data) => {
    for (const card of document.querySelectorAll(".download")) {
      const platform = data.platforms && data.platforms[card.dataset.platform];
      if (!platform) continue;
      if (platform.detail) card.querySelector(".detail").textContent = platform.detail;
      if (typeof platform.file !== "string" || !isReleaseAsset(platform.file)) continue;
      const button = card.querySelector(".button");
      const parts = [data.version ? `Version ${data.version}` : null, platform.size, platform.sha256 ? `SHA-256 ${platform.sha256}` : null];
      card.querySelector(".meta").textContent = parts.filter(Boolean).join(" · ");
      button.href = platform.file;
      button.textContent = `Download for ${platform.label}`;
      button.removeAttribute("aria-disabled");
    }
  })
  .catch(() => {});

// Fades sections in as they scroll into view. Sections already on screen, and visitors who prefer
// reduced motion or lack IntersectionObserver, see everything immediately.
(() => {
  if (!("IntersectionObserver" in window) || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.remove("pending");
      observer.unobserve(entry.target);
    }
  }, { rootMargin: "0px 0px -10% 0px" });
  for (const section of document.querySelectorAll("[data-reveal]")) {
    if (section.getBoundingClientRect().top < window.innerHeight) continue;
    section.classList.add("pending");
    observer.observe(section);
  }
})();
