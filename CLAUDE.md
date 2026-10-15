# ContextSnap

## Goal
A lightweight Windows desktop utility that captures useful desktop context, generates Markdown, and copies it to the clipboard using a global keyboard shortcut. Users paste the result into any LLM manually. No AI integration.

## Stack
- C# / .NET 10
- Windows APIs for desktop integration
- xUnit for tests
- Minimal dependencies

## Workflow
Global hotkey -> Collect context -> Redact sensitive information -> Generate Markdown -> Copy to clipboard -> Notify user.

## MVP
1. Visible application window titles and process names.
2. Open File Explorer windows and current folder paths.
3. Browser tabs, titles, and URLs for supported browsers.
4. Desktop files and folders.
5. Markdown output with configurable sections and size limits.
6. Global keyboard shortcut.
7. Clipboard integration and notifications.
8. Tests for formatting and collection logic.

## Architecture
- `src/ContextSnap.Core/`: Library with all non-UI logic.
  - `Collectors/`: Context collectors (filter + OS source per collector; `Git/` for Git).
  - `Privacy/`: Exclusion rules and redaction.
  - `Formatting/`: Markdown generation and output limits.
  - `Capture/`: `SnapshotService` orchestration and command-line runner.
  - `Configuration/`, `Output/` (clipboard), `Platform/` (Windows APIs, hotkeys).
- `src/ContextSnap/`: WPF app; `Program.cs` chooses GUI or command-line mode.
- `tests/ContextSnap.Tests/`: Unit tests.
- `src/ContextSnap.Linux/` + `tests/ContextSnap.Linux.Tests/`: Linux command-line app that links the platform-neutral Core files.
- See `docs/ARCHITECTURE.md`.

Keep collection, formatting, and platform integration separate. Use abstractions only when they improve testing or extensibility.

## Security
- Local processing only. Never transmit snapshots.
- Never collect passwords, tokens, API keys, cookies, environment-variable values, or file contents.
- Never collect command-line arguments by default.
- Treat URLs, titles, paths, and filenames as potentially sensitive.
- Make URL collection configurable.
- Support exclusions and collection limits.
- Collection must not modify user files or execute captured commands.
- Handle permission failures safely.

## Engineering Rules
- Prefer built-in .NET and Windows APIs.
- Keep the application lightweight and responsive.
- Handle collector failures gracefully and report meaningful errors.
- Keep output concise and useful to LLMs.
- No AI APIs, cloud services, telemetry, accounts, or database.
- Core files linked by the Linux app must stay free of Windows APIs; put Windows-only code in separate files.
- Do not implement unrelated features or perform unnecessary refactors.

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/ContextSnap`

## Working Protocol
Before changes:
1. Inspect the existing solution and this file.
2. Read `TODO.md`.
3. Identify the smallest coherent task.

After changes:
1. Build and test.
2. Fix failures before finishing.
3. Update `TODO.md` with verified progress and limitations.
4. Report changed files, test results, and design decisions.

Never claim an unimplemented feature works.

## Implementation Order
1. Snapshot data model and collector interface.
2. Application window collector.
3. File Explorer path collector.
4. Markdown formatter and tests.
5. Desktop directory listing.
6. Research and implement Chrome/Edge tab extraction.
7. Clipboard integration.
8. Global hotkey and notifications.
9. Configuration, exclusions, redaction, output limits.
10. Packaging and documentation.
