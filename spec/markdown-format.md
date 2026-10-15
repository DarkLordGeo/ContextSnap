# ContextSnap Markdown output specification

Both implementations (Windows: `src/`, macOS: `macos/`) produce Markdown with this structure. Sections that are turned off or not supported on a platform are omitted; their names appear in the final "Not collected" line.

```markdown
# Context Snapshot

_Captured 2026-10-14 19:31 (UTC+04:00) by ContextSnap. Sensitive values may be redacted._

**Foreground window:** <app> — <window title>

## Visible text

_Read on this PC from the screen (<method summary>). Sensitive values may be redacted._

### Active window: <app> — <window title>

_Source: <UI Automation | Accessibility | OCR | combinations joined with " + "> · <n> characters[ · shortened to the per-window limit]_

_Note: <warning>_            (zero or more)

```text
<visible text in reading order>
```

## Other visible windows      (only when more than one window was read)

### <app> — <window title>
...

## System context

### Open windows (<n>, most recently used first)
- **<process>**: <title>[ _(foreground)_]

### Git repositories (<n>)

#### <folder name> — `<path>`

- Branch: `<branch>` → `<upstream>` (ahead <a>, behind <b>) | (no upstream) | (no commits yet)
- HEAD: detached at `<hash>`
- Working tree: clean | <n> staged, <n> unstaged, <n> untracked, <n> conflicted
- Changed files:
  - `<XY>` `<path>`[ ← `<original path>`]
  - _…and <n> more._
- Recent commits:
  - `<hash>` <subject> (<relative date>)

### System
- <OS and version> · <architecture> · <n> logical processors · <memory> memory

_Not collected (turned off): <section names>._
```

## Rules

1. **Redaction first.** Every captured string passes through the redactor before it is written. Redaction must match `spec/redaction-cases.json` exactly on both platforms (default options: secrets and emails on, identity hiding and custom terms off).
2. **Escaping.** Titles and names are single-line and Markdown-escaped. Paths, hashes, and branch names use inline code spans. Visible text is written verbatim (after redaction) inside a fenced `text` block whose backtick fence is longer than any backtick run in the text. See `spec/markdown-cases.json` (`escape`, `codeSpan`, `fence`).
3. **Status lines.** Empty, unavailable, and failed sources are reported in italics (`_No visible text could be extracted; …_`, `_Unavailable: …_`, `_Collection failed: …_`). One failing source never removes the others.
4. **Limits.** The whole document is cut at a line boundary to the configured maximum, and the notice in `spec/markdown-cases.json` (`truncationNotice`) is appended. If the cut falls inside a fenced code block, the block is closed with its own fence before the notice, and the cut moves back if needed so the result still fits; if no content of the block would remain, the cut moves before its opening line. The result never has an unbalanced fence and the notice is never inside captured content. See `limitOutput` cases.
5. **Counts and sizes.** Characters are UTF-16 code units of the copied text, words are whitespace-separated tokens containing a letter or digit, and size is the UTF-8 byte count formatted with binary units (`textStatistics`, `formatSize` cases).
6. **Privacy.** Visible text is read only when the user captures. Password fields are never read. Browsers contribute page content only, never tabs or the address bar.

Platform-specific labels: Windows reports the method as "UI Automation", and macOS as "Accessibility". Both use "OCR" for on-device text recognition (Windows.Media.Ocr and Apple Vision respectively).
