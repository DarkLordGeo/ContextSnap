import AppKit
import ContextSnapCore
import SwiftUI

/// Runs one capture when the user asks for it: collect, redact, format, copy, notify.
/// Nothing runs in the background and nothing captured is stored.
final class CaptureService: ObservableObject {
    @Published private(set) var lastStatus = "No snapshot yet."
    @Published private(set) var isCapturing = false

    private let toast = ToastPresenter()

    func captureAndCopy() {
        guard !isCapturing else { return }
        isCapturing = true

        let preferences = Preferences.current()
        let front = NSWorkspace.shared.frontmostApplication
        let target = front.flatMap { $0.bundleIdentifier == Bundle.main.bundleIdentifier ? nil : CaptureTarget($0) } ?? LastActiveApp.shared.target

        Task.detached(priority: .userInitiated) { [weak self] in
            let snapshot = await SnapshotBuilder.build(target: target, preferences: preferences)
            let redactor = Redactor(options: RedactionOptions(
                redactSecrets: true,
                redactEmails: preferences.redactEmails,
                hideUserIdentity: preferences.hideIdentity,
                homeDirectory: NSHomeDirectory(),
                userName: NSUserName(),
                hostName: Host.current().localizedName))
            let markdown = MarkdownFormatter.limitOutput(
                MarkdownFormatter(redactor: redactor).format(snapshot), maxCharacters: preferences.maxOutputCharacters)
            await MainActor.run { self?.finish(markdown: markdown, snapshot: snapshot, preferences: preferences) }
        }
    }

