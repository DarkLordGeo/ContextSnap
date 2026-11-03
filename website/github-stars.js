// Shows the repository's GitHub star count next to the GitHub links. The count is cached in the
// visitor's browser for an hour so repeat visits stay well under GitHub's unauthenticated rate limit.
// If the request fails, the badges stay hidden and the links work as before.
(() => {
  const badges = [...document.querySelectorAll("[data-gh-stars]")];
  if (!badges.length) return;
  const KEY = "snapctx-gh-stars";
  const MAX_AGE = 60 * 60 * 1000;
  const format = (count) => new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 }).format(count);
  const show = (count) => {
    for (const badge of badges) {
      badge.querySelector("[data-gh-stars-count]").textContent = format(count);
      badge.hidden = false;
    }
  };
  try {
    const cached = JSON.parse(localStorage.getItem(KEY) || "null");
    if (cached && Number.isFinite(cached.count) && Date.now() - cached.at < MAX_AGE) {
      show(cached.count);
      return;
    }
  } catch {}
  fetch("https://api.github.com/repos/DarkLordGeo/ContextSnap", { headers: { Accept: "application/vnd.github+json" } })
    .then((response) => (response.ok ? response.json() : Promise.reject(new Error(response.statusText))))
    .then((data) => {
      if (!Number.isFinite(data.stargazers_count)) return;
      show(data.stargazers_count);
      try {
        localStorage.setItem(KEY, JSON.stringify({ count: data.stargazers_count, at: Date.now() }));
      } catch {}
    })
    .catch(() => {});
})();
