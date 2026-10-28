# ContextSnap TODO

## Completed (verified 2026-10-14 on Windows 11 x64)
- [x] Snapshot data model and collector interface (`ICollector<T>`, `CollectorResult<T>` with Ok/Empty/Disabled/Unavailable/Failed)
- [x] Visible application windows and process names (Alt+Tab-style filtering, foreground marker) — exercised
- [x] Open File Explorer folders via Shell COM on an STA thread — exercised with real windows, including a virtual location
- [x] Git context: branch, upstream, status summary, changed files, recent commits; read-only and bounded — exercised + integration test
- [x] Developer tools (allow-list, grouped, PIDs, executable paths; no command lines) — exercised
- [x] Desktop listing (names/sizes only, newest first, limits) — exercised
- [x] System summary (no machine/user names) — exercised
- [x] Browser tab titles via UI Automation, opt-in, active-tab URL opt-in — exercised with Chrome
- [x] Markdown formatter with escaping, per-section and total limits
- [x] Redaction (secrets, tokens, emails, identity, custom terms) and exclusions (programs, title keywords, folders)
- [x] Win32 clipboard writer (Unicode, CRLF, retry, excluded from clipboard history) — exercised, read back
- [x] WPF UI: capture, editable preview, copy, status chips, settings (auto-save), privacy tab, restore defaults — exercised
- [x] Global hotkey, default Ctrl+Alt+Shift+C and configurable (capture + copy, no window) — exercised
- [x] Command line: `--capture`, `--print`, `--help` — exercised
- [x] Settings file with normalization; written only when changed
- [x] Release build and framework-dependent publish (`publish/ContextSnap`)
- [x] README and docs/ARCHITECTURE.md
- [x] Start with Windows (Startup-folder shortcut, on by default) — enable/disable/repair and shortcut launch verified; actual sign-in not performed
- [x] Silent background mode: `--startup` shows no window, taskbar button, console, or icon; closing the window keeps the process running; `--exit`, `--enable-startup`, `--disable-startup` — verified on Windows 11 (actual sign-in not performed)
- [ ] Optional notification-area icon (off by default; Open / Capture snapshot / Exit) — implemented and unit-tested; NOT yet tried on a real Windows desktop
- [x] Animated copy notifications (bottom-right toast for button, auto-copy, hotkey, and `--capture`; success/truncated/empty/failure; reduced motion; light/dark) — verified on one 100% DPI monitor
- [x] Visible screen text (focused or all visible windows; UI Automation with on-device OCR fallback; browsers read page only; password/tab/title-bar skipping; limits, timeouts, exclusions) — verified on Chrome (ChatGPT, YouTube), Claude app, Notepad, message box, VS Code first-run screen (OCR), two-window capture
- [x] Single instance per session (second launch activates the first; `--startup` duplicate exits silently) — verified
- [x] Per-window screen text limit cuts an overlong line at a word boundary, so a token is never split before redaction (Windows + macOS; regression tests added) — Windows logic checked on Linux with a standalone run; test suite not run here
- [x] Release workflow publishes packages to the public releases repository, verifies anonymous downloads by SHA-256, and opens a PR that updates `website/downloads.json`; the website enables a button only for a GitHub Release asset URL (checked in headless Chromium with generated, empty, and invalid metadata)
- [x] Website redesign 3 (static HTML/CSS/JS, no dependencies, system fonts, CSP unchanged, strictly neutral palette with no green): centered headline above one split demo frame, a stationary keyboard on a desk (no tilt or pointer tracking; thin bezel, visible front face, dark switch plate, sculpted keycaps in wells, contact and ambient shadows, soft focus falloff away from Ctrl/Alt/Shift/C) beside a sample AI chat. A press squeezes and shades the cap instead of sinking it. Running the shortcut outlines the frame, sweeps white pixels around its perimeter with a short white wash, shows a status pill (capturing, then copied), then pastes the sample snapshot into the chat and shows a sample reply. Sound is always on after the first interaction (no toggle). Header is fixed (no band above the hero, anchors land below it), the nav collapses to a menu below 900px so it never wraps, five links, same header on the privacy page. Checked in the in-app Chromium at 1280x800 (whole demo above the fold), 1000, 860, and 375x812: the capture frame rendered with pixels, outline, and pill (animations seeked while the pane was hidden), chat end state, three rapid runs leave one snapshot message and no stuck keys, sticky modifiers, nav centering and collapse, menu, privacy header clearance, no horizontal overflow, JS syntax. Keyboard input was simulated with synthetic events only, never real key presses
- [x] Website redesign 4, one page of stacked sections (no separate demo or privacy page; `/privacy` redirects to `/#privacy` in `vercel.json`, untested locally). Homepage content shares one 960px width (header bar, keyboard-and-chat stage, downloads, privacy); the first screen is one viewport tall. Sample Windows 11 desktop (`os-demo.js`, `win11.css`) fills its own viewport: code editor with highlighting, PowerShell, File Explorer, System Information, and a sample AI chat with a model picker (letter marks for OpenAI, Anthropic, Google, xAI, DeepSeek, Meta; labeled as a demo, no model is called); windows open, close, minimize, maximize, drag; Start and tray menus; replay and manual capture: shortcut, capture effect, snapshot card into the tray, clipboard preview to inspect, paste into the chat as ContextSnap.md, question, answer built only from that snapshot (uses the latest terminal response). Pasting in the sample chat never reads the real clipboard. Capture effect (`capture-fx.js`, one canvas per run): pixel bars grow inward from the edges to irregular lengths (at most 16% of the shorter side), hold, shrink back the same way, charcoal-to-white and slightly transparent, while the content scales to 0.97 and back. Verified in the in-app Chromium with synthetic events and a virtual clock (the pane was hidden, so frames were stepped, not watched live): homepage and desktop flows, grounded answers, repeated and rapid captures with no leftover state, layout at 800-1920 wide and 375 wide with no horizontal overflow, CSS cleanup with computed styles compared before and after. Not verified: live playback, auto-start of the replay on scroll and the real shortcut on the desktop (IntersectionObserver does not fire in a hidden pane), real key presses, Safari, Firefox. Privacy: Google Analytics (after consent) and Vercel Web Analytics (always loaded, cookieless) run, but the privacy section does not mention them
- [x] Website: 3D transfer and context collector. The Windows demo is a centered frame (`min(90%, 1180px)`); a capture runs the pixel bars around the active window while it scales to 0.98, 3 to 5 sheets built from the snapshot's parts lift out in 3D, land on a small 3D clipboard, stack into ContextSnap.md, and the clipboard carries it into the chat as the attachment. A floating icon at the bottom right (`collector.js`) collects the homepage keyboard demo's captures (card flight, pixel-bar burst, badge) and opens them as cards with mock source app icons in a modal, blurred overlay with a detail view. The two demos each own a sample desktop (`createDesktop()` in `workspace-sim.js`): the Windows demo never reaches the collector, and resetting or replaying one never changes the other (checked both directions in the in-app Chromium). Verified with synthetic events and a virtual clock (the pane is hidden, so nothing was watched live); not verified: live playback, Safari, Firefox, real key presses

