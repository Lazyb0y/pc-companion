# Icons

How PC Companion's own icons look: the app icon, and the icon that names each feature. A new icon starts from the drawings in [`icons/`](icons/), follows this page, and is shown to the maintainer before it's used.

![App icon](icons/app-48.svg) ![Hot Corners](icons/hot-corners-32.svg) ![Clipboard Actions](icons/clipboard-actions-32.svg) ![System Meters](icons/system-meters-32.svg)

## Where icons appear

| Where | Icon | Size at 100% display scale |
|---|---|---|
| Taskbar, Start menu, Alt+Tab, Settings > Apps, File Explorer | App icon, from `src/PCCompanion.App/Assets/PCCompanion.ico` | 16 to 256 pixels; the file has 16, 20, 24, 32, 40, 48, 64 and 256 |
| The settings window's title bar, and the PC Companion icon in the notification area | App icon | 16 |
| Notifications, which come from the PC Companion icon | App icon | 32 |
| Installer images and the website | App icon; `scripts/installer-images.ps1` draws the images, and `website/icon.png` is the 256-pixel image | 256 |
| Navigation pane | Feature icon | 16 |
| Home cards | Feature icon | 20 |
| A feature page's header | Feature icon | 32 |
| Setting cards, buttons and menus inside a page | Segoe Fluent Icons glyph; see [Inside pages](#inside-pages) | 16 to 20 |

- In the navigation pane, on Home and in the header, the settings app shows a feature's drawing when its entry in `FeatureCatalog` names one (`DrawnIcon`), and otherwise its Segoe Fluent Icons glyph, `FeatureInfo.Glyph`. Hot Corners, Clipboard Actions and System Meters have drawings.
- `website/icon.png` and the website's screenshots show the app icon of the latest release; see [Website](../development/website.md).

## App icon

- Two rounded squares: a large blue one at the bottom left, the PC, and a smaller amber one at the top right, the companion that adds to it. The amber square is tilted 10° clockwise and lies over the blue one's top-right corner, and where they overlap the icon is light cream.
- Abstract: no object, letters or text.
- Drawn on a 48-pixel grid, and by hand at 64, 40, 32, 24, 20 and 16 pixels, with the blue square's edges on whole pixels. Other sizes, such as 256 pixels, scale the 48-pixel drawing.

| Size | Blue square: x, y, side, corner radius | Amber square: center, side, corner radius |
|---|---|---|
| 64 | 4, 17, 43, 12 | (45, 19), 29.5, 8.75 |
| 48 | 3, 13, 32, 9 | (33.5, 14.5), 22, 6.5 |
| 40 | 2, 11, 27, 7.5 | (27.75, 12.25), 18.5, 5.5 |
| 32 | 2, 9, 21, 6 | (22, 10), 14.5, 4.25 |
| 24 | 1, 7, 16, 4.5 | (16.5, 7.5), 11, 3.25 |
| 20 | 1, 5, 14, 4 | (14, 6), 9, 2.7 |
| 16 | 1, 4, 11, 3 | (11.5, 4.5), 7, 2 |

- Colors: the blue square is frame blue with the highlight, the amber square is accent amber, and the overlap is `#FFF6E6` → `#FFD9B0`; see [Colors](#colors).
- Don't put it on a plate or in a frame, outline it, recolor it, change its angle or proportions, mirror it, or add shapes or text. Nothing in it may resemble the Windows logo or other Microsoft marks, such as four squares in a grid.
- The one-color version fills both squares and cuts out their overlap.

## Feature icons

- One object that stands for what the feature works on, in blue, and one amber accent for what the feature adds or does:
  - **Hot Corners:** a screen, with its top-left corner in amber and a pointer heading for it.
  - **Clipboard Actions:** a clipboard, with an amber bolt, the action, at its bottom right.
  - **System Meters:** three meters, with their levels in amber.
- One idea per icon. A literal, familiar object, such as a screen, a clipboard or a drive, so the icon reads without its name.
- Flat shapes seen from the front, with soft gradients lit from the top left; no perspective, shadows or textures.
- The same family as the app icon: its blue and amber, rounded corners and light from the top left.

### Colors

| Role | Used for | Fill |
|---|---|---|
| Frame | The object's body | `#5A86FF` → `#3940D6`, with the highlight over it |
| Highlight | Light on the frame | White, from 28% to 0% opacity |
| Deep | Parts on the frame, such as a clipboard's clip | `#3F55D9` → `#2B2FA8` |
| Glass | The object's inside: screens, paper, channels | `#F3F7FF` → `#CBD9FF` |
| Detail | Lines of text and small marks on glass | `#9DB0F5` |
| Accent | What the feature adds or does | `#FFD25E` → `#FF8A3C` |
| Neutral | Things that aren't the object, such as the pointer | `#FFFFFF`, with a `#2B2FA8` outline |

- Every gradient runs from the top left to the bottom right of its shape (`x1="0" y1="0" x2="1" y2="1"`), and the highlight over the top-left 60% (`x2=".6" y2=".6"`).
- Amber is used once per icon (System Meters' three levels count as one). It's never the object, and never the only thing that tells two icons apart.
- At least half of each icon has a contrast of 3:1 or more against light (`#F3F3F3`) and dark (`#202020`) backgrounds: frame blue does this on light backgrounds, and its lighter half and glass on dark ones. Amber has only 1.3:1 to 2.1:1 on light backgrounds, so the object's outline is always blue.

### Shapes and sizes

- Each icon is drawn at 32, 20 and 16 pixels, each on its own: the smaller sizes leave out details and enlarge what must stay readable, such as the pointer and the bolt at 16 pixels, instead of scaling the 32-pixel drawing down.
- At 20 and 16 pixels, straight edges are on whole pixels, so they stay sharp.

| | 32 px | 20 px | 16 px |
|---|---|---|---|
| Object's corner radius | 3.5 to 4 | 2 to 2.5 | 2 |
| Glass's corner radius | 1.5 to 2 | 1 | 0.5 to 1 |
| Frame around the glass | 2 to 3 | 1 to 2 | 1 |
| Gap around a shape that lies over another | 1.5 to 1.7 | 1 to 1.2 | 1 |
| Pointer's and bolt's stroke | 1 to 1.4 | 0.8 to 1 | 0.6 to 1 |

- A pointer, bolt or other modifier sits at the bottom right, over the object.
- Where a shape lies over another, a gap cut through the shapes under it shows the background, so the two stay apart at small sizes.
- Icons aren't mirrored in right-to-left layouts.

## High contrast

- Each icon has a one-color version for high contrast themes, in the theme's text color: the same shapes, with glass, gaps and the app icon's overlap cut out, and everything else filled, including details on the glass.
- No gradients, and nothing added or left out compared with the color version.

## Inside pages

- Setting cards, buttons, menus and notes use Segoe Fluent Icons glyphs, in one color, which follow the theme and high contrast, as Windows Settings does. Two-tone drawings are only for the app icon and the icons that name a feature.

## Files

- [`icons/`](icons/) has the approved drawings as SVG: `<name>-<size>.svg`, and `<name>-<size>-mono.svg` for the one-color version, such as `hot-corners-16.svg` and `hot-corners-16-mono.svg`. `<name>` is the feature's name in lowercase with hyphens, or `app`.
- `scripts/icon-drawings.ps1` writes them from each icon's shapes at each size, and works out the gaps and the one-color versions. To change an icon or add one, change or add its shapes there, run it, then `app-icon.ps1` or `feature-icons.ps1`, and look at the result.
- `scripts/feature-icons.ps1` makes the settings app's images of the feature icons in `src/PCCompanion.App/Assets/Icons`: a PNG of the color and the one-color version for each size an icon is shown at, 16, 20 and 32 pixels, at 100%, 125%, 150% and 200% display scale, from the drawing closest in size. It records the drawings it used in `drawings.txt`, and `PCCompanion.Architecture.Tests` fails when a drawing has changed since.
- `scripts/app-icon.ps1` makes `PCCompanion.ico` from the `app-<size>.svg` drawings; see [Packaging](../development/packaging.md).
- They use only what Windows' SVG renderer, Direct2D, [supports](https://learn.microsoft.com/en-us/windows/win32/direct2d/svg-support), which WinUI's `SvgImageSource` uses: `svg`, `defs`, `g`, `rect`, `path`, `linearGradient`, `stop` and `clipPath`, with attributes rather than `style` or CSS. Direct2D ignores `mask`, `symbol`, `use`, `filter` and `text`.
- Coordinates are in pixels, with a `viewBox` the size of the icon.
- A gap is a `clipPath` on the shapes under it: the whole icon minus the gap's outline, as one path with `clip-rule="evenodd"`. The outline is the shape grown by half its stroke plus the gap, with round corners.
- One-color versions use `fill="currentColor"`, and cutouts are holes in a path with `fill-rule="evenodd"`.

## Checklist

Before an icon is proposed:

- It's one object with one amber accent, and reads at 16 pixels without its name.
- It's drawn at 32, 20 and 16 pixels, with straight edges on whole pixels at 20 and 16, and each size has a one-color version.
- Its colors are the ones in [Colors](#colors).
- It was looked at at its real sizes and enlarged, on light, dark and high contrast backgrounds, next to the icons in [`icons/`](icons/): in the navigation pane, on a Home card and in a page header.
- Its files are written by `scripts/icon-drawings.ps1`, so they use only what Direct2D supports.
