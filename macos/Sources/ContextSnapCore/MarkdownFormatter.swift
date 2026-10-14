import Foundation

/// Formats a snapshot as Markdown following `spec/markdown-format.md`. Every captured string is
/// redacted before it is written.
public struct MarkdownFormatter {
    public static let defaultMaxCharacters = 12_000
    public static let truncationNotice = "\n\n_[Snapshot truncated by the output limit.]_\n"
    private static let maxFieldLength = 240

    private let redactor: Redactor

    public init(redactor: Redactor) {
        self.redactor = redactor
    }

    public func format(_ snapshot: Snapshot) -> String {
        var md = "# Context Snapshot\n\n"
        md += "_Captured \(Self.timestamp(snapshot.capturedAt, snapshot.timeZone)) by ContextSnap. Sensitive values may be redacted._\n"

        if let app = snapshot.foregroundApp {
            md += "\n**Foreground window:** \(text(app)) — \(text(snapshot.foregroundTitle ?? ""))\n"
        }

        appendVisibleText(&md, snapshot.visibleText)

        md += "\n## System context\n"
        appendRunningApps(&md, snapshot.runningApps)
        appendGit(&md, snapshot.git)
        appendSystem(&md, snapshot.system)

        let disabled = [snapshot.visibleText.name, snapshot.runningApps.name, snapshot.git.name, snapshot.system.name]
            .enumerated()
            .filter { index, _ in
                [snapshot.visibleText.status, snapshot.runningApps.status, snapshot.git.status, snapshot.system.status][index] == .disabled
            }
            .map(\.element)
        if !disabled.isEmpty {
            md += "\n_Not collected (turned off): \(disabled.joined(separator: ", "))._\n"
        }
        return md
    }

    /// Cuts at a line boundary when possible and appends a visible notice. UTF-16 based, matching Windows.
    public static func limitOutput(_ markdown: String, maxCharacters: Int = defaultMaxCharacters) -> String {
        precondition(maxCharacters > 0, "maxCharacters must be positive")
        let ns = markdown as NSString
        guard ns.length > maxCharacters else { return markdown }

        let notice = truncationNotice as NSString
        if maxCharacters <= notice.length {
            return notice.substring(to: maxCharacters)
        }

        var cut = maxCharacters - notice.length
        let newline = ns.range(of: "\n", options: .backwards, range: NSRange(location: 0, length: cut))
        if newline.location != NSNotFound && newline.location > cut / 2 {
            cut = newline.location
        }
        if cut > 0 && CFStringIsSurrogateHighCharacter(ns.character(at: cut - 1)) {
            cut -= 1
        }

        var head = ns.substring(to: cut)
        while let last = head.unicodeScalars.last, CharacterSet.whitespacesAndNewlines.contains(last) {
            head.unicodeScalars.removeLast()
        }
        return head + truncationNotice
    }

    /// Binary units with one decimal below 10, matching Windows ("8.2 KB", "157 MB").
    public static func formatSize(_ bytes: Int) -> String {
        let units = ["B", "KB", "MB", "GB", "TB"]
        var value = Double(bytes)
        var unit = 0
        while value >= 1024 && unit < units.count - 1 {
            value /= 1024
            unit += 1
        }
        if unit == 0 { return "\(bytes) B" }
        if value >= 10 { return "\(Int(value.rounded())) \(units[unit])" }
        let rounded = (value * 10).rounded() / 10
        let text = rounded == rounded.rounded(.down) ? String(Int(rounded)) : String(format: "%.1f", rounded)
        return "\(text) \(units[unit])"
    }

    // MARK: Sections

    private func appendVisibleText(_ md: inout String, _ section: Section<WindowText>) {
        guard section.status != .disabled else { return }
        md += "\n## Visible text\n\n"

        switch section.status {
        case .empty:
            md += "_No visible text could be extracted; the system context below is still included._\n"
            appendNotes(&md, section.notes)
            return
        case .unavailable(let reason), .failed(let reason):
            md += "_Visible text could not be read: \(text(reason))_\n"
            appendNotes(&md, section.notes)
            return
        default:
            break
        }

        md += "_Read on this Mac from the screen (\(text(Self.summary(section.items)))). Sensitive values may be redacted._\n"
        for window in section.items where window.isFocused {
            appendWindowText(&md, window, heading: "### Active window: \(text(window.appName)) — \(text(window.windowTitle))")
        }
        let others = section.items.filter { !$0.isFocused }
        if !others.isEmpty {
            md += "\n## Other visible windows\n"
            for window in others {
                appendWindowText(&md, window, heading: "### \(text(window.appName)) — \(text(window.windowTitle))")
            }
        }
        appendNotes(&md, section.notes)
    }

    /// "Accessibility 2,340 characters · OCR 680 characters · 1 window"
    public static func summary(_ windows: [WindowText]) -> String {
        var parts: [String] = []
        let ax = windows.reduce(0) { $0 + $1.accessibilityCharacters }
        let ocr = windows.reduce(0) { $0 + $1.ocrCharacters }
        if ax > 0 { parts.append("Accessibility \(grouped(ax)) characters") }
        if ocr > 0 { parts.append("OCR \(grouped(ocr)) characters") }
        parts.append(windows.count == 1 ? "1 window" : "\(windows.count) windows")
        let warnings = windows.reduce(0) { $0 + $1.warnings.count }
        if warnings > 0 { parts.append(warnings == 1 ? "1 warning" : "\(warnings) warnings") }
        return parts.joined(separator: " · ")
    }

