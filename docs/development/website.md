# Website

The project website, [lazyb0y.github.io/pc-companion](https://lazyb0y.github.io/pc-companion/), is published from `website/` with GitHub Pages. Issue: [#106](https://github.com/Lazyb0y/pc-companion/issues/106).

## Content

- One page: what PC Companion is, its features with screenshots, its languages, privacy, how to download and install it, and where to get help.
- It describes the latest release. When a release adds or changes features, update the page, with screenshots of the new or changed pages; see [Releasing](releasing.md).
- The download buttons link to `releases/latest`, so a release needs no change to the site unless its features change.
- In English.
- `website/icon.png` is the 256-pixel image of the app icon, `src/PCCompanion.App/Assets/PCCompanion.ico`. Replace it when the icon changes.
- Each feature is a row of two equal columns, its text and its screenshot, which swap sides from row to row (`feature-reverse`), so every feature's screenshot is the same width.
- Screenshots are `website/images/<page>-light.png` and `<page>-dark.png`, at 100% scaling with the features turned on. The page shows the ones of its theme; the other one is never downloaded.
  - Home's, at the top, is the settings app's whole window at 1,086 × 736 pixels, without its borders.
  - A feature's is only its page, without the navigation pane, so it stays the same when a release adds a feature to the pane. It's as wide as the page in that window, and goes down to the last part of the page in view whole, such as a card, with the page's 24-pixel margin below it, so no part is cut off.
  - The desktop is plain gray while they're taken: the window's Mica backdrop takes its tint from the desktop, and a gray one leaves it neutral.
- Screenshots fade into the page at their edges, over 1.5% of each side and 0.75% for Home's, so neither the window's outline nor the desktop shows, and a shadow raises them off the page. The fade stays inside the app's own margin, so the content stays sharp.

## Theme

- The page follows the system's light or dark mode. The switch in the header chooses System, Light or Dark.
- The switch itself works without JavaScript: it's three radio buttons, and CSS `:has()` sets `color-scheme` from the one that's checked. Each color is `light-dark(light, dark)` in `styles.css`, so it follows `color-scheme`.
- `theme.js` remembers the choice, in the browser's local storage under `pc-companion.theme`: `light` or `dark`, removed for System. It sends nothing anywhere. The site shares `lazyb0y.github.io`, and its storage, with other sites of the account, hence the prefix.
- It runs from `<head>`, before the page is drawn: it sets `data-theme` on `<html>` from the stored choice, which the CSS treats like the checked button, so a dark page never shows in light first. Once the page is loaded, it checks the matching button, and it keeps `data-theme` and the storage in step with the switch.
- Without the script, or with storage off, the switch still works, for as long as the page is open.
- Each screenshot has both images, with `loading="lazy"`. CSS hides the one of the other theme, and a lazy image that's hidden is never downloaded.
- Browsers without `:has()` drop the rules that mention it, including `data-theme`'s, so they hide the switch and follow the system's mode.

## Screenshots

```
powershell -ExecutionPolicy Bypass -File scripts\website-screenshots.ps1 -Msi <folder>\PCCompanion-<version>-x64.msi -Pages "System Meters"
```

- Takes the screenshots in Windows Sandbox with networking off, so they show nothing of this PC, from the installer of the release the site describes. Without `-Msi`, it builds the installer; `-NoBuild` uses the one already built.
- When every step passes, the screenshots of the pages named in `-Pages` replace those in `website/images`. The report and every page's screenshots are also in `TestResults\website-screenshots`. Look through them before committing them.
- Commit only the screenshots of new or changed pages: every version of an image stays in the repository's history. All of them are taken again from time to time, without `-Pages`, when the maintainer asks.
- `scripts/sandbox/website-screenshots.ps1` runs inside the Sandbox and lists the pages and the settings they're taken with. When a release adds a feature, add its page there and turn the feature on, then add it to `index.html`, with each screenshot's width and height from the report.
- A program in a rule must exist in the Sandbox, and not be one that PC Companion refuses, such as File Explorer; otherwise the page shows a warning.
- Needs the Windows Sandbox feature, and only one Sandbox can run at a time. Keep the Sandbox window visible until it closes: a minimized Sandbox draws nothing, so its screenshots come out blank.

## Rules

- Plain HTML and CSS, and one small script of our own, `theme.js`, that remembers the theme: no site generator, packages or other JavaScript. The page works without the script.
- Nothing loaded from other sites: no web fonts, CDNs, analytics or cookies. The page's Content Security Policy allows only its own images, style sheet and script.
- Our own icon and screenshots; no Microsoft logos or look-alike branding. The footer says PC Companion isn't affiliated with Microsoft.
- Light and dark themes, keyboard navigation, screen readers, high contrast, and widths from phones up. Logical CSS properties, such as `margin-inline` and `inset-inline-start`, keep right-to-left layouts working.
- Links to repository files point to `main` on GitHub.

## Publishing

- `.github/workflows/website.yml` publishes `website/` when it changes on `main`, and can be run by hand. It packages the folder with `actions/upload-pages-artifact` and deploys it with `actions/deploy-pages`.
- Repository settings: **Pages > Source** is "GitHub Actions". The `github-pages` environment deploys only from `main`.
- Pull requests that change only the site run the same Build checks as others, which `main` requires.
- GitHub Pages logs visitors' IP addresses; [PRIVACY.md](../../PRIVACY.md) says so.

## Previewing

- Open `website/index.html` in a browser; it needs no server. Check the three themes of the switch with the system in light and in dark mode, a narrow window and keyboard navigation.
