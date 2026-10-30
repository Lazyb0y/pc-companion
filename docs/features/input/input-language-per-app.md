# Input Language per App

Switch the keyboard language automatically for the app in front: a fixed language for the apps the user chooses, and for every other app, the language it had last.

- Module: Input & Triggers (`PCCompanion.Modules.Input`). Settings section: `features.inputLanguages`. Issue: [#42](https://github.com/Lazyb0y/pc-companion/issues/42).
- Inspired by the keyboard layout policies of KDE Plasma and the per-app input source tools of macOS.
- Off until the user turns it on.

## Behavior

- **Apps with a language:** the user lists apps and gives each an input method, such as German for a chat app and English for a code editor. Each time a listed app comes to the front, it switches to its input method.
- **Other apps** get one of three behaviors; see [Other apps](#other-apps):
  - **Remember each app's language**, by default: each app gets back the input method it had when the user last left it, also after a restart;
  - **Keep the language in use**: PC Companion doesn't switch them, and Windows does what its own setting says;
  - **Use one language**: they all switch to one input method.
- It switches once, when an app comes to the front, never while the user stays in it. A switch the user makes by hand holds until they leave the app; see [When it switches](#when-it-switches).
- A short notice shows the new language when PC Companion changed it, such as "German (Germany)", like Lock Keys' notice; see [The notice](#the-notice).
- It works with Windows' own **Let me use a different input method for each app window** on or off, and never changes it.
- It never adds or removes input methods, and never changes Windows' language settings.
- Apps running as administrator aren't switched while PC Companion runs without admin rights; the page says so. See [Apps running as administrator](#apps-running-as-administrator).

## Compared with other systems

- **Windows** has **Let me use a different input method for each app window**, in **Advanced keyboard settings**, off by default. It keeps each window's language only while the window is open: a new window starts in the default input method, and nothing makes an app always start in one. With it off, one language follows the user into every app. Input Language per App works per app, as the taskbar groups apps, gives chosen apps a fixed language, and remembers the others' languages across restarts, with Windows' setting on or off.
- **macOS** keeps the input source per document when **Automatically switch to a document's input source** is on, and forgets it when the document closes. Tools for macOS set a language per app, and some per website. Their users report switches that don't take, languages flipping back and forth when a tool fights the user's own switch, and Chinese, Japanese and Korean input methods that don't really switch. Input Language per App switches once, when an app comes to the front, and uses the request Windows itself sends when the user switches. It shows its notice only when it has confirmed the change.
- **KDE Plasma** remembers the layout per desktop, app or window, but turned down a fixed layout per app ([174948](https://bugs.kde.org/show_bug.cgi?id=174948)). On Wayland it lost an app's layout within the same session until a fix in October 2026, and its layout notice was reported to show when the user only starts typing ([452759](https://bugs.kde.org/show_bug.cgi?id=452759)). Input Language per App has fixed languages and remembered ones, and its notice can be turned off.
- **GNOME** can switch input sources for each window, but a reopened window gets the first source rather than the one used last. Xfce's and Cinnamon's per-window modes are reported to be unreliable. IBus and Fcitx5 can share one state or keep one per program, with no fixed languages. Input Language per App remembers an app's language after it closes.
- **Tools for Windows** that set a language per program fail on apps running as administrator and on Store apps, and some use keyboard hooks or check many times a second, which antivirus and anti-cheat software flag. Input Language per App switches Store apps and consoles, uses no keyboard hook and runs no code in other apps, reads an app's language only when the app in front changes and once a second, and says on its page which apps it can't switch.

## Input methods

- An **input method** is an entry in the user's list in **Settings > Time & language > Language & region**, as Windows' input switcher (Windows + Space) shows it: a keyboard layout, such as German or US-International, or an input method editor (IME), such as Microsoft Pinyin.
- **Keyboard layouts** are each an input method of their own, also two of one language, such as US and US-International.
- **IMEs** are chosen by language: a switch to a language with IMEs gets the one of them the user used last, as the spike showed with Microsoft Pinyin and Wubi. So the page lists such a language once, by its name; see [Not included](#not-included).
- **Identifiers:** a rule stores Windows' own identifier, never the handle Windows gives a layout for the session (`HKL`):
  - a keyboard layout as Windows writes it in the user's language list, the language and the layout: `0407:00000407` for German, `0409:00020409` for US-International;
  - a language with IMEs as its language alone: `0411` for Japanese.
- **From identifier to handle:** the host takes the user's layouts from `GetKeyboardLayoutList`, in the order of the input switcher. A handle's low word is its language; its high word is the layout, or, when it starts with `F`, the layout's `Layout Id` in the registry's list of keyboard layouts (`HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts`), as .NET's `InputLanguage.LayoutId` reads it: US-International is `0xF0010409`, and `Layout Id` `0001` belongs to `00020409`. A language with IMEs has one handle in the list for all of them. Handles are compared on their low 32 bits: some APIs return them sign-extended, such as `0xFFFFFFFFF0010409`.
- **Names** are Windows', in Windows' display language, as its switcher shows them: the language's name (`GetLocaleInfoEx` with `LOCALE_SLOCALIZEDDISPLAYNAME`), such as "German (Germany)", and the keyboard layout's (`Layout Display Name`, read with `SHLoadIndirectString`, otherwise `Layout Text`), such as "German".
- **The page** lists the input methods from the text input framework's enabled profiles (`ITfInputProcessorProfileMgr::EnumProfiles`): keyboard layouts with their handle, and IMEs by language. An entry of language 0, which Windows lists among them, is skipped.
- **Only from the user's list:** PC Companion switches only to handles in `GetKeyboardLayoutList`, so it can't add a layout to the session. It never calls `LoadKeyboardLayout`, which adds one.
- A rule whose input method is no longer in the user's list is skipped, with a Debug entry, until it's back; the page says so in the app's row.

## Apps and rules

- **The app in front** is the app of the window in front, or of the window that owns it (`TopLevelWindows.RootOwner`), such as a dialog's main window, when the taskbar shows that window (`TopLevelWindows.MayHaveTaskbarButton`).
- **What counts as one app:** the windows the taskbar groups as one app, with the same code as App Windows, Minimize to Tray, Hide Apps and Hot Corners (`AppOfWindow`): the app ID the window sets for itself, as browsers do for each profile and installed web app; otherwise its Store app package's app ID; otherwise its program file. So each browser profile and each installed web app has its own language, and every File Explorer window shares one.
- **Not an app:** windows the taskbar doesn't show and that no such window owns, such as the desktop, the taskbar, Start, Search, Alt + Tab, the input switcher, the emoji panel, notification-area pop-ups and the lock screen. They're never switched, and going from an app to one of them and back is staying in that app.
- **Listed apps** are added as in Hot Corners and Minimize to Tray: **Running app** lists the apps with windows on the taskbar, and **Program file** picks a program with the Open dialog. Each has an input method. **Remove** takes one out.
- An app that moves its program file when it updates, as some apps installed in a folder named after their version do, must be added again.
- PC Companion's settings window counts as any other app's window.

## Other apps

### Remember each app's language

- The default. When the user leaves an app, PC Companion reads the input method it had, and when the app comes back, switches it back to that one.
- An app with nothing remembered yet isn't switched: it starts with what Windows gives it, and its input method is remembered when the user leaves it.
- While an app without a rule is in front, its input method is also read once a second, so an app that closes while in front keeps the one it had last.
- Apps with a rule aren't remembered. Remembered input methods are kept for up to 500 apps; see [Remembered languages](#remembered-languages).

### Keep the language in use

- PC Companion doesn't switch apps without a rule. Windows does what its setting says: with **Let me use a different input method for each app window** off, the language in use follows the user into the app; with it on, each window keeps its own.

### Use one language

- Every app without a rule switches to one input method, chosen on the page, each time it comes to the front.
- With no input method chosen, or one that isn't in the user's list, it switches nothing.

## When it switches

- **When an app comes to the front** from another app (`EVENT_SYSTEM_FOREGROUND`), PC Companion reads the input method of the window with the keyboard focus and, when it isn't the one the app should have, asks for the switch.
- **When a window of the same app comes to the front** that hasn't been in front since the app came to the front, such as a new window, it gets the app's current input method: the one its window in front had last, including a switch the user made by hand. With Windows' setting off, every window already has it, so nothing changes; with it on, a new window would otherwise start in the default input method.
- **Never while the user stays in an app.** A switch the user makes by hand holds until they leave the app. With Remember, it becomes the app's language. With a rule, the rule applies again the next time the app comes to the front.
- **Only when it differs:** a read that already shows the right input method asks for nothing, so the foreground event coming twice, as the spike saw, does no harm.
- **Windows' setting off:** as an app comes to the front, Windows first gives it the language in use. PC Companion's request comes after Windows' change, so it holds: in the spike, a request from the foreground event, about 16 ms after the change, held in every switch. Meanwhile, the taskbar's language indicator can show the language in use for a moment.
- **Windows' setting on:** Windows doesn't switch on its own, and the rules apply as they do with it off.

## How it switches

- **Read:** the window with the keyboard focus, from `GetGUIThreadInfo` for the thread in front, and its thread's input method (`GetKeyboardLayout`), sending no message to the app. For Store apps the focus is in the app's own process, not its frame's, and its thread is the one that switches: in the spike, Settings' app window switched while its frame window didn't. `KeyboardLayout.OfForegroundWindow` already reads it this way.
- **Switch:** `PostMessage` of `WM_INPUTLANGCHANGEREQUEST` with the input method's handle to the window with the focus, the request Windows itself posts when the user switches. Posting doesn't wait for the app, and an app takes posted messages before key presses (`GetMessage`), so keys typed after the request go in the new language.
- **Confirm:** the input method is read again 50 ms after the request, and every 50 ms after that up to 500 ms. When it changed to the one asked for, the notice shows; otherwise a Debug entry says the app didn't take it, and nothing is tried again until the app next comes to the front.
- **Why not the text input framework:** asked from another process, `ITfInputProcessorProfileMgr::ActivateProfile` with `TF_IPPMF_FORSESSION` did nothing for keyboard layouts, though it reported success, and switched an IME for every app in the session at once, including background apps and apps running as administrator, also with Windows' setting on. So it can't switch one app.

### Consoles

- A console window, such as Command Prompt's or PowerShell's in the console host, reports the console program as its process (`GetWindowThreadProcessId`), and that program's thread reads no input method. `GetConsoleKeyboardLayoutName` failed in the spike, from inside the console and when attached to it.
- So PC Companion reads a console's input method from the console host process (`conhost.exe`), which is a child process of the console program, and from its thread that runs the console window: in the spike, the user's own switch (Left Alt + Shift) changed one of its threads, and a posted request all of them.
- Finding the console host lists processes and threads (`CreateToolhelp32Snapshot`), which took 17 to 30 ms in the spike, so it runs on the thread pool once per console window and is kept until the window closes. Until it's found, the console's input method can't be read: Remember doesn't record it, and the request is posted without the read before it or the notice after it.
- Switching a console posts the request to its window, as for any app; the spike confirmed it with a typed key.
- Windows Terminal is an ordinary app, with its tabs in one window, so all its tabs share one language. It's tested by hand.

## Remembered languages

- The host keeps, for each app, the identifier of the input method it had last, in memory and in `input-languages.json` in the data folder, which only the host writes. Apps are listed by app ID or program path, in the order they were last used, without times. When there are more than 500, the one used longest ago goes.
- An app with neither an app ID nor a program path is remembered for the session only.
- The file is read on the thread pool when the feature starts; until it's read, Remember switches nothing. It's written on the thread pool 5 seconds after a change, and when the feature stops. When the host exits, it waits up to a second for that write.
- A file that can't be read, for example after a hand edit, is ignored with a warning in the log and replaced at the next write. Each entry is checked as the settings are: an identifier that doesn't parse, or an entry with neither or both an app ID and a program, is left out.
- **Forgetting:** turning the feature off, or choosing another behavior than Remember for other apps, forgets every remembered language and deletes the file.
- **Running as administrator:** while the host runs as administrator after a UAC prompt, it neither reads, writes nor deletes the file, since a program without admin rights could put a link in its place; see [Always run as administrator](../general/run-as-administrator.md#between-the-host-and-other-programs). Remembered languages then last for the session only, and a file to delete is deleted the next time the host runs without admin rights. With UAC off, the file is used as usual.

## The notice

- **Show a notice when the language changes,** on by default, shows a notice when PC Companion switched the app in front and the read confirmed it. It shows nothing for switches the user makes, for which Windows shows its own switcher, or when the app already had the right input method.
- It's Lock Keys' notice: the same box, colors, fade, high contrast and right-to-left layout, at the bottom center of the display of the window in front, for 2 seconds. Its icon is Keyboard (`E765`) from Segoe Fluent Icons on Windows 11 and Segoe MDL2 Assets on Windows 10.
- **Text:** the language's name, and for a keyboard layout the layout's name on a second line, as Windows' switcher shows them, such as "English (United States)" and "US-International". A language with IMEs shows its name only, since which IME it got can't be read from another process.
- The notice code moves from Lock Keys' folder to one of the module that both features use, as `FullScreen` is. The two features share one notice window, so a notice from either replaces the other's, and shows next to Lock Keys' Caps Lock badge as Lock Keys' own notices do.
- It doesn't show while an app is full screen on that display, with Hot Corners' rules, as Lock Keys' notice doesn't. A skipped notice logs why at Debug level.
- Names for the notice are read on the thread pool when the feature starts and when the user's list of layouts changes, and kept. A switch to an input method whose name isn't read yet shows the notice once the name is.

## Apps running as administrator

- Windows keeps the windows of apps running as administrator away from programs that don't run as administrator. In the spike, with a copy labeled to run without admin rights, posting the request to such an app's window failed with error 5, while reading its input method worked.
- So without admin rights, such an app keeps what Windows gives it: with Windows' setting off, the language in use. The refusal is logged at Debug level, and nothing is tried again until the app next comes to the front. No notice shows, so a failed switch isn't noisy.
- Their input method is still read, so Remember records it.
- With Always run as administrator, or with UAC off, they switch like any other app. No elevated helper is started for this.
- The page's note says which apps can't be switched, and follows the host's rights, read from its process when the page opens, as App Windows' and Hide Apps' notes do.

## Windows' own setting

- PC Companion reads **Let me use a different input method for each app window** only for the page (`SystemParametersInfo` with `SPI_GETTHREADLOCALINPUTSETTINGS`), and never changes it. The host behaves the same with it on or off.
- The page shows whether it's on, explains that it decides what apps without a rule do with **Keep the language in use**, and has **Open** for Windows' **Typing** settings (`ms-settings:typing`), where **Advanced keyboard settings** has it: on Windows 11 build 26100, `ms-settings:keyboard-advanced` opened **Language & region** instead.

## Threads

- Input Language per App has no thread of its own: an app switch takes well under a millisecond, so it runs on the host's UI thread, like App Windows' foreground hook and Lock Keys' check. See [Threads and processes](../../architecture/features.md#threads-and-processes).
- **The host's UI thread** runs `Apply`, the foreground hook (`WinEventHook`, out of context), reading the app in front and its input method, the request, the confirming reads and the once-a-second read on a coalescable timer (`CoalescableTimer`), and the notice.
  - Reading the input method took 1 to 3 µs in the spike, 12 µs for Settings; posting the request 30 to 600 µs; reading the app's identity about 40 µs for each process, as App Windows measured.
  - The notice takes 1 to 3 ms, as Lock Keys'.
  - A request that comes later because the UI thread was busy still comes after Windows' own change, so it still holds; only keys typed meanwhile go in the language in use.
- **The thread pool** reads the user's layouts' identifiers and names from the registry, finds console hosts, and reads, writes and deletes `input-languages.json`. It passes the results back with `MessageWindow.BeginInvoke`. The list of layouts is read again when `GetKeyboardLayoutList` returns other handles than before, which the host checks at each app switch.
- Nothing waits for another program: reading sends no messages, and `PostMessage` returns at once. The host's `SlowHandlerWatch` times these handlers like every other.
- **Stopping** removes the hook, the timer and the notice, and leaves every app's input method as it is.
- **Failures:** a failure in a handler is logged and stops the feature until the host restarts, like a failing `Apply`; the other features keep running.

## Spike results

A spike in Windows Sandbox on Windows 11 24H2 (build 26100), with English (United States) with the US and US-International layouts, German, Japanese and Chinese (Simplified) with Microsoft Pinyin and Wubi, confirmed:

- **The Sandbox's languages stay in it:** a watcher on the PC that ran it, reading 10 times a second, saw no change in its layouts, its apps' input methods, its lock keys or its keyboard settings in the registry during four runs, one with the Sandbox's window in front the whole time. So the Sandbox checks may add languages and switch them.
- **The request** switched Windows Forms and WPF test apps, Character Map, File Explorer, Task Manager, Settings, Edge and a console within 50 ms, as a typed key showed: the key that types y in US English types z in German. WPF didn't hang, and File Explorer didn't switch back within 3 seconds. Posting took 30 to 600 µs, and reading 1 to 3 µs, 12 µs for Settings.
- **Windows' setting off:** as an app came to the front, Windows gave it the language in use within 20 ms. A request from an out-of-context foreground hook, about 16 ms after the change, came after it and held in 8 of 8 switches. The hook sometimes came twice. At the hook, the app being left still read its own input method. With Windows' setting on, changed only in the Sandbox, Windows didn't switch.
- **Apps running as administrator:** from a copy labeled to run without admin rights, the request failed with error 5, and reading worked. Between two apps without admin rights, the switch worked.
- **The text input framework:** from another process, `ActivateProfile` with `TF_IPPMF_FORSESSION` did nothing for keyboard layouts and switched an IME for every app in the session, from a copy without admin rights too. Creating it took 5 to 6 ms and listing the enabled profiles 2 to 5 ms; the list included an entry of language 0.
- **IMEs:** a request for Chinese gave the IME used last, Wubi after Wubi and Pinyin after Pinyin. US-International had its own handle, `0xF0010409`, which maps through `Layout Id` to `00020409`; the text input framework returned it sign-extended.
- **Consoles:** the console program's thread read no input method, and `GetConsoleKeyboardLayoutName` failed. The console host's threads changed with each request, and the user's own Left Alt + Shift changed one of them. Finding the console host took 17 to 30 ms.
- **IME modes:** from outside, an open Chinese IME read as open, but its mode read the same after Shift changed it, and keys sent to the Japanese IME didn't turn it on.
- **Registry:** requests wrote nothing. The Chinese IME wrote its own settings when first used, and Left Alt + Shift wrote one value of Windows' switch notice. The user's language list never changed.
- **Not checked yet:** which of the console host's threads runs the console window; Windows Terminal and Notepad, which aren't in the Sandbox; a switch with a real keyboard; UAC on; a Japanese IME that's on; Narrator; and Windows 10.

## Settings

- **Feature page:** the on/off switch; a note when the user has fewer than two input methods, with **Open** for **Language & region** (`ms-settings:regionlanguage`); **Apps**, the app list with **Running app**, **Program file** and, in each row, an input method list and **Remove**; **Other apps**, with **Remember each app's language**, **Keep the language in use** and **Use one language**, which shows its own input method list; **Show a notice when the language changes**; **Windows' own setting**, with its state and **Open**; a note about apps running as administrator; and a **Permissions & data** card.
- The app list is the one Hot Corners and Minimize to Tray use, with an input method list in each row, named "Language for <app>" for screen readers, before **Remove**. After **Add**, the focus goes to the new row's list, which starts with the first input method in the user's list.
- The input method lists show Windows' names, and are read when the page opens.
- The switch on Home changes only `enabled` and keeps the other settings.
- A section that can't be read, for example after a hand edit, turns the feature off with a warning in the log. Listed apps are read as Hot Corners reads its list: a blank app ID or program counts as none, and an app with neither or both, or a `null`, is left out. An input method identifier that doesn't parse leaves its rule out, with a warning. An unknown behavior for other apps is Remember.

```json
"features": {
  "inputLanguages": {
    "enabled": true,
    "apps": [
      { "name": "Telegram", "program": "C:\\Users\\Me\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe", "inputMethod": "0407:00000407" },
      { "name": "Terminal", "appId": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "inputMethod": "0409:00000409" }
    ],
    "otherApps": "Remember",
    "otherAppsInputMethod": null,
    "showNotice": true
  }
}
```

- `otherApps`: `Remember`, `Keep` or `Use`. `otherAppsInputMethod`: the identifier for `Use`, otherwise `null`.

## Not included

- **The modes of IMEs,** such as Japanese Hiragana or the Chinese IME's English mode: the spike couldn't read them from another process, and setting them needs undocumented commands that wait for the app. Later, with a spike and gated by Windows version.
- **Choosing one of two IMEs of one language:** a request chooses only the language, and the text input framework's way switches every app.
- **A language per window, tab, document or website:** Windows' own setting keeps one per window, and websites would mean reading addresses.
- **Guessing the language from what the user types,** which means reading every key.
- **Apps running as administrator without admin rights,** which would need the elevated helper to post every switch.
- **A notice for the user's own switches,** for which Windows shows its switcher; and a choice of where the notice shows and for how long.
- **An indicator at the text cursor,** which many apps don't report.
- **Switching the shell,** such as Start and Search, the lock screen, the sign-in screen and UAC prompts.
- **Changing Windows' setting, the user's language list or the default input method.**

## Compatibility

- Windows 10 version 2004 and later, and Windows 11.
- The APIs are documented and in every supported version: `GetGUIThreadInfo`, `GetKeyboardLayout`, `GetKeyboardLayoutList`, `PostMessage` with `WM_INPUTLANGCHANGEREQUEST`, `SetWinEventHook` with `EVENT_SYSTEM_FOREGROUND`, `CreateToolhelp32Snapshot`, `GetLocaleInfoEx`, `SHLoadIndirectString`, `SystemParametersInfo` with `SPI_GETTHREADLOCALINPUTSETTINGS` and `ITfInputProcessorProfileMgr`, plus those of Lock Keys' notice and Hot Corners' full-screen check.
- The registry's list of keyboard layouts and its `Layout Id` values aren't documented by Microsoft, but haven't changed since Windows NT, and .NET's `InputLanguage` reads them the same way. A handle whose layout can't be found there is left out of the lists, with a Debug entry, and rules for it are skipped.
- Reading a console's input method from the console host's threads relies on how the console host works, not on what Microsoft documents. When the console host isn't found, as it wouldn't be if a later Windows changed how it's started, consoles are switched without the reads before and after, so without a notice, and Remember skips them.
- The spike ran on Windows 11 only, so Windows 10 is tested by hand.

## Security and privacy

- **Reads:** when the window in front changes, and once a second while an app whose language is remembered is in front, which window is in front, its owner, whether the taskbar shows it, its app's identity (app ID, package and program path), and the input method of the thread with the keyboard focus; for consoles, the console host's process and threads. The user's input methods and their names, from Windows. On the page, whether Windows' per-window setting is on. It reads no window titles or contents and never receives keys: no keyboard hook, no raw input.
- **Changes:** the input method of the app in front, with the request Windows posts when the user switches, and only to an input method in the user's list. It never adds or removes input methods, never changes Windows' settings, the user's language list or the text input framework's profiles, runs no code in other apps and sends them no input.
- **Remembered languages:** which input method each app had last, for up to 500 apps, by app ID or program path, which can name the user, in the order they were used, without times. Kept on this PC in the data folder; turning the feature off or choosing another behavior deletes them, and uninstalling removes them with the rest of the data folder. Not read or written while the host runs as administrator after a UAC prompt.
- **Settings and the file:** `settings.json` and `input-languages.json` can only choose input methods from the user's list for apps, so a changed file can't make PC Companion do more.
- **Logs:** Debug entries for each switch: whether it came from a rule, a remembered language or the one language for other apps, how long it took, and whether it held, was refused or didn't take; when a console host is found and how long it took; and how many remembered languages were read. Warnings when the section or the file can't be read or an identifier doesn't parse. Never app IDs, names, paths or input methods.
- **Network:** none. `PRIVACY.md` gets a line on Input Language per App.
- **Rights:** runs without admin rights and needs none. Without them, apps running as administrator aren't switched. With Always run as administrator, it switches them too, and the file is handled as in [Remembered languages](#remembered-languages).

## Performance

- Between app switches, only the once-a-second read runs, a few microseconds on a coalescable timer, and only while an app without a rule is in front and other apps are remembered.
- An app switch reads the app and its input method and may post a request: well under a millisecond on the UI thread, plus 1 to 3 ms for the notice.
- Finding a console's host takes 17 to 30 ms on the thread pool, once per console window. Reading the layouts' names and writing the file are on the thread pool too.

## Accessibility and localization

- **Screen readers:** the notice isn't announced, and never takes the focus; its window has its text as its name, as Lock Keys' has. Whether Narrator says the language change itself is a hand test; an announcement is added only if it doesn't, so nothing is said twice.
- The notice follows the display's scale, Windows' text size and high contrast, and is mirrored in right-to-left languages, as Lock Keys' is.
- **Names:** input methods show with Windows' names in Windows' display language, as its switcher shows them. The page's own strings are translated like the other pages', marked "Needs native review."
- The page works with the keyboard alone, and every list and switch has a label for screen readers, including each row's "Language for <app>".

## Testing

- **Unit tests,** with fake windows, a fake clock and fake Windows calls:
  - the app in front: a window with a taskbar button, a dialog of one, shell windows that aren't apps, and going to the shell and back staying in the same app;
  - rules, Remember, Keep and Use one language: which input method each app gets, an app with nothing remembered, a hand switch holding until the user leaves, a new window of the same app getting its current input method, and the foreground event coming twice;
  - the request: asked only when the input method differs, confirmed within 500 ms with the notice, not taken, and refused with error 5 without a notice;
  - consoles: read through the console host once it's found, and switched without reads or a notice until then;
  - identifiers and handles: keyboard layouts, `F` handles through `Layout Id`, sign-extended handles, a language with IMEs, an input method that left the user's list, and identifiers that don't parse;
  - remembered languages: the order, the limit of 500, the write after 5 seconds, a file that can't be read, entries left out, forgetting, and no file while running as administrator after a UAC prompt;
  - the settings: defaults, the spec's example, unknown behaviors and rules left out.
- **Sandbox checks** (`scripts/sandbox/groups/InputLanguages.ps1`), which add German, US-International and Japanese inside the Sandbox, as the spike showed that its languages stay in it:
  - before they're added, the page's note about fewer than two input methods, and its **Open**;
  - the Home switch turns the feature on with its defaults, and the page lists the input methods by Windows' names, saves a rule's input method, the behavior for other apps and the notice switch, and shows Windows' setting with its **Open**;
  - a rule switches a test app when it comes to the front, as a typed key shows, and the notice shows the language's name, in the Windows mode's colors, light and dark, and in Arabic, with screenshots;
  - Remember gives a test app back the input method it had after another app was in front, also after the host restarts; Keep switches nothing; Use one language switches an app without a rule;
  - with Windows' setting on, changed only in the Sandbox, rules and Remember work the same;
  - File Explorer, Settings and a console window switch;
  - a borderless window covering the display skips the notice, and the host logs why;
  - with the host's file labeled to run without admin rights, as the spike did, a test app running as administrator is left as it is, and the host logs the refusal;
  - turning the feature off deletes `input-languages.json` and switches nothing more;
  - switching adds nothing to the user's language list, and no feature holds the host's UI thread, as every group checks.
- The App group checks the page's layout at several window widths, and its header at the largest text size, as for every feature page.
- **What the Sandbox can't check:** Windows Terminal and Notepad, which it doesn't have, UAC on, a real keyboard, IMEs that are on, Narrator and Windows 10.
- **By hand:** on a PC or virtual machine with two or more input languages, since switching changes the PC's state:
  - rules and Remember with a real keyboard, also with Windows + Space and Left Alt + Shift, and after signing out and in;
  - Windows Terminal, including whether a switch changes its zoom, which older versions did; Notepad; a browser with two profiles and an installed web app; Office; and a full-screen game;
  - a Japanese and a Chinese IME, turned on, and a Korean one;
  - an app running as administrator with UAC on, with and without Always run as administrator;
  - Narrator after a switch, high contrast, a right-to-left language and several displays;
  - Windows 10.
