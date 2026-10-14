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
- [x] Global hotkey Ctrl+Alt+Shift+C (capture + copy, taskbar flash) — exercised
- [x] Command line: `--capture`, `--print`, `--help` — exercised
- [x] Settings file with normalization; written only when changed
- [x] Release build and framework-dependent publish (`publish/ContextSnap`)
- [x] README and docs/ARCHITECTURE.md
- [x] Start with Windows (Startup-folder shortcut, on by default, `--startup` opens minimized) — enable/disable/repair and shortcut launch verified; actual sign-in not performed
- [x] Animated copy notifications (bottom-right toast for button, auto-copy, hotkey, and `--capture`; success/truncated/empty/failure; reduced motion; light/dark) — verified on one 100% DPI monitor
- [x] Visible screen text (focused or all visible windows; UI Automation with on-device OCR fallback; browsers read page only; password/tab/title-bar skipping; limits, timeouts, exclusions) — verified on Chrome (ChatGPT, YouTube), Claude app, Notepad, message box, VS Code first-run screen (OCR), two-window capture
- [x] Single instance per session (second launch activates the first; `--startup` duplicate exits silently) — verified
- [x] Per-window screen text limit cuts an overlong line at a word boundary, so a token is never split before redaction (Windows + macOS; regression tests added) — Windows logic checked on Linux with a standalone run; test suite not run here
- [x] Website redesign (static HTML/CSS/JS, green accents, entrance/scroll fade-ins, reduced motion, skip link, focus styles, accurate privacy page) — served locally and checked in headless Chromium at 1280/820/375 px: no console errors, no horizontal overflow, all links resolve, download buttons stay disabled while `downloads.json` lists no files

## macOS (not built or tested; needs a Mac)
- [ ] Run `swift test` and `./scripts/build.sh` on macOS 14+
- [ ] Verify the new window-title exclusions (InPrivate, Incognito, Private Browsing, Password by default) skip private browser windows

## Next
- [ ] Tray icon
- [ ] Configurable hotkey
- [ ] Dark theme
- [ ] Installer/MSIX packaging and an application icon
- [ ] Automated UI smoke test
- [ ] First tagged release so the website download buttons have real files (`release.yml` fills `downloads.json`)
- [ ] Code signing (Windows) and Developer ID signing + notarization (macOS)

## Known limitations
- Terminal working directories are not collected (no documented, safe Windows API).
- Browser tabs: only the active tab's URL is available; English-only cleanup of Chromium status suffixes.
- Explorer virtual locations (Home, This PC) are counted, not listed.
- Arm64 and Windows 10 untested.
- If another app owns F5 system-wide, use Ctrl+Enter.
- Start with Windows follows the most recently opened copy, so running from source re-points it at the build output.
- Visible screen text not verified with AnyDesk, a VS Code editor/terminal pane, high-DPI, or multiple monitors; Windows OCR projection adds ~25 MB.
- Copy notifications not yet tried on multi-monitor, non-100% scaling, or non-bottom taskbars; real clipboard-lock failure not reproducible here.
- When the output limit cuts inside a visible-text code block, the block is left unclosed and the truncation notice appears inside it.
- Website: the Vercel project's Root Directory must be set to `website` in the dashboard (not verifiable from the repository).
- Bringing the existing window forward can be blocked by Windows focus rules; the taskbar button flashes instead.