    private func finish(markdown: String, snapshot: Snapshot, preferences: Preferences) {
        defer { isCapturing = false }

        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        var types: [NSPasteboard.PasteboardType] = [.string]
        if preferences.concealFromClipboardManagers {
            // Widely honored marker (nspasteboard.org) that asks clipboard managers not to record this item.
            types.append(NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
        }
        pasteboard.declareTypes(types, owner: nil)
        let copied = pasteboard.setString(markdown, forType: .string)
        if preferences.concealFromClipboardManagers {
            pasteboard.setString("", forType: NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
        }

        let stats = TextStatistics.measure(markdown)
        if copied {
            lastStatus = "Copied \(stats.summary) at \(DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium))."
        } else {
            lastStatus = "Copy failed: the clipboard could not be written."
        }

        guard preferences.showNotification else { return }
        if copied {
            var detail = stats.summary
            if snapshot.visibleText.status == .ok {
                detail += "\nIncludes visible text from \(snapshot.visibleText.items.count == 1 ? "1 window" : "\(snapshot.visibleText.items.count) windows")."
            }
            if markdown.hasSuffix(MarkdownFormatter.truncationNotice) {
                detail += "\nSnapshot truncated to the \(preferences.maxOutputCharacters)-character limit."
            }
            toast.show(title: "Context copied to clipboard", detail: detail, isError: false)
        } else {
            toast.show(title: "Copy failed", detail: "The clipboard could not be written. Try again.", isError: true)
        }
    }
}

enum SnapshotBuilder {
    static let visibleTextName = "Visible screen text"

    static func build(target: CaptureTarget?, preferences: Preferences) async -> Snapshot {
        var title: String?
        var documentPath: String?
        var visible = Section<WindowText>.disabled(visibleTextName)

        if preferences.captureVisibleText {
            if let target {
                if preferences.isExcluded(appName: target.name, bundleID: target.bundleID) {
                    visible = Section(name: visibleTextName, status: .empty, notes: ["The active app is excluded by your privacy settings."])
                } else {
                    let read = await readWindow(target, preferences)
                    title = read.title
                    documentPath = read.documentPath
                    visible = read.window.text.isEmpty
                        ? Section(name: visibleTextName, status: .empty, notes: read.window.warnings)
                        : .collected(visibleTextName, [read.window])
                }
            } else {
                visible = Section(name: visibleTextName, status: .empty, notes: ["No active app was found."])
            }
        }

        return Snapshot(
            capturedAt: Date(),
            foregroundApp: target?.name,
            foregroundTitle: title ?? target?.name,
            visibleText: visible,
            runningApps: runningApps(preferences, active: target),
            git: git(documentPath: documentPath, preferences: preferences),
            system: .collected("System summary", [systemSummary()]))
    }

    private static func readWindow(_ target: CaptureTarget, _ preferences: Preferences) async -> (window: WindowText, title: String?, documentPath: String?) {
        let ax = AccessibilityReader.capture(pid: target.pid, deadline: Date().addingTimeInterval(4))
        var warnings = ax.warnings
        var lines: [String] = []

        if let root = ax.root, let frame = ax.windowFrame {
            let assembled = VisibleTextAssembler.assemble(root, visibleArea: frame, maxCharacters: preferences.maxCharactersPerWindow * 2)
            lines = assembled.text.isEmpty ? [] : assembled.text.components(separatedBy: "\n")
            if assembled.skippedSecureFields > 0 {
                warnings.append("\(assembled.skippedSecureFields) password field(s) were skipped.")
            }
        }

        let accessibilityCharacters = lines.reduce(0) { $0 + $1.utf16.count }
        var ocrCharacters = 0
        if preferences.useOCR && needsOCR(accessibilityCharacters, frame: ax.windowFrame) {
            if let windowID = AccessibilityReader.windowID(for: target.pid) {
                do {
                    let result = try await ScreenOCR.recognize(windowID: windowID)
                    if let warning = result.warning { warnings.append(warning) }
                    let merged = OCRLayout.merge(accessibility: lines, ocr: OCRLayout.order(result.lines))
                    lines = merged.lines
                    ocrCharacters = merged.added
                } catch {
                    warnings.append("OCR failed (\(type(of: error))).")
                }
            } else {
                warnings.append("The window could not be located for OCR.")
            }
        }

        let (text, truncated) = VisibleTextAssembler.limit(lines, maxCharacters: preferences.maxCharactersPerWindow)
        if text.isEmpty && warnings.isEmpty {
            warnings.append("No text was found in the visible part of this window.")
        }
        let length = text.utf16.count
        let window = WindowText(
            appName: target.name, windowTitle: ax.windowTitle ?? target.name, isFocused: true,
            accessibilityCharacters: min(accessibilityCharacters, length), ocrCharacters: min(ocrCharacters, length),
            text: text, truncated: truncated, warnings: warnings)
        return (window, ax.windowTitle, ax.documentPath)
    }

    /// OCR when Accessibility returned fewer than ~200 characters per million screen points (20–200), as on Windows.
    static func needsOCR(_ characters: Int, frame: CGRect?) -> Bool {
        let area = (frame?.width ?? 0) * (frame?.height ?? 0)
        let threshold = min(200, max(20, Int((200 * area / 1_000_000).rounded())))
        return characters < threshold
    }

    private static func runningApps(_ preferences: Preferences, active: CaptureTarget?) -> Section<RunningApp> {
        let apps = NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular && $0.bundleIdentifier != Bundle.main.bundleIdentifier }
            .filter { !preferences.isExcluded(appName: $0.localizedName ?? "", bundleID: $0.bundleIdentifier) }
            .compactMap { app in app.localizedName.map { RunningApp(name: $0, isActive: app.processIdentifier == active?.pid) } }
            .sorted { ($0.isActive ? 0 : 1, $0.name.lowercased()) < ($1.isActive ? 0 : 1, $1.name.lowercased()) }
        return .collected("Running apps", Array(apps.prefix(40)))
    }

    private static func git(documentPath: String?, preferences: Preferences) -> Section<GitRepository> {
        let candidates = ([documentPath].compactMap { $0 } + preferences.projectFolders.map { ($0 as NSString).expandingTildeInPath })
        var roots: [String] = []
        for candidate in candidates {
            if let root = RepositoryLocator.findRoot(candidate), !roots.contains(root) { roots.append(root) }
        }
        guard !roots.isEmpty else { return .collected("Git repositories", []) }

        let runner = GitRunner()
        guard runner.executable != nil else {
            return Section(name: "Git repositories", status: .unavailable("Git was not found. Install the Xcode Command Line Tools or Homebrew Git."))
        }
        return .collected("Git repositories", roots.prefix(5).map { runner.read(root: $0, maxChanges: 15) })
    }

    private static func systemSummary() -> SystemSummary {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        #if arch(arm64)
        let architecture = "Apple silicon (arm64)"
        #else
        let architecture = "Intel (x86_64)"
        #endif
        return SystemSummary(
            operatingSystem: "macOS \(version.majorVersion).\(version.minorVersion).\(version.patchVersion)",
            architecture: architecture,
            logicalProcessors: ProcessInfo.processInfo.processorCount,
            memoryBytes: ProcessInfo.processInfo.physicalMemory)
    }
}

/// A small, non-activating notice near the menu bar that fades in and out.
final class ToastPresenter {
    private var panel: NSPanel?
    private var hideWork: DispatchWorkItem?

    func show(title: String, detail: String, isError: Bool) {
        let panel = self.panel ?? makePanel()
        self.panel = panel

        let hosting = NSHostingView(rootView: ToastView(title: title, detail: detail, isError: isError))
        let size = hosting.fittingSize
        panel.contentView = hosting
        panel.setContentSize(size)
        if let screen = NSScreen.main {
            let area = screen.visibleFrame
            panel.setFrameOrigin(NSPoint(x: area.maxX - size.width - 16, y: area.maxY - size.height - 12))
        }

        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        panel.alphaValue = 0
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = reduceMotion ? 0.05 : 0.2
            panel.animator().alphaValue = 1
        }

        hideWork?.cancel()
        let work = DispatchWorkItem { [weak panel] in
            guard let panel else { return }
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = reduceMotion ? 0.05 : 0.25
                panel.animator().alphaValue = 0
            }, completionHandler: { panel.orderOut(nil) })
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + (isError ? 5 : 3.5), execute: work)
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 320, height: 80),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        return panel
    }
}

struct ToastView: View {
    var title: String
    var detail: String
    var isError: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: isError ? "xmark.circle.fill" : "checkmark.circle.fill")
                .font(.title2)
                .foregroundStyle(isError ? Color.red : Color.green)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.headline)
                Text(detail).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .frame(width: 340)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.primary.opacity(0.08)))
        .accessibilityElement(children: .combine)
    }
}