- [x] Git: repository filter drivers are disabled for every Git command and submodules are never entered (malicious-repository regression test, Windows and macOS)
- [x] Browser tabs honor excluded programs (never read) and share one browser list with visible text
- [x] Windows shortcut always copies the complete Markdown directly, never opens a window; older review settings are migrated; errors go to the notification and `%LOCALAPPDATA%\ContextSnap\logs\contextsnap.log` (capture failure covered by unit tests only). macOS keeps its review window.
- [x] Idle cost: no timers or polling, non-concurrent GC, memory released after each shortcut capture — measured 0 ms CPU per idle minute; private bytes 109 MB after 15 captures (126 MB before). Growth over repeated captures slows but is not proven to stop.
- [x] Truncation closes an open code fence before the notice (shared fixtures + property tests on both platforms)
- [x] Redaction regexes are linear on adversarial input (about 24 s → 4 ms for 50,000 characters of "a.a.a…"); performance-bound tests on both platforms
- [x] Integration tests that cannot run (no Git, no desktop, no OCR) are reported as skipped

## Linux command-line app (verified 2026-10-15 on Ubuntu 24.04 under Xvfb + Openbox; not tried on a real GNOME/KDE desktop)
- [x] `contextsnap` captures, redacts, copies, and notifies; `--print`, `--help`, `--version`
- [x] Open windows via wmctrl/xprop (active first, stacking order, UTF-8 titles, host dropped, exclusions) — exercised with xterm windows, password-manager title excluded, token and email in titles redacted
- [x] Clipboard via wl-copy / xclip / xsel on stdin — xclip round trip tested (also in CI under xvfb-run); `$(contextsnap)` does not hang
- [x] Developer tools from /proc (names and executable paths only), Git (same safety rules, shared Git tests pass on Linux), Desktop items, system summary (os-release, kernel, desktop session, memory)
- [x] Self-contained single-file `linux-x64` build runs without .NET installed; release job, CI job, website card
- [x] Start at login: `--install` / `--uninstall` (GNOME custom shortcut via gsettings; other X11 desktops: XDG autostart + `--background` with an X11 key grab, one instance per user) — background shortcut capture tested under Xvfb with xdotool; install/uninstall tested in a temp home (path with a space) and with the GNOME schema on the gsettings keyfile backend
- [ ] Try on GNOME and KDE (X11 and Wayland), with wl-copy and notify-send on a real session, including that GNOME picks up the new shortcut without logging out
- [ ] Wayland-native window listing (no common protocol; only XWayland apps are listed)
- [ ] linux-arm64 build

