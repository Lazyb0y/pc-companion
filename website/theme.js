// Remembers the theme chosen with the switch in the header, in this browser's local storage, and
// sends it nowhere. The switch works without this script; see docs/development/website.md.
(() => {
  "use strict";

  // The site shares lazyb0y.github.io, and its storage, with other projects' sites.
  const key = "pc-companion.theme";
  const root = document.documentElement;

  const read = () => {
    try {
      const value = localStorage.getItem(key);
      return value === "light" || value === "dark" ? value : null;
    } catch {
      return null;
    }
  };

  const apply = (theme) => {
    if (theme) {
      root.dataset.theme = theme;
    } else {
      delete root.dataset.theme;
    }
  };

  // Runs from <head>, before the page is drawn, so it never shows in the other theme first.
  apply(read());

  document.addEventListener("DOMContentLoaded", () => {
    const saved = document.getElementById(`theme-${read() ?? "system"}`);
    if (saved) {
      saved.checked = true;
    }

    for (const radio of document.querySelectorAll('input[name="theme"]')) {
      radio.addEventListener("change", () => {
        const theme = radio.value === "light" || radio.value === "dark" ? radio.value : null;
        apply(theme);
        try {
          if (theme) {
            localStorage.setItem(key, theme);
          } else {
            localStorage.removeItem(key);
          }
        } catch {
          // Storage is off, so the choice lasts while the page is open.
        }
      });
    }
  });
})();
