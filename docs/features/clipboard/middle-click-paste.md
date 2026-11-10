# Middle-Click Paste

Select text in any app, then middle-click in a text box: the text goes in where the pointer is, without the clipboard.

- Module: Clipboard & Text (`PCCompanion.Modules.Clipboard`). Settings section: `features.middleClickPaste`. Issue: [#50](https://github.com/Lazyb0y/pc-companion/issues/50).
- Inspired by the primary selection of X11 and Wayland on Linux, which GNOME, KDE Plasma and other desktops paste with the middle button.
- Off until the user turns it on.

## Behavior

- **A middle-click on a text box** pastes the text selected in the window in front, at the pointer. Usually the user selects text in one app and middle-clicks straight into a box in another, or elsewhere in the same document.
- **The clipboard isn't read or changed,** so clipboard history, the cloud clipboard and clipboard tools never see the text.
- **Only a plain middle-click on a text box** pastes. Everything else keeps its middle-click:
  - links;
  - tabs;
  - page text, where browsers start scrolling;
  - title bars;
  - the taskbar;
  - games, drawing apps and terminals;
  - the clicks of listed apps.

  See [Recognizing a text box](#recognizing-a-text-box).
- **The click that pastes is kept from the app,** so the app doesn't also act on it. The app comes to the front with the text in the box, as after a drop.
- **One Ctrl+Z** in the app takes the paste out again.
- **When nothing can be pasted,** such as when nothing is selected, a short notice at the pointer says why. The click is still kept from the app; see [When nothing is pasted](#when-nothing-is-pasted).
- **Nothing happens when text is selected:** PC Companion reads the selection only at a middle-click on a text box.

## Compared with other systems

- **X11:** the app with the selection keeps it, and the pasting app asks that app for the text when the user middle-clicks ([freedesktop.org](https://specifications.freedesktop.org/clipboard/1.0/)). Closing that app empties it.
  - Boxes that select all their text when they get the focus replace the selection with their own text, so Firefox's address bar selects its text on a click without making it the selection ([1631575](https://bugzilla.mozilla.org/show_bug.cgi?id=1631575)).
  - GTK, KDE's text editors and Firefox insert the text where the pointer is, not at the text cursor.
- **Wayland** keeps the primary selection, and only the app with the keyboard focus can read it ([protocol](https://wayland.app/protocols/primary-selection-unstable-v1)).
- **GNOME** turned middle-click paste off by default in GNOME 50, and the switch is only in Tweaks ([LWN](https://lwn.net/Articles/1070276/)).
- **KDE Plasma** has a System Settings switch that turns off the primary selection on Wayland, but not for X11 apps ([441668](https://bugs.kde.org/show_bug.cgi?id=441668), [464474](https://bugs.kde.org/show_bug.cgi?id=464474)).
- **Linux users report:**
  - pasting into a document by pressing the wheel while scrolling ([735665](https://bugs.launchpad.net/bugs/735665));
  - pasting with a touchpad's three-finger tap ([103301](https://bugs.freedesktop.org/show_bug.cgi?id=103301));
  - private text reaching web pages through the paste ([1515783](https://bugzilla.mozilla.org/show_bug.cgi?id=1515783));
  - hidden text from a web page running commands in a terminal ([pastejacking](https://thejh.net/misc/website-terminal-copy-paste)).
- **macOS** has no primary selection. Tools that add one copy every selection with Cmd+C and paste with Cmd+V, which replaces the clipboard.
- **Tools for Windows** copy every selection with Ctrl+C and paste with Ctrl+V. Their users report:
  - clipboard history filling up;
  - lost clipboard formats;
  - the middle-click no longer opening links in new tabs or closing tabs;
  - Ctrl+C stopping programs in terminals.

  The better ones paste only over the text pointer and put the clipboard back afterwards, which races the app's paste.
- **Text expanders** insert text in one of two ways:
  - they type it, which is slow for long text and changed by autocorrect and editors' indentation;
  - or they paste it through the clipboard and put the clipboard back after a delay, which no value gets right for every app.
- **Middle-Click Paste:**
  - never copies and never touches the clipboard;
  - reads the selection only at the middle-click;
  - recognizes text boxes before the click, so links, tabs and scrolling keep their middle-click;
  - puts the text where the pointer is, undone with one Ctrl+Z, without typing;
  - leaves password boxes and terminals alone;
  - doesn't paste right after the wheel turns;
  - says why when nothing is pasted;
  - is off by default.

## Which text

- **The selection in the window in front** when the middle-click comes. The click is kept from the app, so it doesn't move the focus. It's read with UI Automation, as Clipboard Actions' Selected text reads it; see [Reading the selected text](clipboard-actions.md#reading-the-selected-text):
  - the window that Windows shows in place of an app that stopped responding isn't asked;
  - only an app at PC Companion's integrity level is asked;
  - the focused element, or the nearest element around it with text, up to 10 levels up;
  - never a password box;
  - the same time limits: 0.5 seconds to connect and 1 second for each request, and at most 1.5 seconds for the whole read, with browsers built on Chromium asked again every 100 ms while they turn on their accessibility support.
- **Parts:** a selection in several parts is joined with line breaks, in the app's order.
- **Only text** is read, exactly as selected, with no trimming.
- **At most 65,536 characters.** One more is read to tell when there are more; longer text isn't pasted.
- **No time limit of its own:** the selection can be pasted for as long as the app still has it selected. Nothing is kept between clicks.
- **A box's own selection of all its text doesn't count.** When the selection is in the box being clicked and covers all of its text, as after a click in a browser's address bar, nothing is pasted.
  - The box with the selection counts as the clicked one when the click is inside its rectangle. After a click, Edge's address bar puts another element under the pointer.
  - Its text is read up to one character more than the selection, to tell whether the selection is all of it. Edge's boxes report the selection's end after the text's, so the ends can't be compared.
  - This is checked for boxes only, not documents, where the whole text can be long.
- **The text stays in memory** only until it's pasted. It's never stored, logged or sent.

## Recognizing a text box

- **When:** the pointer is watched with raw input, as for Title Bar Actions. 50 ms after the pointer stops, the place under it is checked. A check that waits doesn't start another.
- **The pointer:** only where Windows shows its text pointer, the I-beam, is anything asked.
  - It's compared with Windows' own I-beam (`GetCursorInfo`, `LoadCursor` with `IDC_IBEAM`), which asks no app.
  - Links show a hand, and tabs, title bars and the taskbar an arrow, so they're never asked. Games and drawing apps show pointers of their own or none.
  - Apps that draw an I-beam of their own aren't recognized.
- **The window under the pointer** (`WindowFromPoint`) must belong to an app at PC Companion's integrity level, and not to a listed app.
- **The element** under the pointer is then asked for (`IUIAutomation::ElementFromPoint`), on the feature's UI Automation thread, with the same time limits as reading. From it, the nearest element that takes the keyboard focus is found, up to 10 levels up. It's a text box when it's enabled, isn't a password box, has a text pattern or is one of Windows' own plain text boxes (`Edit`), and either:
  - **has a value pattern that isn't read-only:** text boxes, text areas, rich edit boxes and address bars; or
  - **has no value pattern and isn't a document:** editable parts of a web page, such as an email being written.
- **Windows' own plain text boxes** count without a text pattern, since they get the text by window messages, which don't need one. Windows Forms on .NET Framework gives its text boxes none: in the Sandbox checks, a test program's boxes had only a value pattern, so a selection in them isn't read either.
- **Not text boxes:**
  - read-only boxes;
  - password boxes;
  - documents without a value pattern, such as the console's text;
  - read-only documents, such as a web page's text;
  - links in editable text, which take the focus themselves.
- **Claiming the point:** once a text box is recognized, the point, give or take 2 pixels, is claimed for a plain middle-click, without Shift, Ctrl, Alt or the Windows key. The claim is removed as soon as the pointer leaves the point.
- **The wheel:** turning it removes the claim, and the next one waits until the wheel has been still for half a second. A wheel press while scrolling then reaches the app, which may start scrolling, rather than pasting.
- **While a mouse button is held,** as during a drag, nothing is claimed.
- **Apps that don't answer** within the time limits aren't recognized, and their clicks reach them as usual.

## Keeping the click from the app

- **The shared pointer hook:** `PointerHook`, as for Title Bar Actions, set only while some feature has a claim; see [Keeping the click from the app](../windows/title-bar-actions.md#keeping-the-click-from-the-app).
- **What it keeps:** a plain middle-click at the claimed point, with its release.
- **The paste starts when the hook reports a kept click.** Raw input also reports the clicks the hook let through, and those don't paste, since the app got them.
- **A click the hook never saw** while it was set means Windows removed it. A warning is logged, the hook is set again, and that click doesn't paste.
- **A click before the box is recognized,** as right after the pointer arrives, reaches the app as usual.

## How the text goes in

- **Two ways,** by the kind of box:
  - **Windows' own text boxes** get the text by window messages: the `Edit` class and the rich edit classes, and classes built on them, such as Windows Forms' `WindowsForms10.Edit` and `WindowsForms10.RICHEDIT50W`.
  - **Every other box** gets the text as a drop.
- **Never by typing or through the clipboard;** see [Not included](#not-included).

### Windows' own text boxes

- **The text cursor goes to the pointer:**
  - **Plain text boxes:** `EM_CHARFROMPOS`, then `EM_SETSEL`.
  - **Rich edit boxes:** their `EM_CHARFROMPOS` takes a pointer to memory, which Windows can't pass to another app, so UI Automation places the cursor instead (`RangeFromPoint` and `Select`). The same goes for plain boxes with more than 65,535 characters, since `EM_CHARFROMPOS` gives 16-bit positions.
  - For a window that isn't per-monitor DPI aware, the point is converted to the window's own pixels first (`PhysicalToLogicalPointForPerMonitorDPI`), as Title Bar Actions does.
- **The text goes in** with `EM_REPLACESEL`, marked so it can be undone. Windows copies the text into the app's process, as it does for every message below `WM_USER`.
- **No waiting:** each message is sent with `SendMessageTimeout` and `SMTO_ABORTIFHUNG`, at most 500 ms each, so an app that stopped responding gets nothing, now or later.
- **Then the window comes to the front,** and the box gets the keyboard focus (`SetForegroundWindow`, then UI Automation's `SetFocus`).
  - UI Automation focuses a box that has a window of its own by making that window, a child window, the foreground window, a moment later (Sandbox checks).
  - So for 300 ms the front is watched, and when it's the box's window, the top-level window is brought to the front again. The box keeps the focus.

### Dropping the text

- **As a drag does:**
  1. A small window of PC Companion's appears under the pointer. It's 24 pixels square, nearly transparent, always on top and has no taskbar button.
  2. PC Companion presses the primary mouse button on it with `SendInput`: the left button, or the right one when the user swapped them (`SM_SWAPBUTTON`). It presses only when its window is the one under the pointer (`WindowFromPoint`), so no other app ever gets the press.
  3. The window hides and starts OLE drag and drop (`DoDragDrop`) with the text as plain text (`CF_UNICODETEXT`) only, offering only a copy.
  4. Windows asks the app under the pointer whether it would take the text, about every 65 ms while the button is held.
  5. Once the app says it would copy the text, PC Companion releases the button, and the app inserts the text where the pointer is, as for any drop.
- **Without a pressed button,** Windows' drag loop never asks the app, and with PC Companion's window behind others it never ends (spike). So the button press is needed.
- **Before the drop,** the box's app must answer `WM_NULL` within 200 ms (`SendMessageTimeout` with `SMTO_ABORTIFHUNG`).
- **The drag is cancelled** when the app doesn't say within half a second that it would take the text. A drop is also cancelled if the app answers only after 1 second, as one that stopped responding can (spike), so text never arrives late.
- **Where it drops:** at the point of the middle-click. When the pointer has moved more than 4 pixels from it before the text was read, nothing is pasted, and the notice says so, rather than moving the pointer under the user's hand.
- **Afterwards,** the window comes to the front and the box gets the keyboard focus, as for Windows' own boxes, whether or not the app does that itself after a drop. Windows' drag pointer may show for the moment the drop takes.
- **Apps handle the text as they handle drops:**
  - text areas keep line breaks;
  - Edge's single-line boxes turn them into spaces;
  - WPF's single-line boxes keep the first line;
  - Edge's address bar replaces the whole address with the text, without opening it.

### One at a time

- While a paste is still being read or dropped, another middle-click on a text box is kept and pastes nothing, with the notice.
- A paste goes only into the box under the click, never anywhere else.

## When nothing is pasted

- **A notice** next to the pointer says why, for about 3 seconds, or until the pointer moves away:
  - nothing is selected in the window in front;
  - the app with the selection doesn't share it, so the user can copy the text and press Ctrl+V;
  - the selected text is in a password box, which PC Companion never reads;
  - the app with the selection runs as administrator, without admin rights while PC Companion runs as administrator, or with fewer rights, such as some Store apps;
  - the app with the selection, or the box's app, isn't responding or didn't answer in time;
  - the selected text is longer than 65,536 characters, a number shown in the user's regional format;
  - the box doesn't take pasted text, so the user can copy the text and press Ctrl+V;
  - the pointer moved before the text was pasted;
  - another paste is still in progress.
- **The notice** is drawn like Lock Keys' notice (`LayeredWindow` and `TextMask`): it can't be activated, lets clicks through, isn't on the taskbar and has its text as its name.
- **The log gets the reason,** never the text.

## Apps running as administrator

- **Windows keeps apps running as administrator away from programs that don't run as administrator** (spike, with a copy labeled Medium integrity):
  - UI Automation's questions fail, so their boxes are never recognized, and their middle-clicks reach them as usual;
  - their selection isn't read;
  - `EM_REPLACESEL` fails with error 5;
  - `SendInput` reports success, but nothing arrives;
  - while one is in front, PC Companion gets no pointer input.
- **With Always run as administrator,** only apps that also run as administrator are asked, as for Selected text, so pasting works between those apps only. The page's note follows the host's rights, read when the page opens.

## With the other features

- **Title Bar Actions** shares the pointer hook and raw input. The claims can't overlap: title bars show an arrow and answer `HTCAPTION`, and text boxes show the I-beam. If an app ever answered both, the claim made first would win, as for any two claims.
- **Clipboard Actions** shares the code that reads the selected text (`SelectedTextReader` and `SelectedText.CheckWindow`) and its notes. Each feature has its own reading thread and its own switch: Middle-Click Paste works with Clipboard Actions off, and Windows + Alt + S is unchanged.

## Threads

- **The host's UI thread** runs:
  - `Apply`;
  - the pointer events from `RawPointerMonitor`, the 50 ms timer, the check of the pointer and of the window's integrity level, and the claims;
  - the check after a click;
  - the notice.
  
  It never waits for another app: answers come back with `MessageWindow.BeginInvoke`.
- **`PointerHook`'s thread** runs the hook and nothing else.
- **The UI Automation thread:** the feature's own, in COM's multithreaded apartment and with no windows, as a UI Automation client should be. It asks for the element under the pointer and reads the selection, one request at a time; a read comes before a waiting question about the pointer. It starts with the feature and keeps its UI Automation object until the feature stops.
- **The drop thread:** the feature's own, in a single-threaded apartment with OLE started, as drag and drop needs, and its own message loop for the drop window. It starts at the first drop and lives until the feature stops.
- **The thread pool** sends the window messages to Windows' own boxes, since each can wait for the box's app, and watches the front after such a box gets the focus.
- **Stopping:** the claims are removed, a waiting drop is cancelled, and both threads end without the UI thread waiting for them. They're background threads, so one held by an app doesn't keep the host running.
- **Failures:** everything on these threads runs inside the feature's error boundary. A failure is logged and keeps the feature off until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 build 26100 used Edge, test windows with Windows Forms and WPF boxes, Character Map, the Run box and the console. A copy labeled Medium integrity covered apps running as administrator. It confirmed:

- **The pointer:**
  - The I-beam showed over every text box, read-only and password boxes included, and over page text.
  - Links showed a hand. Tabs, Edge's empty title bar and page, the taskbar, labels and the console showed an arrow.
  - Checking the pointer took about 3 µs.
- **The element under the pointer:**
  - **Speed:** a median of 2.6 to 4.5 ms, at most 17 ms. Edge's first answer came after about 0.3 seconds, while it turned on its accessibility support.
  - **Boxes** reported a value pattern that says whether they're read-only, and password boxes said so.
  - **Edge's editable text** gave the text under the pointer, whose focusable parent was the editable element.
  - **Page text** gave text inside a read-only document.
  - **The console** gave a document without a value pattern.
- **The selection:**
  - The window in front's selection was read in 1 to 30 ms, and 65,536 characters in 9 ms.
  - Windows behind kept a readable selection: Edge's page and text area, Windows' own boxes and WPF. Windows reports no focus for a background window's thread, though.
  - A click in Edge's address bar selected the whole address.
- **Dropping**, with the button pressed on the spike's own window:
  - **Took the text** where the pointer was, undone with one Ctrl+Z:
    - Edge's single-line box, text area, editable text and address bar;
    - WPF boxes;
    - Character Map's rich edit box.

    Each paste took 0.2 to 0.4 seconds: Edge first answered that it wouldn't take the text, then that it would, up to about 100 ms later. Arabic, Chinese and an emoji came through intact, and the app came to the front.
  - **Refused it:** Windows' plain text boxes (Windows Forms and the Run box), Windows Forms' rich text box, the console and read-only boxes.
  - **Took it although they shouldn't:** password boxes in Edge and WPF.
  - **An app that stopped responding** held the drag loop for 7.7 seconds and then still took the text.
  - **Without a pressed button,** no app was asked, and the drag never ended while the spike's window was behind others.
  - **Once,** with the Run box in front, the spike's window didn't get the press.
- **Window messages:**
  - `EM_REPLACESEL` put the text exactly at the pointer in Windows Forms' plain and rich text boxes, the Run box and Character Map's box, in 0.6 to 2 ms, and one `EM_UNDO` took it out. The window in front didn't change.
  - An app that stopped responding made each message fail after 500 ms, and nothing arrived later.
- **Typing, for comparison:**
  - **Speed:** 1,000 characters took 1.5 seconds in Edge, 2 seconds in a plain text box and 7 seconds in WPF. 4,096 took 13 seconds in Edge, and over 38 seconds in WPF before the check broke.
  - **Keys:** Enter submitted a form, and Tab moved to the next box. Line breaks typed as characters disappeared.
  - **A click before typing,** 150 ms after the user's click, selected a word.
  - One Ctrl+Z did take all of the typed text out.
- **Medium integrity against apps running as administrator:**
  - UI Automation's questions failed, and the selection wasn't read;
  - `EM_REPLACESEL` failed with error 5;
  - `SendInput` reported success, but nothing arrived;
  - the spike's window got no press while such an app was in front.
- **Not checked yet:**
  - whether a 60,000-character drop arrives whole in Edge: reading it back gave about 7,200 characters;
  - placing the text cursor in a rich edit box with UI Automation, and giving a box the focus after `EM_REPLACESEL`;
  - Office, Visual Studio Code, Firefox, Chrome, Windows Terminal, Windows 11's Notepad, Java apps and WinUI 3 apps;
  - a real mouse and touchpad, the foreground lock, swapped buttons, UAC on, other scales and Windows 10.

## Settings

- **Feature page:**
  - the on/off switch;
  - a card that says how it works: select text, then middle-click in a text box;
  - **Apps that keep their middle-click,** with **Running app**, **Program file** and **Remove**, as in Title Bar Actions;
  - a note that apps share their selected text through Windows' accessibility support, which some apps don't have, and that password boxes and terminals are left alone;
  - a note about apps running as administrator, following the host's rights;
  - a note that this is a mouse action, with Ctrl+C and Ctrl+V for the keyboard;
  - a **Permissions & data** card.
- **When the feature is first turned on,** no app is listed.
- **The switch on Home** changes only `enabled` and keeps the list.
- **A section that can't be read,** for example after a hand edit, turns the feature off with a warning in the log.
- **Listed apps** are read as Title Bar Actions reads its list, with the same code (`AppOfWindow`): only while the list has apps, once for each top-level window the pointer comes to.
- **Permissions & data** says:
  - that the pointer is watched while the feature is on;
  - that when it rests on text, the app is asked what's under it, never its text;
  - that the selected text is read only at a middle-click on a text box, never from a password box, and isn't stored, logged or sent;
  - that the clipboard isn't used;
  - that asking a browser turns on its accessibility support, which uses more memory on very long pages, until the browser closes.

```json
"features": {
  "middleClickPaste": {
    "enabled": true,
    "excludedApps": [
      { "name": "Example Paint", "program": "C:\\Program Files\\Example\\paint.exe" }
    ]
  }
}
```

- `name` is shown on the page only.

## Not included

- **Reading every selection as it's made,** and keeping a selection after the app no longer has it. It would come close to watching what the user reads, and would need event handlers in every app.
- **The selection of the window that was in front before,** for a middle-click on the window in front when nothing is selected there. Windows behind keep a readable selection (spike), but finding the element that has it needs the focus followed in every app.
- **Typing the text:**
  - it's slow for long text (13 seconds for 4,096 characters in Edge);
  - autocorrect and editors' automatic indentation and brackets change it;
  - Enter submits forms and runs commands, and Tab moves to the next box;
  - placing the text cursor first takes a click, which can select a word.
- **The clipboard:**
  - Putting it back loses formats it can't read, makes the app that copied provide all its formats, and clears Excel's copied range.
  - It races the app's paste, since no delay is right for every app.
  - Ctrl+V doesn't paste in every app.
  - The text would reach clipboard tools that ignore the marks for private content.
- **UI Automation's value pattern,** which replaces the box's whole text, with its formatting and undo.
- **Terminals:**
  - the console doesn't take drops, and its text isn't a text box;
  - text with line breaks can run commands, the risk terminals' own paste guards against;
  - Windows Terminal and terminals in editors are checked by hand before any support.
- **Middle-clicks with Shift, Ctrl, Alt or the Windows key,** and other buttons: [#39](https://github.com/Lazyb0y/pc-companion/issues/39).
- **Copying every selection to the clipboard,** and pasting the clipboard with the middle button: Ctrl+C and Ctrl+V do that.
- **Text input services** loaded into every app, and accessibility interfaces that need a component registered with Windows (IAccessible2).
- **Reading or pasting into apps with other rights,** such as through a copy of PC Companion that runs with that app's rights.
- **Recent results on the page:** the notice says why at the moment, and Debug logs say why later.
- **A setting for the length limit.**

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- **The APIs are documented** and in every supported version:
  - **the pointer:** `GetCursorInfo`, `LoadCursor`, `RegisterRawInputDevices`, `GetRawInputData`, `SetWindowsHookEx` with `WH_MOUSE_LL` (through `PointerHook`), `GetSystemMetrics` with `SM_SWAPBUTTON`, and `SendInput`;
  - **windows:** `WindowFromPoint`, `SendMessageTimeout` with `WM_NULL`, `EM_CHARFROMPOS`, `EM_SETSEL` and `EM_REPLACESEL`, `SetForegroundWindow`, `PhysicalToLogicalPointForPerMonitorDPI` and `UpdateLayeredWindow`;
  - **UI Automation's client interfaces:** `CUIAutomation8`, `IUIAutomation2`, `ElementFromPoint`, `SetFocus`, and the text pattern, with `RangeFromPoint` and `Select`, and the value pattern, in Windows since Windows 8;
  - **OLE drag and drop:** `OleInitialize`, `DoDragDrop`, `IDropSource` and `IDataObject`.
- **What an app takes depends on the app:** whether it shares its selection, accepts dropped text, or shows Windows' I-beam; see [When nothing is pasted](#when-nothing-is-pasted).
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:**
  - the pointer's position, buttons and wheel, by raw input, only while the feature is on;
  - the pointer's shape, from Windows;
  - while the pointer rests on text, the element under it: its kind, whether it's enabled, read-only or a password box, and its window's class. Never its text or name;
  - at a kept middle-click on a text box, the selection in the window in front, at most 65,537 characters, as Selected text reads it: never from a password box, never the document's other text, never the clipboard;
  - the app of the windows the pointer comes to, only while apps are listed.

  It reads nothing between clicks: no UI Automation event handlers, no keyboard hook, no clipboard listener.
- **Writes:**
  - The text goes only into the box under the middle-click, right after the user clicked there: by window messages to Windows' own boxes, or as a drop, which the app handles as its own.
  - It never types keys, never writes the clipboard and never pastes into a password box.
  - Its only injected input is one press and release of the primary button on its own window under the pointer, and only when that window is the one there.
- **Untrusted text:** the selection can come from any page or app.
  - It goes in as text only, exactly as a drop of copied text would.
  - Terminals are left alone, since hidden text with line breaks could run commands.
  - Web pages see a drop as a drop event, as they see a paste on Linux.
- **Other apps:** asking a browser built on Chromium what's under the pointer turns on its accessibility support until the browser closes, as a screen reader does. It happens as soon as the pointer rests on text in it, not only at a paste. In Clipboard Actions' spike, that cost about 250 MB on a page with 20,000 paragraphs and nothing on a short one. Permissions & data says so.
- **Logs:**
  - **Debug entries:**
    - when a text box is claimed and the claim removed;
    - for each kept click: how long reading took, whether the text went in by message or drop, and why nothing was pasted, such as a password box, a listed app, an app with other rights or a box that didn't take it.
  - **Never:** the text, titles, element names, app IDs, paths or pointer positions.
  - **Warnings** when Windows removed the hook, or the settings section can't be read.
  - **Errors** when a failure stops the feature.
- **Settings:** a changed settings file can only list apps that keep their middle-click.
- **Network:** none. `PRIVACY.md` gets a line on Middle-Click Paste.
- **Rights:**
  - It runs with the host's rights, and asks, reads from and pastes into apps at the same integrity level only. So it never reads an app with more rights and never takes in objects from one with fewer.
  - It doesn't use UI Automation's access for assistive technology (`uiAccess`).
  - Turning the feature off removes the claims, stops reading the pointer and ends both threads.

## Performance

- **Away from text,** each pointer event costs a few microseconds on the UI thread and resets the 50 ms timer. Raw input costs nothing while the pointer is still.
- **At each rest:**
  - the pointer check takes about 3 µs;
  - over the I-beam, one question to the app on the feature's thread takes a few milliseconds: a median of 2.6 to 4.5 ms in the spike, about 0.3 seconds for a browser asked for the first time.
- **The hook** is set only while the pointer rests on a text box. While it's set, every pointer event passes through its thread, which only compares the event with the claims.
- **A paste:**
  - reading takes a few milliseconds, at most 1.5 seconds;
  - window messages take 1 to 2 ms;
  - a drop takes 0.2 to 0.4 seconds.

## Accessibility and localization

- **The page** works with the keyboard alone and has labels for screen readers. It follows the Windows mode, dark or light, and high contrast, and is laid out right to left in right-to-left languages.
- **The feature is a mouse action.** The page says so and names Ctrl+C and Ctrl+V for the keyboard.
- **The notice:**
  - follows the Windows mode and high contrast's colors;
  - is drawn at the display's scale and Windows' text size, right to left in right-to-left languages;
  - shows on the pointer's display.
  
  Screen readers can find it by its name, but the feature announces nothing: the host has no UI Automation provider.
- **Pasted text** keeps its own order: Arabic, Chinese and emoji came through intact in the spike.
- **Strings:** the strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests,** with fake elements, a fake hook, a fake clock and fake raw input:
  - **recognizing a text box:**
    - the I-beam;
    - the window's integrity level and listed apps;
    - each rule for the element: a value pattern read-only or not, editable parts of a page, documents, password boxes, disabled elements, Windows' own plain boxes without a text pattern, a link inside editable text, and the 10 levels;
    - an app that doesn't answer in time;
  - **claims:**
    - a plain middle-click only, give or take 2 pixels;
    - removed when the pointer leaves;
    - none while a button is held;
    - the wheel removing the claim, and the half second before the next;
  - **the check after a click:** a kept click pastes, one let through doesn't, and one never seen sets the hook again and logs a warning;
  - **reading:**
    - each result and its notice;
    - the box's own selection of all its text not counting;
    - the 65,536-character limit;
    - a click while a paste is in progress;
  - **choosing the way:** Windows' own plain and rich edit classes, and their Windows Forms classes, get messages, and every other box a drop;
  - **the drop's decisions:**
    - release once the app would copy;
    - cancel after half a second without that, and when the app answers after 1 second;
    - no press unless the window under the pointer is PC Companion's;
    - no paste when the pointer moved more than 4 pixels;
    - the right button when the buttons are swapped;
  - **the messages' order:** `EM_CHARFROMPOS` only for plain boxes up to 65,535 characters, and placing by UI Automation for the others;
  - **the settings:** defaults, the spec's example, the app list, and a section that can't be read.
- **Sandbox checks** (`scripts/sandbox/groups/MiddleClickPaste.ps1`), with Edge, test windows the checks start, Character Map and the Run box, middle-clicks sent by `SendInput`:
  - **the page:** the Home switch turns the feature on, and the page saves the app list, with Running app and Remove;
  - **pasting:**
    - **into boxes:** text selected in an Edge page goes in at the pointer in a test window's WPF box, Windows' own plain and rich text boxes, Character Map's box and the Run box;
    - **into Edge:** text selected in a test window goes into Edge's text area, single-line box and editable text;
    - one Ctrl+Z takes each paste out, and the window comes to the front;
  - **left alone:**
    - in Edge, a middle-click on a link opens a tab, one on a tab closes it, and one on page text starts scrolling;
    - with Title Bar Actions on, one on a title bar minimizes the window;
    - the console, read-only and password boxes get their click, and nothing is pasted;
    - right after the wheel turns, a middle-click on a text box isn't a paste;
  - **notices:** each one's reason in the host's log, for:
    - nothing selected;
    - a selection in a password box;
    - a test window that stops responding, as the app with the selection and as the box;
    - text that's too long;
    - a box that doesn't take dropped text;
    - a second click while a paste waits;
  - **rights:** with the host labeled Medium integrity, a test window running as administrator isn't recognized and gets its click;
  - **privacy:** the clipboard's sequence number doesn't change, and the host's log never holds the text;
  - **the hook:** a listed app gets its click, and the hook is set only while the pointer rests on a text box;
  - **the page** in English and Arabic, in light and dark, with screenshots;
  - no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** a real mouse and touchpad, the foreground lock, several displays and scales, UAC on, Windows 10, Narrator, and apps it doesn't have.
- **By hand:**
  - Word, Outlook and Excel while a cell is edited;
  - Visual Studio Code's editor and terminal;
  - Firefox and Chrome;
  - Windows Terminal, Windows 11's Notepad, Teams, a Java app, a WinUI 3 app and a Store app in an AppContainer;
  - an app with an I-beam of its own;
  - a 60,000-character paste in a browser;
  - a real mouse's wheel press while scrolling, and a touchpad's three-finger tap;
  - swapped mouse buttons;
  - the app coming to the front with the focus in the box on a PC, where the foreground lock applies;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - several displays at different scales, and an app that isn't DPI aware;
  - the notice in high contrast, with Narrator and in a right-to-left language;
  - Windows 10.
