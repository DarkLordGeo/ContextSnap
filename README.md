# ContextSnap

A small, local-only Windows app that captures a compact Markdown snapshot of what you are working on (open windows, Git repositories, File Explorer folders, running developer tools, Desktop items, optionally browser tabs) so you can paste it into any AI chat. It answers "what am I doing right now, and what would an assistant need to know?" without you typing it out.

ContextSnap has no AI integration, network access, telemetry, or accounts. A snapshot only leaves the app when you copy it to your clipboard.

This README describes the Windows app. A Linux command-line version is described in [Linux](#linux). Downloads for both are published as [GitHub Releases](https://github.com/DarkLordGeo/ContextSnap-Releases/releases) (see [docs/RELEASING.md](docs/RELEASING.md)), and the static website is in [`website/`](website/). A macOS menu-bar app with the same privacy model lives in [`macos/`](macos/). CI builds and tests it, but it is not released yet because it is not notarized by Apple.

## Features

- **Capture → review → copy.** One button (or `F5` / `Ctrl+Enter`) collects context and shows an editable Markdown preview. `Ctrl+Shift+C` copies it.
- **Global shortcut.** `Ctrl+Alt+Shift+C` (configurable) works from any application while ContextSnap runs in the background. It always captures and copies the complete Markdown snapshot straight to the clipboard, with no window or review step; redaction and output limits still apply.
- **Copy notifications** (on by default). After every copy, a small animated notice appears in the bottom-right corner: "Context copied to clipboard" with character, word, and size counts, a warning when the snapshot was truncated, or "Copy failed" with the reason. See [Copy notifications](#copy-notifications).
- **Runs silently in the background** (on by default). At sign-in ContextSnap starts with no window, taskbar button, notification-area icon, console, or startup notification. It just waits for its configurable shortcut (default Ctrl+Alt+Shift+C). Opening ContextSnap again shows its window; closing the window returns it to the background. Only one copy ever runs. See [Background operation](#background-operation).
- **Visible screen text** (on by default, focused window only). The text you can currently see in the window you are working in, such as a ChatGPT or Claude conversation, a document, or a dialog, read through Windows UI Automation with an on-device Windows OCR fallback. See [Visible screen text](#visible-screen-text).
- **Collectors** (each can be turned on or off):
  | Collector | What it records | Default |
  |---|---|---|
  | Open windows | Titles and process names of Alt+Tab-style windows, most recent first; marks the foreground window | On |
  | Git repositories | Branch, upstream ahead/behind, staged/unstaged/untracked counts, changed file names, last 3 commits | On |
  | File Explorer folders | Paths of open Explorer windows and tabs | On |
  | Developer tools | IDEs, terminals, runtimes from an editable list, with PIDs and executable paths | On |
  | Desktop items | Names and sizes of non-hidden Desktop entries, newest first | On |
  | System summary | Windows version, architecture, CPU count, memory | On |
  | Browser tabs | Tab titles from Chrome, Edge, Brave, Vivaldi, Opera, Firefox, Arc, Thorium; optionally the active tab's address. Browsers listed under excluded programs are never read | **Off** |
- **Privacy controls:** secret and email redaction, user-identity hiding, URL query stripping, excluded programs / title keywords / folders, custom redaction words, item and output limits.
- **Clear status:** each collector reports a count, "none", "off", "unavailable", or "failed" (hover a chip in the status bar for details). One failing collector never blocks the rest.
- **Command line:** `ContextSnap --capture` (capture + copy + summary) and `ContextSnap --print` (Markdown to stdout).

## Requirements

- Windows 10 or 11 (verified on Windows 11 x64; Arm64 and Windows 10 untested)
- To run a published build: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- To build: .NET 10 SDK (WPF is included in the SDK on Windows; no extra workloads)
- Optional: Git on `PATH` for the Git collector

No administrator rights are required or requested.

## Build, test, run

```powershell
dotnet build
dotnet test
dotnet run --project src/ContextSnap
```

Publish a framework-dependent Release build (about 0.5 MB, needs the .NET 10 Desktop Runtime):

```powershell
dotnet publish src/ContextSnap -c Release -o publish/ContextSnap
```

Or a self-contained single file that needs no runtime (larger download):

```powershell
dotnet publish src/ContextSnap -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/ContextSnap-standalone
```

### Command line

```powershell
ContextSnap.exe --capture   # capture, copy to clipboard, print a one-line summary (exit 0 = ok, 1 = failed, 2 = bad option)
ContextSnap.exe --print     # write the Markdown to standard output; the clipboard is not touched
ContextSnap.exe --help
```

In command-line modes the current directory is also checked for a Git repository. ContextSnap is a GUI executable, so when run from PowerShell without redirection the output can appear after the next prompt; piping (`ContextSnap.exe --print | Out-File snap.md`) waits for completion.

## Linux

`contextsnap` is a command-line app for 64-bit Linux. Each capture redacts the snapshot, copies the Markdown to the clipboard and shows a desktop notification. It has no window.

```sh
tar -xzf ContextSnap-<version>-linux-x64.tar.gz
./contextsnap-<version>-linux-x64/contextsnap --install   # once: Ctrl+Alt+Shift+C then works from every login
contextsnap            # capture, copy, notify (exit 0 = ok, 1 = failed, 2 = bad option)
contextsnap --print    # write the Markdown to standard output; the clipboard is not touched
contextsnap --uninstall
```

`--install` copies `contextsnap` to `~/.local/bin` and sets up Ctrl+Alt+Shift+C to work from every login:

| Desktop | What `--install` does |
|---|---|
| GNOME (X11 or Wayland) | Adds a custom keyboard shortcut named ContextSnap (Settings → Keyboard → Custom Shortcuts). GNOME listens for the key itself, so nothing has to keep running. Your other shortcuts are kept. |
| Other X11 desktops (KDE, Xfce, Cinnamon, MATE, …) | Adds `~/.config/autostart/contextsnap.desktop` and starts `contextsnap --background` now. At each login the desktop starts it again. It waits for the key with an X11 key grab, so it receives only that key combination and no other keyboard input. One instance runs per user. |
| Other Wayland desktops (KDE Plasma, Sway, Hyprland, …) | Wayland does not let apps register a global shortcut. `--install` prints the command to add as a shortcut in your desktop's keyboard settings. |

`--uninstall` stops the background instance and removes the shortcut, the autostart entry, and `~/.local/bin/contextsnap`. Your settings are kept. Run `--install` again after downloading a new version.

The download is a self-contained single file, so you don't need to install .NET.

| Needs | For |
|---|---|
| `wl-clipboard` (Wayland) or `xclip` / `xsel` (X11) | Copying to the clipboard |
| `wmctrl` (and `xprop`, usually installed) | The open-windows list |
| `notify-send` (libnotify) | Copy notifications (optional) |
| `git` | The Git collector (optional) |

What it collects:
- open windows, with the active window first;
- running developer tools;
- Git repositories in the current directory and in the configured project folders;
- Desktop items;
- a system summary (distribution, kernel, desktop session, CPU count, memory).

It uses the same exclusions, redaction, limits and Markdown as the Windows app.

What is Windows-only:
- **Visible screen text, browser tabs and File Explorer folders.** These are listed as not collected.
- **Clipboard-history exclusion.** A clipboard manager you run may keep a copy.

On Wayland, there is no common way for an app to list other apps' windows, so only X11 apps running through XWayland are listed. The snapshot says so.

Settings are read from `~/.config/ContextSnap/settings.json` (or from `$XDG_CONFIG_HOME`). It uses the same format as the Windows `settings.json`. Copy that file over or edit it by hand.

Build and test from source with the .NET 10 SDK:

```sh
dotnet test tests/ContextSnap.Linux.Tests
dotnet publish src/ContextSnap.Linux -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux
```

## Start with Windows

The **Settings → Behavior → Start ContextSnap in the background when I sign in to Windows** option (on by default) manages a single shortcut:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\ContextSnap.lnk
  Target:            <folder of the running copy>\ContextSnap.exe
  Arguments:         --startup
  Working directory: <folder of the running copy>
  Run:               minimized
```

- It is created or removed when you change the setting, and re-checked every time the window opens, so moving the app folder repairs the shortcut on the next launch. The status line under the option shows the exact shortcut path and target, or the reason it failed.
- No administrator rights, registry Run keys, scheduled tasks, or services are used. ContextSnap only creates, updates, or deletes a `ContextSnap.lnk` that points to ContextSnap; a same-named shortcut for another program is left alone and reported.
- `--startup` starts ContextSnap in the background with no window, taskbar button, or icon (see [Background operation](#background-operation)). If ContextSnap is already running, a `--startup` launch exits silently; a normal launch brings the existing window to the front (or flashes its taskbar button when Windows blocks focus changes).
- If you turn the item off in **Windows Settings → Apps → Startup** (or Task Manager → Startup apps), Windows' choice wins. ContextSnap reads that switch (without changing it) and tells you when the item is turned off there.
- Command-line modes (`--capture`, `--print`) never change startup registration.

**Published build vs. `dotnet run`.** The shortcut always points to the `ContextSnap.exe` launcher in the folder of the copy you opened most recently:

| How you start ContextSnap | Shortcut target |
|---|---|
| Published build (`publish\ContextSnap\ContextSnap.exe`) | That `ContextSnap.exe` |
| `dotnet run --project src/ContextSnap` | `src\ContextSnap\bin\<Debug or Release>\net10.0-windows\ContextSnap.exe` (the build's launcher, not `dotnet.exe`); the status line notes it is a development build |
| `dotnet ContextSnap.dll` in a folder without the launcher | `dotnet.exe "<folder>\ContextSnap.dll" --startup` |

During development the option is also on by default, so running from source re-points the shortcut at your build output. Before relying on it, open the published copy once (or turn the option off while developing). A `dotnet clean` leaves a shortcut to a missing file until ContextSnap is opened again.

Debug, Release, and published copies share one single-instance lock per Windows session.

## Background operation

After sign-in ContextSnap is a quiet background process (`ContextSnap.exe` in Task Manager → Details/Background processes). It shows nothing until you use it:

- **Nothing visible at startup:** no window (only a hidden window handle that receives the shortcut), no taskbar button, no console (it is a Windows-subsystem app), no notification-area icon unless you turn on **Settings → Behavior → Show a ContextSnap icon in the notification area**, and no splash screen or startup toast.
- **Shortcut:** **Settings → Behavior → Shortcut** (default `Ctrl+Alt+Shift+C`; Ctrl, Alt, or Win, optionally Shift, plus a letter, digit, or F1–F24). It works while any other application is focused. If another app already owns the combination, the setting says so and you can pick another.
- **On demand only:** context is collected only when you press the shortcut (or click Capture). Between presses the process is idle: it waits on Windows messages and two named events (no timers or polling), registers its shortcut once, and writes nothing to disk except your settings when you change them and the diagnostics log on errors. After each shortcut capture it returns the capture's temporary memory to Windows.
- **What the shortcut does:** it captures and copies the complete Markdown (the same document the preview shows, after redaction and output limits) and shows the small copy notification if that is on. It never opens a window, dialog, or review step, including when visible screen text is captured. The window's preview is updated in the background, so opening ContextSnap afterwards shows the last snapshot.
- **When something fails:** the shortcut still opens nothing. A capture or clipboard failure shows the small error notification (if notifications are on), appears in the window's status line the next time you open it, and is written to `%LOCALAPPDATA%\ContextSnap\logs\contextsnap.log`. That log is written only on errors, holds error types and messages (secrets redacted, never snapshot contents), and is capped at about 256 KB plus one previous file. **Open diagnostics log** in Settings opens its folder. Pressing the shortcut again while a capture is still running is ignored.
- **Window:** opening ContextSnap (Start menu, the `.exe`) shows the window of the running copy; closing it returns ContextSnap to the background while the shortcut is on. If the shortcut is off or unavailable (and no icon is shown), closing the window quits instead.

**Controls**

| Goal | How |
|---|---|
| Quit the running process | **Quit ContextSnap** button in the window, `ContextSnap.exe --exit`, the icon's **Exit** (if shown), or **End task** in Task Manager |
| Stop starting at sign-in | Uncheck **Start ContextSnap in the background when I sign in**, or run `ContextSnap.exe --disable-startup` |
| Start at sign-in again | Check that option, or run `ContextSnap.exe --enable-startup` |
| Start it now without a window | `ContextSnap.exe --startup` |

The process is never hidden from Task Manager. Quitting (or signing out) unregisters the shortcut and removes the optional icon. A second launch never creates a duplicate: `--startup` exits silently, and a normal launch shows the existing window.

**Verified on this PC (Windows 11, published Release build):** a `--startup` launch had no visible top-level window, no console host, and no taskbar or notification-area button; a second `--startup` left one process. The shortcut, pressed while another app was focused, put the complete snapshot (visible text and system context) on the clipboard with no window, also for three rapid presses. Closing the window kept the process running hidden. `--exit` ended it and the shortcut became free again, and `--disable-startup` / `--enable-startup` removed and recreated the Startup shortcut. **Not verified:** an actual sign-out/sign-in cycle (the Startup-folder shortcut was launched directly instead), a Windows restart, and a capture failure on a real desktop (the error path is covered by unit tests).

**Measured idle cost** (published Release build, Windows 11, 12 logical processors; `--startup` launch, 8 s settle, then 60 s of idle measured with the process's total CPU time, working set, private bytes, thread and handle counts):

| State | CPU in 60 s idle | Working set | Private bytes | Threads | Handles |
|---|---|---|---|---|---|
| Just started, never captured | 0–16 ms (≤ 0.002% of all cores) | 98 MB | 55 MB | 10 | 602 |
| After 15 shortcut captures | 0 ms | 147 MB | 109 MB | 15 | 711 |

Before this change, 15 shortcut captures left 126 MB private bytes (and the old review flow 99 MB private plus 906 ms of CPU per idle minute, because its review window stayed open). Further rounds of 15 captures raised private bytes to 122, 132, and 137 MB with a stable handle count (about 710–770) and GDI/USER objects (26/21), so the growth slows; it has not been shown to stop completely. Most of the memory is the .NET/WPF runtime, UI Automation, and the Windows OCR engine that is loaded on first use.

**Manual check after installing:** sign out and back in; confirm there is no ContextSnap window, taskbar button, or icon, and that Task Manager shows one `ContextSnap.exe`. Then focus another app, press the shortcut, and paste.

## Visible screen text

ContextSnap reads the text that is on screen **only when you capture** (button, shortcut, or `--capture`). Nothing runs in the background, and nothing is stored after the snapshot is built.

**Modes** (Settings → Visible screen text):
- **Focused window only** (default): the window you are using. If ContextSnap itself is in front (you clicked Capture), the window you used just before it.
- **All visible windows**: the focused window plus other windows that are at least partly visible (covered or minimized windows are skipped), up to the window limit (default 4).

**How text is read** (per window, each with its own timeout, default 5 s):
1. **UI Automation**, the Windows accessibility interface used by screen readers. ContextSnap walks the window's accessible elements and keeps those that are on screen (not reported as off-screen, inside the window, larger than 2 px), in reading order. Fragments on the same line are joined, container labels are dropped when their children carry the text, and consecutive duplicates are removed. Text controls without child elements (for example, Notepad or Word) are read through their *visible ranges*, which is exactly what is scrolled into view.
   - **Browsers** (Chrome, Edge, Brave, Vivaldi, Opera, Firefox, Arc, Thorium) and **Chromium/Electron apps** (VS Code, Claude, Slack, …): only the page document is read, never tabs, the address bar, bookmarks, or extensions. Inside pages, button and region labels are skipped because they are usually invisible accessibility labels ("Copy message").
   - **Skipped everywhere:** password fields (and their children), title-bar caption buttons, and tab titles (which name other files or pages).
2. **On-device OCR** (Windows.Media.Ocr, built into Windows) when an app exposes too little text: fewer than about 200 characters per megapixel of window (at least 20, at most 200). The window is rendered off-screen with `PrintWindow`, so text from overlapping windows is not mixed in. For browsers only the page area is captured. OCR lines are read column by column (a sidebar and a conversation do not interleave), and lines already read through UI Automation are not repeated. Pixel buffers are zeroed and released immediately after recognition.
   - OCR uses the OCR languages installed with Windows for your display languages. If none is installed, the snapshot says so and UI Automation text is still used.

**Output:** a **Visible text** section comes first (active window, then **Other visible windows**), each with its source (`UI Automation`, `OCR`, or both), character count, and any warnings, in a fenced block. The existing metadata follows under **System context**. Each window is limited to 4,000 characters by default (200–50,000); the overall output limit still applies. The status bar shows, for example, `Screen text: UI Automation 2,340 characters · OCR 680 characters · 2 windows · 1 warning`, and the copy notification says "Includes visible text from 1 window."

**Never done:** no clicking, scrolling, focus changes, or mouse movement to reveal more text; no cloud OCR; no browser extension or remote-debugging port; no persistence of captured text.

**Measured on this PC** (Windows 11, i7-class CPU, one 1920×1080 monitor at 100%; collector time, excluding process start):

| Window | Method | Characters | Time |
|---|---|---|---|
| ChatGPT conversation in Chrome | UI Automation | 1,578 | ≈ 0.5–1.0 s whole CLI run (incl. ~0.3 s process start) |
| YouTube page in Chrome | UI Automation | 1,322 | 313–396 ms |
| Claude desktop app (Electron) | UI Automation | 2,855 | 220–387 ms |
| Notepad (Windows 11) | UI Automation (visible ranges) | 390 | 69–159 ms |
| Standard message box | UI Automation | 47 | 2–64 ms |
| VS Code first run (accessibility not yet active) | UI Automation + OCR | 323 | 246 ms |
| All visible windows (2 windows) | UI Automation | 1,047 | 166–254 ms |
| Published `ContextSnap --print` end to end | — | — | 575–645 ms |

Chromium-based apps (including Electron apps such as VS Code and Claude) build their accessibility tree only after an accessibility client asks for it. The first capture may therefore fall back to OCR, and later captures read structured text. Having that tree enabled can add some CPU and memory use in those apps until they restart.

## Copy notifications

Every copy path shows the same notification: the **Copy** button, **Copy to clipboard automatically after Capture**, the global shortcut `Ctrl+Alt+Shift+C`, and `ContextSnap --capture`.

| Outcome | Notification | Shown for |
|---|---|---|
| Copied | ✓ **Context copied to clipboard** · `8,420 characters · 1,240 words · 8.2 KB` | 4 s |
| Copied but truncated | Same, plus an amber "Snapshot truncated to the 12,000-character limit." line | 5 s |
| Empty snapshot | ! **Nothing copied**: the clipboard is not touched | 4 s |
| Clipboard error | ✕ **Copy failed** with a short reason, and a brief shake | 6 s |

- It appears only after the clipboard write has finished, and never delays it. It shows only counts and fixed wording, never snapshot text, window titles, paths, or URLs.
- Words are whitespace-separated tokens containing a letter or digit (Markdown markers such as `-` or `#` are not counted). Size is the UTF-8 size of the copied text.
- The toast is a small always-on-top window that never takes focus, so typing continues uninterrupted. It sits at the bottom-right of the work area (above the taskbar) on the monitor of the window you are using, and uses physical pixels and per-monitor DPI.
- A new copy while it is visible updates the same toast and restarts its countdown; toasts never stack. Click it to dismiss early.
- **Motion:** it slides up with a gentle spring and fades in, the checkmark draws itself, the counts fade in one after another, and a thin bar along the bottom counts down. On dismissal it fades out and moves down slightly. Everything is opacity and transform animation; there are no blur effects.
- **Reduced motion:** when **Settings → Accessibility → Visual effects → Animation effects** is off, the toast only fades in and out, and the icon and counts appear instantly.
- **Theme:** it follows the Windows app mode (light or dark) and uses system colors in High Contrast. The main window keeps its light theme.
- `--capture` shows the toast only when its output is not redirected (for example, when run from a shortcut or an interactive console). The process then stays alive until the toast closes (about 4–6 s). Piped or redirected runs (`ContextSnap --capture > log.txt`) never show a toast and exit immediately.
- Turning off **Settings → Behavior → Show a notification when a snapshot is copied** stops all toasts; copying is unaffected. When notifications are off, the global shortcut flashes the taskbar button instead.
- If a toast cannot be displayed, the copy still counts as successful and the status bar says the notification failed.

Diagnostics: set `CONTEXTSNAP_REDUCED_MOTION=1` (or `0`) to force reduced (or full) motion, and `CONTEXTSNAP_TOAST_THEME=light` or `dark` to force a toast theme.

## Example output

```markdown
# Context Snapshot

_Captured 2026-10-14 19:31 (UTC+04:00) by ContextSnap. Sensitive values may be redacted._

**Foreground window:** Code — Program.cs - ContextSnap - Visual Studio Code

## Open windows (3, most recently used first)

- **Code**: Program.cs - ContextSnap - Visual Studio Code _(foreground)_
- **WindowsTerminal**: Windows PowerShell

## Git repositories (1)

### ContextSnap — `~\Desktop\ContextSnap`

- Branch: `master` → `origin/master` (ahead 1)
- Working tree: 2 unstaged, 1 untracked
- Changed files:
  - ` M` `src/ContextSnap/Program.cs`
  - `??` `docs/ARCHITECTURE.md`
- Recent commits:
  - `ed0064a` Apply redaction and output limits to snapshots (2 hours ago)

## File Explorer folders (1)

- `~\Desktop\ContextSnap\docs`

_Not collected (turned off): Browser tabs._
```

## Privacy model

**Never collected:** files read from disk, Git diffs, process command-line arguments, environment-variable values, password fields, cookies, browser history or profiles, clipboard contents or history.

**Visible screen text** (on by default for the focused window) can include anything shown in that window: messages, code, documents, or numbers. It is read only when you capture, kept in memory only for that snapshot, and redacted like everything else. The shortcut copies it immediately; capturing from the window shows it in the preview first. Turn it off, switch to focused-only, or add apps and window-title keywords under **Never read text from these apps or windows**. Password managers and private/incognito windows are excluded by default.

**Collected only as described above**, and then:

1. **Excluded before formatting:** windows and processes of excluded programs (password managers by default), windows and tabs whose titles contain excluded keywords (`InPrivate`, `Incognito`, `Private Browsing`, `Password` by default), and anything under excluded folders.
2. **Redacted in every text field** before it is written to Markdown:
   - key/value secrets (`api_key=…`, `password: …`, `GITHUB_TOKEN=…`, connection-string `Password=`/`AccountKey=`), `Authorization`/`Bearer`/`Basic` credentials, JWTs, private-key blocks, credentials in URLs, sensitive URL query parameters;
   - known token formats (GitHub, GitLab, Slack, AWS, Google, Stripe, OpenAI, Anthropic, npm, Hugging Face);
   - long random-looking tokens (32+ characters mixing upper/lower case and digits, with high entropy);
   - email addresses;
   - your user-profile path (becomes `~`), Windows user name (`[USER]`) and computer name (`[HOST]`);
   - any custom words you add.
3. **Bounded:** per-section item limits and a total character limit (default 12,000) with a visible truncation notice.
4. **Clipboard:** written as Unicode text through the Win32 clipboard API. By default the copy is marked to be excluded from Windows clipboard history and cloud clipboard sync.
5. **Notifications** display only counts (characters, words, size), the configured output limit, and fixed messages. They are built without access to the snapshot text, so they cannot reveal captured content. They are local windows, not Windows Action Center notifications, so nothing is stored in notification history.

**Redaction is best-effort.** It is pattern-based and can miss secrets in unusual formats or redact harmless text. Window titles, folder names, file names, and commit subjects can still reveal what you are working on. Review the preview — it is editable — before sharing.

ContextSnap makes no network requests. The only files it writes are its settings (`%APPDATA%\ContextSnap\settings.json`, and only after you change a setting) and, while **Start with Windows** is on, its own `ContextSnap.lnk` in your Startup folder (contents listed above; nothing captured is stored in it). It reads, but never changes, Windows' own on/off switch for that startup item (`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder`). Git is run read-only (see Architecture).

## Limitations

- **Terminal working directories are not collected.** Windows offers no documented, safe way to read another process's current directory, so ContextSnap does not try. Git context comes from open File Explorer folders, the "Project folders for Git" setting, and (command-line modes only) the current directory.
- **Editor/IDE project paths** are not read from IDE internals; the window title (for example `file - folder - Visual Studio Code`) is the signal.
- **Browser tabs** (opt-in) are read through UI Automation from each browser's tab strip. Only the *active* tab's address is available, and only when "Include the active tab's address" is on. Titles of tabs in minimized windows and some browser layouts may be missing. Tab-name status suffixes Chromium adds in English (memory usage, audio playing) are removed; other languages may keep them.
- **Git status** is read with every Git filter disabled (so a repository cannot run commands through filters) and without entering submodules. Files that rely on a filter, such as Git LFS files, can therefore show as modified when their timestamps changed, and changes inside submodules are not reported.
- **Explorer folders** showing virtual locations (Home, This PC, Recycle Bin) are counted but not listed.
- **Elevated processes** (run as administrator) may show without executable paths, and their windows still appear by title.
- The **global shortcut** works only while ContextSnap is running (after sign-in, or after you open it). Running in the background never captures anything on its own; there is no monitoring. A full sign-out/sign-in cycle and the optional notification-area icon have not been tried on a real desktop yet; the background launch, shortcut, close-to-background, and `--exit` were.
- **Visible screen text:**
  - Apps that expose little through UI Automation (games, canvas-rendered UIs, remote-desktop viewers such as AnyDesk or RDP, many video players) rely on OCR, which can misread small or stylized text and does not know reading order beyond column layout.
  - UI Automation cannot tell when a window is covered by another window, so in all-windows mode a partly covered window can contribute text that is hidden behind another window (OCR does not have this problem).
  - Some apps report scrolled-away content as on-screen. ContextSnap also checks element bounds, but very long virtualized lists may still include a little off-screen text.
  - Apps that block screen capture (DRM video, apps using `WDA_EXCLUDEFROMCAPTURE`) produce no OCR text.
  - Verified by hand on this PC: a ChatGPT conversation and YouTube in Chrome, the Claude desktop app, Notepad, a standard message box, VS Code's first-run screen (via OCR), and a two-window capture. Not verified: AnyDesk (not installed), a VS Code editor and terminal pane (the test window closed before they were opened), high-DPI or multi-monitor setups (single 100% monitor).
  - The Windows OCR projection adds about 25 MB to the published folder.
- **Copy notifications:** placement was verified on a single 1920×1080 monitor at 100% scaling. Other scaling levels, mixed-DPI multi-monitor setups, and taskbars on other edges are handled by the placement code and unit tests, but were not tried on real hardware. An always-on-top toast can still be covered by full-screen exclusive games or other topmost windows. A `--capture` toast and a toast from the running app are separate windows and can briefly appear together.
- The clipboard-failure toast was verified with the real toast window and the error content, but a real clipboard lock could not be reproduced on this PC (a copy succeeded even while another process held the clipboard open). The failure handling itself is unit-tested.
- **Start with Windows** was verified by opening the Startup shortcut the way Explorer does at sign-in, not by an actual sign-out/sign-in cycle.
- When a second launch asks the running copy to come forward, Windows may refuse to change focus (for example, when the launch came from a background process). The taskbar button flashes instead.
- **Function keys owned by other software:** if another program registers `F5` as a system-wide hotkey, Windows never delivers it to ContextSnap — use `Ctrl+Enter`.
- The interface is English-only and uses a light theme (it falls back to system colors in Windows High Contrast mode).

## Troubleshooting

| Symptom | What to do |
|---|---|
| Git section says "Unavailable: Git was not found on PATH" | Install Git for Windows or add it to `PATH`, then restart ContextSnap. |
| Git section says "No Git repositories found" | Open the project folder in File Explorer, or add it under **Settings → Project folders for Git**. |
| A repository shows "owned by another user (safe.directory)" | Git's own safety check refused the repository. ContextSnap intentionally does not override it; mark it safe yourself with `git config --global --add safe.directory <path>` if you trust it. |
| "Shortcut unavailable: another application is already using this shortcut" | Another app registered the same combination. Choose a different one under **Settings → Behavior → Shortcut**, or close the other app and toggle the shortcut off and on. |
| `F5` does nothing | Another program owns F5 system-wide. Use `Ctrl+Enter` or the button. |
| "Copy failed: The clipboard is being used by another application" | A clipboard manager or remote-desktop tool holds the clipboard. Wait a moment and click Copy again. |
| Browser tabs section is empty | Make sure the browser window is open (not only in the tray) and the window title is not matched by an excluded keyword. |
| A collector shows "failed" or "timed out" | Hover the chip in the status bar for details. Each collector has an 8-second limit (Git gets more). |
| "Could not create the startup shortcut …" | Shown under the option and in the status bar with the Windows error. Check that `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` exists and is writable (some managed PCs block it), then toggle the option off and on. |
| ContextSnap does not open at sign-in | Check the option's status line. If it says Windows has the item turned off, enable **ContextSnap** under Settings → Apps → Startup. If the shortcut points to a build folder that no longer exists, open the published copy once. |
| Double-clicking ContextSnap shows nothing new | It is already running in the background. The existing window is brought forward or its taskbar button flashes. |
| I want ContextSnap to quit, not keep running | Click **Quit ContextSnap** in its window, run `ContextSnap.exe --exit`, or end it in Task Manager. To stop it starting at sign-in, run `ContextSnap.exe --disable-startup`. |
| The shortcut does nothing | Another app may own it: open ContextSnap and check **Settings → Behavior** for "Unavailable", then choose another shortcut. |
| Visible text is empty or says "UI Automation could not open this window" | The app does not expose accessible text. Make sure OCR is on; if the note says no OCR language is installed, add one under Settings → Time & language → Language & region. |
| A browser page's text is missing | Capture again: Chromium enables its accessibility tree after the first request. Private/incognito windows are excluded on purpose. |
| Too much unrelated text | Use **Focused window only**, lower the per-window character limit, or add the app to **Never read text from these apps or windows**. |
| No copy notification appears | Check **Settings → Behavior → Show a notification when a snapshot is copied**. For `--capture`, toasts are skipped when output is redirected or piped. |
| Notification is hidden behind a game | Full-screen exclusive apps draw above always-on-top windows; the copy still happened. |
| Settings warning at startup | The settings file is not valid JSON. Defaults are used and the file is not overwritten until you change a setting. Delete `%APPDATA%\ContextSnap\settings.json` to reset. |

## Project layout

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

MIT. See [LICENSE](LICENSE).