    private func appendWindowText(_ md: inout String, _ window: WindowText, heading: String) {
        md += "\n\(heading)\n\n"
        var details = "Source: \(window.sourceLabel) · \(Self.grouped(window.text.utf16.count)) characters"
        if window.truncated { details += " · shortened to the per-window limit" }
        md += "_\(details)_\n"
        for warning in window.warnings {
            md += "\n_Note: \(text(warning))_\n"
        }
        guard !window.text.isEmpty else { return }
        let body = MarkdownText.block(redactor.redact(window.text))
        let fence = MarkdownText.fence(body)
        md += "\n\(fence)text\n\(body)\n\(fence)\n"
    }

    private func appendRunningApps(_ md: inout String, _ section: Section<RunningApp>) {
        guard start(&md, section, title: "Running apps", countSuffix: "", empty: "No running apps found.") else { return }
        for app in section.items {
            md += "- **\(text(app.name))**\(app.isActive ? " _(foreground)_" : "")\n"
        }
        appendNotes(&md, section.notes)
    }

    private func appendGit(_ md: inout String, _ section: Section<GitRepository>) {
        guard start(&md, section, title: "Git repositories", countSuffix: "",
                    empty: "No Git repositories found for the active document or configured project folders.") else { return }
        for (index, repo) in section.items.enumerated() {
            if index > 0 { md += "\n" }
            md += "#### \(text((repo.root as NSString).lastPathComponent)) — \(code(repo.root))\n\n"
            if let error = repo.error {
                md += "- _Could not read repository: \(text(error))_\n"
                continue
            }
            md += "- \(describeBranch(repo))\n"
            md += "- Working tree: \(Self.describeWorkingTree(repo))\n"
            if !repo.changes.isEmpty {
                md += "- Changed files:\n"
                for change in repo.changes {
                    let original = change.originalPath.map { " ← \(code($0))" } ?? ""
                    md += "  - \(MarkdownText.code(change.code)) \(code(change.path))\(original)\n"
                }
                let hidden = repo.totalChanges - repo.changes.count
                if hidden > 0 { md += "  - _…and \(hidden) more._\n" }
            }
            if !repo.recentCommits.isEmpty {
                md += "- Recent commits:\n"
                for commit in repo.recentCommits {
                    let date = commit.relativeDate.map { " (\(text($0)))" } ?? ""
                    md += "  - \(code(commit.hash)) \(text(commit.subject))\(date)\n"
                }
            }
        }
        appendNotes(&md, section.notes)
    }

    private func appendSystem(_ md: inout String, _ section: Section<SystemSummary>) {
        guard start(&md, section, title: "System", countSuffix: nil, empty: "No system information available.") else { return }
        for system in section.items {
            md += "- \(text(system.operatingSystem)) · \(text(system.architecture)) · \(system.logicalProcessors) logical processors · \(Self.formatSize(Int(system.memoryBytes))) memory\n"
        }
        appendNotes(&md, section.notes)
    }

    private func start<T>(_ md: inout String, _ section: Section<T>, title: String, countSuffix: String?, empty: String) -> Bool {
        guard section.status != .disabled else { return false }
        if section.status == .ok, let countSuffix {
            md += "\n### \(title) (\(section.items.count)\(countSuffix))\n\n"
        } else {
            md += "\n### \(title)\n\n"
        }
        switch section.status {
        case .ok:
            return true
        case .empty:
            md += "_\(empty)_\n"
        case .unavailable(let reason):
            md += "_Unavailable: \(text(reason))_\n"
        case .failed(let reason):
            md += "_Collection failed: \(text(reason))_\n"
        case .disabled:
            return false
        }
        appendNotes(&md, section.notes)
        return false
    }

    private func appendNotes(_ md: inout String, _ notes: [String]) {
        for note in notes {
            md += "\n_\(text(note))_\n"
        }
    }

    private func describeBranch(_ repo: GitRepository) -> String {
        if repo.isDetached { return "HEAD: detached at \(code(repo.headShortHash ?? "unknown"))" }
        let branch = "Branch: \(code(repo.branch ?? "unknown"))"
        if repo.hasNoCommits { return branch + " (no commits yet)" }
        guard let upstream = repo.upstream else { return branch + " (no upstream)" }
        let sync: String
        switch (repo.ahead, repo.behind) {
        case (0, 0): sync = "up to date"
        case (let a, 0): sync = "ahead \(a)"
        case (0, let b): sync = "behind \(b)"
        case let (a, b): sync = "ahead \(a), behind \(b)"
        }
        return "\(branch) → \(code(upstream)) (\(sync))"
    }

    private static func describeWorkingTree(_ repo: GitRepository) -> String {
        guard repo.totalChanges > 0 else { return "clean" }
        var parts: [String] = []
        if repo.staged > 0 { parts.append("\(repo.staged) staged") }
        if repo.unstaged > 0 { parts.append("\(repo.unstaged) unstaged") }
        if repo.untracked > 0 { parts.append("\(repo.untracked) untracked") }
        if repo.conflicted > 0 { parts.append("\(repo.conflicted) conflicted") }
        return parts.joined(separator: ", ")
    }

    private func text(_ value: String) -> String {
        MarkdownText.escape(MarkdownText.singleLine(redactor.redact(value), maxLength: Self.maxFieldLength))
    }

    private func code(_ value: String) -> String {
        MarkdownText.code(MarkdownText.singleLine(redactor.redact(value), maxLength: Self.maxFieldLength))
    }

    static func grouped(_ value: Int) -> String {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.groupingSeparator = ","
        formatter.usesGroupingSeparator = true
        return formatter.string(from: NSNumber(value: value)) ?? String(value)
    }

    static func timestamp(_ date: Date, _ zone: TimeZone) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = zone
        formatter.dateFormat = "yyyy-MM-dd HH:mm '(UTC'xxx')'"
        return formatter.string(from: date)
    }
}
