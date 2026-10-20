# Website

The project website, [lazyb0y.github.io/pc-companion](https://lazyb0y.github.io/pc-companion/), is published from `website/` with GitHub Pages. Issue: [#106](https://github.com/Lazyb0y/pc-companion/issues/106).

## Content

- One page: what PC Companion is, its features with screenshots, its languages, privacy, how to download and install it, and where to get help.
- It describes the latest release. When a release adds or changes features, update the page and its screenshots; see [Releasing](releasing.md).
- The download buttons link to `releases/latest`, so a release needs no change to the site unless its features change.
- In English.
- `website/icon.png` is the 256-pixel image of the app icon, `src/PCCompanion.App/Assets/PCCompanion.ico`. Replace it when the icon changes.
- Screenshots are `website/images/<page>-light.png` and `<page>-dark.png`: the settings app's window at 1,086 × 736 pixels and 100% scaling, without its borders, with the features turned on. The page shows the dark ones in dark mode.

## Screenshots

```
powershell -ExecutionPolicy Bypass -File scripts\website-screenshots.ps1 -Msi <folder>\PCCompanion-<version>-x64.msi
```

- Takes the screenshots in Windows Sandbox with networking off, so they show nothing of this PC, from the installer of the release the site describes. Without `-Msi`, it builds the installer; `-NoBuild` uses the one already built.
- When every step passes, the screenshots replace those in `website/images`. The report and the screenshots are also in `TestResults\website-screenshots`. Look through them before committing them.
- `scripts/sandbox/website-screenshots.ps1` runs inside the Sandbox and lists the pages and the settings they're taken with. When a release adds a feature, add its page there and turn the feature on, then add it to `index.html`.
- Needs the Windows Sandbox feature, and only one Sandbox can run at a time. Keep the Sandbox window visible until it closes: a minimized Sandbox draws nothing, so its screenshots come out blank.

## Rules

- Plain HTML and CSS: no site generator, packages or JavaScript.
- Nothing loaded from other sites: no web fonts, CDNs, analytics or cookies. The page's Content Security Policy allows only its own images and style sheet.
- Our own icon and screenshots; no Microsoft logos or look-alike branding. The footer says PC Companion isn't affiliated with Microsoft.
- Light and dark mode, keyboard navigation, screen readers, high contrast, and widths from phones up. Logical CSS properties, such as `margin-inline` and `inset-inline-start`, keep right-to-left layouts working.
- Links to repository files point to `main` on GitHub.

## Publishing

- `.github/workflows/website.yml` publishes `website/` when it changes on `main`, and can be run by hand. It packages the folder with `actions/upload-pages-artifact` and deploys it with `actions/deploy-pages`.
- Repository settings: **Pages > Source** is "GitHub Actions". The `github-pages` environment deploys only from `main`.
- Pull requests that change only the site run the same Build checks as others, which `main` requires.
- GitHub Pages logs visitors' IP addresses; [PRIVACY.md](../../PRIVACY.md) says so.

## Previewing

- Open `website/index.html` in a browser; it needs no server. Check light and dark mode, a narrow window and keyboard navigation.
