// Google Analytics 4 (G-HTSCP74CL8) behind a consent banner, using Google Consent Mode in its basic
// form: until the visitor accepts, nothing is loaded from Google and no analytics cookies are set.
// The choice is remembered in localStorage and can be changed with "Cookie settings" in the footer.
// Kept in a file because the Content-Security-Policy does not allow inline scripts.
(() => {
  const MEASUREMENT_ID = "G-HTSCP74CL8";
  const STORAGE_KEY = "contextsnap-analytics-consent";

  window.dataLayer = window.dataLayer || [];
  window.gtag = function gtag() { window.dataLayer.push(arguments); };
  gtag("consent", "default", {
    ad_storage: "denied",
    ad_user_data: "denied",
    ad_personalization: "denied",
    analytics_storage: "denied",
  });

  const readChoice = () => { try { return localStorage.getItem(STORAGE_KEY); } catch { return null; } };
  const saveChoice = (value) => { try { localStorage.setItem(STORAGE_KEY, value); } catch { /* not persisted */ } };

  let loaded = false;
  const enable = () => {
    window[`ga-disable-${MEASUREMENT_ID}`] = false;
    gtag("consent", "update", { analytics_storage: "granted" });
    if (loaded) return;
    loaded = true;
    gtag("js", new Date());
    gtag("config", MEASUREMENT_ID);
    const script = document.createElement("script");
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${MEASUREMENT_ID}`;
    document.head.appendChild(script);
  };

  // Withdrawing consent also removes the cookies Google Analytics set (_ga, _ga_<id>), and the
  // ga-disable flag is Google's opt-out switch: a tag already loaded on this page stops sending.
  const disable = () => {
    window[`ga-disable-${MEASUREMENT_ID}`] = true;
    gtag("consent", "update", { analytics_storage: "denied" });
    for (const name of document.cookie.split(";").map((c) => c.split("=")[0].trim())) {
      if (!name.startsWith("_ga")) continue;
      for (const domain of ["", `; domain=${location.hostname}`, `; domain=.${location.hostname}`])
        document.cookie = `${name}=; Max-Age=0; path=/${domain}`;
    }
  };

  const showBanner = (focus) => {
    let banner = document.getElementById("consent-banner");
    if (!banner) {
      banner = document.createElement("section");
      banner.id = "consent-banner";
      banner.className = "consent";
      banner.setAttribute("aria-label", "Cookie consent");
      banner.innerHTML =
        '<p>May we use Google Analytics cookies to understand how visitors use this site? ' +
        'The ContextSnap apps never send analytics. <a href="/privacy">Privacy details</a></p>' +
        '<div class="consent-actions">' +
        '<button type="button" class="button ghost" data-choice="denied">Decline</button>' +
        '<button type="button" class="button primary" data-choice="granted">Accept</button>' +
        "</div>";
      banner.addEventListener("click", (event) => {
        const choice = event.target.closest("[data-choice]")?.dataset.choice;
        if (!choice) return;
        saveChoice(choice);
        if (choice === "granted") enable(); else disable();
        banner.remove();
      });
      document.body.appendChild(banner);
    }
    if (focus) banner.querySelector("button").focus();
  };

  if (readChoice() === "granted") enable();

  document.addEventListener("DOMContentLoaded", () => {
    if (readChoice() === null) showBanner(false);
    for (const opener of document.querySelectorAll("[data-consent-open]"))
      opener.addEventListener("click", () => showBanner(true));
  });
})();