## macOS (built and unit-tested in CI; not released until notarized; not yet tried by hand on a Mac)
- [x] `swift test` and `./scripts/build.sh` run in CI on macOS 15
- [ ] Try the review window and Accessibility/OCR capture on a real Mac
- [ ] Verify the new window-title exclusions (InPrivate, Incognito, Private Browsing, Password by default) skip private browser windows

## Next
- [ ] Configurable hotkey
- [ ] Dark theme
- [ ] Installer/MSIX packaging and an application icon
- [ ] Automated UI smoke test
- [ ] One-time release setup (public `ContextSnap-Releases` repo, `RELEASES_TOKEN` secret, allow Actions to open PRs) and the first release; see docs/RELEASING.md. The release workflow and website handling are implemented but have not run against a real release yet
- [ ] Code signing (Windows) and Developer ID signing + notarization (macOS), then release macOS again (docs/RELEASING.md)

## Known limitations
- Terminal working directories are not collected (no documented, safe Windows API).
- Browser tabs: only the active tab's URL is available; English-only cleanup of Chromium status suffixes.
- Explorer virtual locations (Home, This PC) are counted, not listed.
- Arm64 and Windows 10 untested.
- If another app owns F5 system-wide, use Ctrl+Enter.
- Start with Windows follows the most recently opened copy, so running from source re-points it at the build output.
- Visible screen text not verified with AnyDesk, a VS Code editor/terminal pane, high-DPI, or multiple monitors; Windows OCR projection adds ~25 MB.
- Copy notifications not yet tried on multi-monitor, non-100% scaling, or non-bottom taskbars; real clipboard-lock failure not reproducible here.
- Website: the Vercel project's Root Directory must be set to `website` in the dashboard (not verifiable from the repository).
- Website demo: sounds are synthesized (Web Audio), not recorded, and were not judged by ear here. Not tested: real hardware keystrokes, Safari/Firefox, a real touch device, screen readers, and an actual prefers-reduced-motion setting (that path was reviewed, not observed). The capture animation was inspected frame by frame because the preview pane was often hidden, not watched at full speed. FAQ accordion animation needs `::details-content` and `interpolate-size` (Chromium); other browsers open instantly. If ContextSnap itself is running, it takes the shortcut before the browser. No og:image or custom 404 yet.
- Bringing the existing window forward can be blocked by Windows focus rules; the taskbar button flashes instead.

## Rename to Snapctx, Windows installer, website polish (2026-10-28)

Done and verified locally:
- Product renamed to Snapctx (UI, help, notifications, Markdown footer, exe `Snapctx.exe`, Linux binary `snapctx`, docs, website, macOS strings). Kept for compatibility: C# namespaces and project/folder names, the single-instance IPC name (`ContextSnap`, so a new build stops an old one), the GitHub and `ContextSnap-Releases` repository URLs, the 1.0.1 download URLs and checksums, the macOS bundle identifier and executable name, and the website consent storage key.
- Migration: settings are read from `%APPDATA%\ContextSnap` (Linux `~/.config/ContextSnap`) until the first save; enabling or disabling startup removes an own `ContextSnap.lnk`; Linux `--install`/`--uninstall` remove the old `contextsnap` binary, autostart entry, GNOME shortcut, and stop the old background instance. Unit tests cover the Windows paths.
- `installer/Snapctx.iss` (Inno Setup 6.7): per-user, self-contained x64, Start Menu + optional desktop shortcut, startup task, run after install, stops a running Snapctx/ContextSnap first. Tested on this PC: fresh install over a running ContextSnap (stopped it, replaced its startup shortcut), runtime loaded from the install folder, Ctrl+Alt+Shift+C copied a "by Snapctx" snapshot, in-place upgrade, install without startup, uninstall (files and shortcuts removed, settings kept).
- Release workflow builds `Snapctx-Setup.exe` and `Snapctx-<v>-linux-x64.tar.gz`.
- Website: hero chat redesign (paste into composer, typed question, streamed answer), half keyboard/half chat hero matching the sample desktop width, faster collector stacking, installer-aware Windows card, official Tux logo (Larry Ewing / The GIMP), downloads/privacy/closing sections, no em dashes.

Not verified:
- Installer on a clean Windows without .NET (the bundled runtime was confirmed loaded from the install folder, but no clean VM was available), Windows 10, and after a real reboot.
- Linux rename and migration on a real Linux desktop (unit tests only); macOS changes not built (no Mac).
- The Release workflow's Inno Setup step on GitHub runners (not run).
