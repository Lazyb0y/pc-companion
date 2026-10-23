# Clipboard Actions

Press Windows + Alt + V to see actions for the copied text, such as opening a copied link or looking up a copied parcel number on the carrier's website.

- Module: Clipboard & Text (`PCCompanion.Modules.Clipboard`). Settings section: `features.clipboardActions`.
- Inspired by the actions of Klipper, the KDE Plasma clipboard tool, which offers commands for copied text that matches a pattern, and by the suggested actions that Windows 11 offered for copied phone numbers and dates until Microsoft deprecated them.
- Off until the user turns it on.

## Behavior

- **Windows + Alt + V** shows a menu with the actions for the text on the clipboard, at the text cursor of the app in front, or at the pointer when the app doesn't report one; see [Where the menu opens](#where-the-menu-opens). Choosing an action runs it; Esc or a click elsewhere closes the menu.
- The menu shows, in this order:
  - the start of the copied text, dimmed, so the user sees what the action gets: its first line, up to 50 characters, with an ellipsis when there's more;
  - **Open in browser**, when the text is a web link and **Open copied links** is on;
  - the name of each rule whose pattern matches, in the order of the rule list;
  - **Settings**, which opens PC Companion's settings, as selecting the PC Companion icon does.
- When nothing matches, the menu says "No actions for the copied text" instead of the actions. Instead of the text, it says why there is none when:
  - the clipboard holds no text, such as a picture or copied files;
  - the app that copied it marked it private, as password managers do; see [Private text](#private-text);
  - the text is longer than 4,096 characters;
  - another app keeps the clipboard open, even after a few tries.
- Spaces and line breaks at the ends of the text are removed before matching, such as the line break a terminal adds when copying a line. The actions get the same trimmed text.
- Nothing happens when text is copied: PC Companion reads the clipboard only when the shortcut is pressed.
- The menu opens as the shortcut is pressed. Releasing Alt and the Windows key keeps it open and opens neither Start nor a menu bar.
- Pressing the shortcut again closes the menu.
- A letter chooses the item that starts with it, when only one does, as in other Windows menus.
- The browser or program that an action opens comes to the front. When the menu closes without an action, as with Esc, the window that was in front gets the front back, so typing goes on there; see [Showing the menu](#showing-the-menu).

## Where the menu opens

- The menu opens at the text cursor of the app in front, where the user is typing, as Windows' emoji panel does. Keyboard and Magnifier users then find it where they're looking. When the app doesn't report a text cursor, the menu opens at the pointer.
- At the text cursor, the menu opens below the cursor's line, starting where the cursor is, or above the line when there isn't room below. It never covers the line, and it stays on the display that has the text cursor. Windows places it so: `TrackPopupMenuEx` gets the line as the rectangle to keep clear (`TPM_VERTICAL`).
- The text cursor is read when the shortcut is pressed, before the clipboard, in one of two ways:
  - **Windows' text cursor**, with `GetGUIThreadInfo`, for apps that use it, such as Windows' own text boxes, WinForms and WPF apps. Windows keeps its position, so nothing asks the app or waits for it.
  - Otherwise **the app's accessibility text cursor** (`OBJID_CARET`), which browsers and apps built on them, such as Edge and Visual Studio Code, report instead, for screen readers and Magnifier. This asks the app, with `WM_GETOBJECT` sent by `SendMessageTimeout`, for the cursor's place and whether it's shown. It runs on a thread of its own, in COM's multithreaded apartment, and the feature's thread waits for it at most 100 ms, so an app that stopped responding delays the menu by at most that. While an earlier request still waits for its app, a press doesn't ask again.
- When an app reports neither, the menu opens at the pointer: apps such as WinUI apps and Windows Terminal, and text that can't be typed in, such as a paragraph on a web page or a button, which has no text cursor.
- A text cursor that the user can't be looking at isn't used: one marked hidden, one with no height, one in a hidden or minimized window, outside its window, or on no display.
- PC Companion asks only an app that runs with the same rights as itself, at the same integrity level (`GetTokenInformation` with `TokenIntegrityLevel`): without admin rights while PC Companion runs without them, and as administrator while it runs as administrator. The answer is an object from the app, which a program shouldn't take in from a less trusted one, such as PC Companion running as administrator from an app without admin rights, or from an app in an AppContainer; Windows' text cursor is still used for them. Windows already blocks the request to an app with more rights.
- An app that isn't aware of each display's scale reports the text cursor in its own pixels, which Windows scales. PC Companion reads Windows' text cursor as that app sees it, with the app window's DPI awareness (`GetWindowDpiAwarenessContext` and `SetThreadDpiAwarenessContext`), and converts both kinds to the display's pixels with `LogicalToPhysicalPointForPerMonitorDPI`.
- Compared with other systems: KDE Plasma's clipboard tool and clipboard tools on macOS open at the pointer. Windows' emoji panel and clipboard history open at the text cursor, which apps tell Windows' text input. Clipboard tools for Windows that open at the text cursor find it in the same two ways, and some open over the middle of the app's window when there's none; PC Companion opens at the pointer then, as it always did.

## Web links

- With **Open copied links** on, the default, text that is one web link, starting with `http://` or `https://` and without spaces, gets **Open in browser**, which opens it in the default browser.
- Only `http` and `https` addresses open, from a link or a rule. The Windows shell starts whatever program is registered for an address, so `file:`, `ms-settings:` and other apps' addresses never open.

## Rules

- A rule has a **name**, shown in the menu; a **pattern**, which the copied text must match; and an **action**:
  - **Open a web address:** opens the rule's address in the default browser, with `{text}` replaced by the copied text, such as `https://tracking.example.com/?number={text}`. The text is encoded for addresses (`Uri.EscapeDataString`), so it can't change the other parts of the address. The address must start with `https://` or `http://`, also after the replacement.
  - **Run a program:** starts the rule's program, with `{text}` in its arguments replaced by the copied text; see [Run a program](#run-a-program).
- Patterns are .NET regular expressions. A pattern must match the whole text, as if it started with `^` and ended with `$`, so a pattern for a number doesn't match a paragraph that contains one. **Match case** is off by default.
- Patterns run with `RegexOptions.NonBacktracking`, whose time grows only with the length of the text, so no copied text can keep a pattern busy; each match also stops after 100 ms. The first match in a process takes about 10 ms longer while .NET prepares its matching code, so a match of a pattern of our own, without a time limit, comes first: otherwise the first rule to match could run out of time on a busy PC and be left out of the menu. Backreferences, lookarounds, atomic groups and conditionals aren't available with it. The rule editor says so in the user's language; .NET's own message for them is in English only.
- Patterns match with the invariant culture, so Match case off works the same in every language, as with the Turkish dotted and dotless i.
- Every rule needs a name; the menu shows up to 60 characters of it.
- Several rules can match the same text. Two rules with the same pattern give two actions for the same text.
- Up to 50 rules; more are left out, with a warning in the log.
- A rule that can't be used, such as one with an unknown action, a pattern that doesn't compile or an address that isn't `http` or `https`, is left out of the menu, with a warning in the log that gives its number.

## Run a program

- The program follows the rules of [Run a program](../input/hot-corners.md#run-a-program) in Hot Corners: the full path of an `.exe` file, typed or chosen with **Browse**, started in its own folder with the user's rights, directly with `ProcessStartInfo.ArgumentList` and never through a shell. PC Companion doesn't wait for it, watch it or stop it.
- **Arguments** are in one box. They're split by the Windows command-line rules first, and then `{text}` is replaced in each argument, so the copied text stays inside one argument, whatever spaces or quotes it holds.
- Programs that read their arguments as commands don't run from a rule: `cmd.exe`, `powershell.exe`, `pwsh.exe`, `wscript.exe`, `cscript.exe`, `mshta.exe`, `wsl.exe`, `bash.exe`, `wt.exe`, `explorer.exe` and `rundll32.exe`. Copied text can come from any web page, and with such a program, text like `x & del …` would run commands of its own. The rule editor says why.
- Some programs read an argument that starts with `-` or `/` as an option. The rule editor notes this; a pattern that starts with a letter or a digit avoids it.
- **The program never gets admin rights, also while PC Companion runs as administrator**, as in Hot Corners. Any program running as the user can change the settings file, so that file must never make PC Companion run code as administrator. The host starts the program through `IHostCommands.StartProgram`: elevated through UAC, it has a copy of itself without admin rights start it; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs).

## Running as administrator

- A browser started by a program running as administrator runs as administrator too. So while PC Companion runs as administrator, a copy of the host without admin rights opens links and web addresses, as the update check opens the release page, and another starts the programs of rules.
- While an app running as administrator is in front, Windows may not pass the shortcut to PC Companion; this is tested by hand.

## Private text

- Apps can mark what they copy as private, so that clipboard history and clipboard tools leave it alone. PC Companion looks for these marks before it reads the text, and when one is there, it reads nothing else and the menu says the copied text is private:
  - `ExcludeClipboardContentFromMonitorProcessing`, which Microsoft documents for keeping data out of clipboard history and sync;
  - `CanIncludeInClipboardHistory` with the value 0;
  - `Clipboard Viewer Ignore`, which some password managers and clipboard tools set.

## Reading the clipboard

- Only plain text (`CF_UNICODETEXT`) is read; Windows converts other text formats to it.
- PC Companion opens the clipboard, copies at most the first 4,097 characters, and closes it right away, so other apps can use it. When another app has it open, PC Companion tries again, five times in all, 20 ms apart, which takes about 150 ms with Windows' timer.
- An app can provide its data only when someone asks for it (delayed rendering), and reading then waits for that app, up to 30 seconds, when Windows gives up. That happens on the feature's own thread; see [Threads](#threads). Until then the menu doesn't show, and another press waits its turn.
- Clipboard tools and the Remote Desktop clipboard often read new data first and keep the clipboard open while such an app provides it. The menu then says another app has the clipboard open, rather than waiting.
- The text stays in memory only until the menu closes and the chosen action has started.

## Showing the menu

- The feature's hidden window comes to the front (`SetForegroundWindow`) before the menu opens, as Windows requires for a menu to close when the user clicks elsewhere. Windows allows it right after the user pressed the shortcut.
- Key messages already waiting for the feature's thread are dropped before the menu opens. While its window is in front, such as after an earlier menu, the shortcut's own Alt key waits there and would close the new menu at once.
- When the menu closes without an action and the feature's window is still in front, the window that was in front before the shortcut gets it back (`SetForegroundWindow`); otherwise typing would go to the hidden window. After a click in another window, that window keeps the front.
- Before an action or Settings starts a program, the feature's thread lets it come to the front (`AllowSetForegroundWindow`), while PC Companion is still in front.

## Threads

- Clipboard Actions runs on a thread of its own, as [Threads and processes](../../architecture/features.md#threads-and-processes) requires: an STA thread with its own hidden window and message loop. Reading the clipboard can wait for the app that copied the text, and the menu runs its own message loop while it's open. On their own thread, neither delays the other features.
- **The host's UI thread** runs only `Apply`: it reads the section, then starts the feature's thread, passes it the new settings with `MessageWindow.BeginInvoke` on its window, or asks it to stop. It never waits for the feature's thread.
- **The feature's thread** registers the shortcut, compiles the patterns when the settings change, and at each press reads the text cursor, reads the clipboard, matches the rules and shows the menu. It gets its own copy of the settings, so the two threads share no state.
- **Asking an app for its text cursor** runs on a thread of its own, in COM's multithreaded apartment, since the app's object needs COM and the call waits for the app. The feature's thread waits for it at most 100 ms, and a press during that wait is left out. A request that an app doesn't answer in time ends when the app answers or ends.
- **The chosen action** starts on the thread pool: Windows can take a while to start a program or the browser, for example while antivirus software checks it.
- **Settings** in the menu runs on the host's UI thread, through `IHostCommands`.
- **Stopping:** the feature's thread closes an open menu, removes the shortcut and its window, and ends. When the feature is turned on again before the old thread has ended, the new thread waits for it before registering the shortcut, which Windows would otherwise refuse. It's a background thread, so one that doesn't end doesn't keep the host running.
- **Failures:** an unhandled exception on the feature's thread would end the host, so everything there runs inside the feature's error boundary. A failure is logged, ends the thread and keeps the feature off until the host restarts, like a failing `Apply`.

## Spike results

A spike in Windows Sandbox, with keys sent by scan code and the clipboard set by a helper, confirmed:

- Windows + Alt + V registers. Windows' own shortcuts, such as Windows + V, Windows + Shift + V, Windows + Ctrl + V, and Windows + Alt + K, D and B, are refused as already registered (error 1409), so a Windows version that took Windows + Alt + V would show up as a refused shortcut.
- The menu opens at the pointer as the shortcut is pressed and stays open when Alt and the Windows key are released, in either order, with no Start and no menu bar. Esc, a click on the desktop and the shortcut again close it. A unique first letter chooses its item at once.
- While the feature's window was in front from an earlier menu, the new menu closed within 5 ms: the shortcut's Alt key-down (`WM_SYSKEYDOWN`) was waiting in the thread's queue. Dropping waiting key messages before the menu fixed it every time.
- Without giving the front back, Esc left the hidden window in front and typed text went nowhere. Giving it back to the window that was in front put the text there, after Esc, Settings and a second press alike, with no Alt left held.
- After `AllowSetForegroundWindow`, the browser opened the link and came to the front, and a program came to the front. Windows Sandbox doesn't apply the foreground lock, so this is also tested by hand.
- The program got the copied text as exactly one argument, with its spaces, quotes, `&`, `%PATH%` and `$env:TEMP` as they were.
- `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` with 0 and `Clipboard Viewer Ignore` each kept the text from being read; `CanUploadToCloudClipboard` with 0 and `CanIncludeInClipboardHistory` with 1 didn't.
- 4,096 characters were read; 4,097 and 1,000,000 were too long. Reading took under 0.3 ms, and 2 ms for 1,000,000 characters.
- With the clipboard held open for 1 second, the five tries gave up after about 150 ms; held for 50 ms, the third try read it.
- An app that provided its text after 1.5 seconds made reading wait 1.5 seconds. For an app that never answered, Windows gave up after 30 seconds (`ERROR_TIMEOUT`). With the Remote Desktop clipboard running, as Windows Sandbox has, it asked first and kept the clipboard open, so the tries gave up instead.
- Windows menus draw the isolate characters (U+2068, U+2069) as boxes. The embedding characters show nothing and give Arabic text its own direction in a left-to-right menu.
- 50 non-backtracking patterns compiled in 96 to 130 ms. Matching all 50 against 4,096 characters took up to 17 ms the first time, while each pattern builds its matcher, and under 1 ms after that. The same harmful patterns with backtracking stopped at the 100 ms limit. With the invariant culture, Match case off doesn't match `i` with the Turkish `İ`.
- A rule's first match took about 11 ms, and with 32 busy threads on 8 processors 108 to 123 ms, so 3 of 4 ran out of time. After a match of our own, the first match took about 1 ms, also with 32 busy threads, and with Match case on or off.

A later spike, for [Where the menu opens](#where-the-menu-opens), in a process that is per-monitor DPI aware as the host is, found:

- `GetGUIThreadInfo` took 0.4 µs on average. The first conversion of a position took about 0.2 ms, later ones about 5 µs.
- Character Map's Characters to copy box, the Run dialog's box, a WinForms text box in a process that isn't DPI aware and a WPF text box in a system-aware one reported their text cursor, where the box was on screen, and it moved as text was typed. WPF doesn't mark its text cursor as blinking, so that mark isn't checked. Character Map's character grid reported none.
- Edge reported none to Windows: not in a text area, the address bar or elsewhere, before or after UI Automation asked it, and not with `--force-renderer-accessibility`. Its accessibility text cursor gave the place in a text area, where the box was on screen, moving as text was typed, and in the address bar, in about 2 ms, 6 ms the first time. With a button or plain text focused, it was marked hidden, with no width.
- Asked for their accessibility text cursor, Character Map's box and the WPF box didn't answer themselves; Windows' standard answer is made from `GetGUIThreadInfo`, already read first, so only an app's own answer is used. Character Map's dialog window, rather than its box, which has the keyboard focus, reported none, so the window with the keyboard focus is asked.
- A read-only WinForms box kept Windows' text cursor; a read-only WPF box reported none.
- With the line kept clear, the menu opened below it, and above it when the line was less than the menu's height from the bottom of the screen, also for a line that ended at the screen's bottom edge. At the right edge, it moved left and stayed below the line. Without keeping the line clear, at the bottom, the menu covered it.
- Windows Sandbox has one display, and its scale can't be changed there, so other scales and several displays are tested by hand.

## Settings

- **Feature page:** the on/off switch; the shortcut, Windows + Alt + V; **Open copied links**, on by default; **Rules**, a list with **Add rule**, and **Edit**, **Move up**, **Move down** and **Delete** for each rule; and a **Permissions & data** card.
- **The rule editor** is a dialog with:
  - **Name**, the text shown in the menu;
  - **Pattern**, with **Match case** and a link to Microsoft's regular expression reference;
  - **Try it**, a box for sample text that shows, as the user types, whether the pattern matches it;
  - **Action**, Open a web address or Run a program, then the address, or the program with **Browse** and its arguments, with notes on `{text}`, options and running as administrator.
- Problems show below their box, as in Hot Corners, and **Save** is available only when the rule has none. **Delete** asks first.
- Whether a program exists is read off the settings app's UI thread, since a program on a network share that's offline can take seconds to find: in the rule editor when the program box changes, and **Save** waits for it; in the list after it shows, which then adds that a rule's program wasn't found.
- When the feature is first turned on, Open copied links is on and there are no rules.
- The switch on Home changes only `enabled` and keeps the other settings.
- A rule keeps its address, program and arguments when its action changes, so choosing the other action again brings them back.
- A section that can't be read, for example after a hand edit, turns Clipboard Actions off with a warning in the log.

```json
"features": {
  "clipboardActions": {
    "enabled": true,
    "openLinks": true,
    "rules": [
      {
        "name": "Track the parcel",
        "pattern": "1Z[0-9A-Z]{16}",
        "matchCase": false,
        "action": "OpenAddress",
        "address": "https://tracking.example.com/?number={text}"
      },
      {
        "name": "Play in the video player",
        "pattern": "https?://\\S+\\.mp4",
        "matchCase": false,
        "action": "RunProgram",
        "program": "C:\\Program Files\\Example\\player.exe",
        "arguments": "--fullscreen {text}"
      }
    ]
  }
}
```

- `action`: `OpenAddress` or `RunProgram`.

## Not included

- Showing the actions on their own when matching text is copied, in a flyout or a notification.
- Choosing another shortcut.
- Actions that change or paste the copied text, such as removing tracking parameters from a link.
- Clipboard history, which Windows has (Windows + V), and pictures, files and formatted text.
- Parts of the match in the actions, such as a pattern's groups; only the whole text.
- Built-in patterns other than web links, such as email addresses, phone numbers and file paths, and links without `http://` or `https://`, such as `www.example.com`.
- Importing and exporting rules.
- Choosing between the text cursor and the pointer: the menu opens at the text cursor whenever the app reports one.
- Finding the text cursor through UI Automation, for WinUI apps and Windows Terminal. It makes several calls into the app, each of which can wait up to 2 seconds, and browsers build their whole accessibility tree when it asks them.

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- Every API is documented and in every supported version: `RegisterHotKey`, the clipboard functions, `RegisterClipboardFormat` for the private marks, `TrackPopupMenuEx`, and for the text cursor `GetGUIThreadInfo`, `SendMessageTimeout` with `WM_GETOBJECT`, `ObjectFromLresult` and `IAccessible`, `GetWindowDpiAwarenessContext`, `SetThreadDpiAwarenessContext` and `LogicalToPhysicalPointForPerMonitorDPI`, the last three since Windows 10 version 1607. `RegexOptions.NonBacktracking` is part of .NET.
- Windows doesn't use Windows + Alt + V, according to Microsoft's list of keyboard shortcuts, and registering it succeeded where Windows' own shortcuts were refused. When another program already has it, Windows refuses it: the shortcut then does nothing, and a warning is logged.

## Security and privacy

- **Reads:** the clipboard's text, only when the shortcut is pressed; no clipboard listener and no hook. First the private marks, and the text only when there are none, at most 4,097 characters. The text stays in memory until the menu closes and the action has started; it's never stored, logged or sent.
- **The text cursor:** where the app in front has its text cursor, only when the shortcut is pressed, to place the menu; never anything typed, and the position is never stored or logged. Windows' text cursor is read from Windows, without rights and without asking the app. The accessibility text cursor asks the app for only the cursor's place and whether it's shown; the answer is checked like any untrusted input, as a place on a display, and a slow app can't delay the menu by more than 100 ms. Only apps with the same integrity level as PC Companion are asked, so no less trusted program hands it an object, also while it runs as administrator.
- **On screen:** the menu shows the start of the copied text, only after the user pressed the shortcut. For a link, it shows where the link goes before it opens.
- **Untrusted text:** the copied text can come from any app or web page, so it's treated as untrusted. Patterns run in time that grows only with the text's length, with a time limit. Addresses get the text encoded and open only as `http` or `https`. Programs get it inside one argument, without a shell, and command interpreters don't run from a rule.
- **Network:** PC Companion makes no connections. Choosing an action that opens a link or web address has the browser open it, and a rule's address sends the copied text to the site the user named. `PRIVACY.md` gets a line on Clipboard Actions.
- **Logs:** Debug entries when the shortcut is pressed, how many rules matched, whether the menu opened at Windows' text cursor, the app's accessibility text cursor or the pointer, without where, and whether the app didn't answer in time, which rule's action ran, by number, and why the menu had no actions, such as private text. Warnings when Windows refuses the shortcut, a rule can't be used, a program doesn't start (with its file name only, as in Hot Corners) or the browser doesn't open. Never the copied text, addresses, patterns or rule names, which can hold personal data.
- **Settings:** the rules are in `settings.json`. Any program running as the user can change that file, but it could also open the same addresses and start the same programs itself, so this crosses no boundary while PC Companion runs as the user. Running as administrator it would, so programs and links still start without admin rights then.
- **Rights:** runs without admin rights, and never starts a program or the browser with them. Turning the feature off removes the shortcut.
- **Failures:** everything on the feature's thread runs inside the feature's error boundary, and the other features keep running.

## Performance

- Between presses nothing runs: no clipboard listener, no timer and no hook. The feature's thread waits in its message loop.
- Patterns compile only when the settings change, on the feature's thread: about 0.1 seconds for 50 rules. A press reads the text cursor in well under a millisecond from Windows, or in a few milliseconds from the app, at most 100 ms, reads at most 4,097 characters, in well under a millisecond unless the copying app provides its data on request, and matches up to 50 patterns, each in time that grows only with the length of the text: under 20 ms for 50 patterns and 4,096 characters, and under 1 ms once each pattern has matched once.

## Accessibility and localization

- The menu is a Windows menu: it works with the keyboard (arrow keys, Enter, Esc and first letters), with screen readers and in high contrast, follows dark mode as the PC Companion icon's menu does, and is laid out right to left in right-to-left languages.
- The preview of the copied text is wrapped in a left-to-right or right-to-left embedding (U+202A or U+202B, ended by U+202C), by its first letter's direction, so text in the other direction keeps its own order. Windows menus draw the newer isolate characters as boxes.
- A `&` in a rule's name shows as written, not as an access key.
- The page and the rule editor work with the keyboard alone. Their boxes, lists and buttons have labels for screen readers, the rule buttons carry the rule's name, such as "Edit Track the parcel", and problems below a box are announced.
- `{text}` is the same in every language; the page explains it in the user's language.
- The strings are translated like the other pages', marked "Needs native review."

## Testing

- **Unit tests** with fake clipboards and fake host commands:
  - matching: the whole text, trimming, Match case, the Turkish i, patterns that don't compile or need backtracking, the 4,096-character limit and the 50-rule limit;
  - web links: `http` and `https` open; `file:`, `javascript:`, `ms-settings:`, other apps' addresses and text with spaces don't;
  - web addresses: encoding of spaces, `&`, `#`, `/` and non-Latin text, and addresses that stop being `http` or `https` after the replacement;
  - programs: `{text}` stays one argument with spaces, quotes and `&`; command interpreters are refused; the host starts the program;
  - private text: each mark keeps the text from being read;
  - the menu: its order, each reason for no actions, the preview's length, ellipsis and embedding, and `&` in names;
  - where the menu opens: at Windows' text cursor with its line kept clear, at the app's accessibility text cursor when Windows has none, and at the pointer when there's neither, when the app doesn't answer within 100 ms, while an earlier request still waits, and for an app with another integrity level, which isn't asked; a text cursor marked hidden, with no height or outside its window isn't used;
  - the front: given back after the menu closes without an action, kept after a click in another window;
  - settings: unknown actions, missing fields and unusable rules are left out with a warning.
- **Sandbox checks** (`scripts/sandbox/groups/ClipboardActions.ps1`): the Home switch turns the feature on; the page adds, edits, moves and deletes rules and saves them, and the rule editor refuses a pattern with a lookahead, an address that isn't a web address, a command interpreter and a program that doesn't exist, and takes one that exists right after it; a rule whose program exists says what it starts, and one whose program doesn't exist shows why in the list. With text the checks put on the clipboard, Windows + Alt + V, sent with `SendInput`, opens the menu with the expected items, read with UI Automation: at the pointer while Character Map's character grid has the focus, below the text cursor of its Characters to copy box, and above it with the window at the bottom of the screen; in Edge, below the text cursor of a local page's text area, and at the pointer with the page's plain text clicked. Open in browser and an address rule start the default browser with the address; a rule that runs a program starts Character Map, and the host logs it by its file name; text marked private, text that's too long and an empty clipboard show only their note. Esc and the shortcut again give the front back to the window that had it. Turned off, the shortcut opens no menu. The host's `--open-web-page` opens a long web address it reads from its pipe, and only web addresses.
- **By hand:**
  - that the menu and the browser come to the front on a PC, where Windows' foreground lock applies;
  - a rule that runs a program, without admin rights, getting the text as one argument;
  - Always run as administrator with UAC on: links open in a browser, and rules' programs start, without admin rights;
  - an app running as administrator in front, with and without Always run as administrator;
  - text copied from a browser, Office and a terminal, and from a password manager that marks it private;
  - a screen reader reading the menu, and a right-to-left language;
  - the menu at the text cursor in Notepad, Word, Chrome and Visual Studio Code, at 125% and 150% scale, in an app that isn't DPI aware, with Magnifier, and on a second display with another scale; and at the pointer in a browser while PC Companion runs as administrator.
