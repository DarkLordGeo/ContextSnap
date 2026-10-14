import Foundation

/// Outcome of one source of context. Mirrors the Windows collector statuses.
public enum SectionStatus: Equatable, Sendable {
    case ok
    case empty
    case disabled
    case unavailable(String)
    case failed(String)
}

public struct Section<Item: Sendable>: Sendable {
    public var name: String
    public var status: SectionStatus
    public var items: [Item]
    public var notes: [String]

    public init(name: String, status: SectionStatus, items: [Item] = [], notes: [String] = []) {
        self.name = name
        self.status = status
        self.items = items
        self.notes = notes
    }

    public static func disabled(_ name: String) -> Section { Section(name: name, status: .disabled) }

    /// Ok when there are items, Empty otherwise.
    public static func collected(_ name: String, _ items: [Item], notes: [String] = []) -> Section {
        Section(name: name, status: items.isEmpty ? .empty : .ok, items: items, notes: notes)
    }
}

public struct WindowText: Sendable, Equatable {
    public var appName: String
    public var windowTitle: String
    public var isFocused: Bool
    public var accessibilityCharacters: Int
    public var ocrCharacters: Int
    public var text: String
    public var truncated: Bool
    public var warnings: [String]

    public init(appName: String, windowTitle: String, isFocused: Bool, accessibilityCharacters: Int, ocrCharacters: Int, text: String, truncated: Bool, warnings: [String]) {
        self.appName = appName
        self.windowTitle = windowTitle
        self.isFocused = isFocused
        self.accessibilityCharacters = accessibilityCharacters
        self.ocrCharacters = ocrCharacters
        self.text = text
        self.truncated = truncated
        self.warnings = warnings
    }

    public var sourceLabel: String {
        switch (accessibilityCharacters > 0, ocrCharacters > 0) {
        case (true, true): return "Accessibility + OCR"
        case (true, false): return "Accessibility"
        case (false, true): return "OCR"
        default: return "none"
        }
    }
}

public struct RunningApp: Sendable, Equatable {
    public var name: String
    public var isActive: Bool

    public init(name: String, isActive: Bool) {
        self.name = name
        self.isActive = isActive
    }
}

public struct GitChange: Sendable, Equatable {
    public var code: String
    public var path: String
    public var originalPath: String?
}

public struct GitCommit: Sendable, Equatable {
    public var hash: String
    public var subject: String
    public var relativeDate: String?
}

public struct GitRepository: Sendable, Equatable {
    public var root: String
    public var branch: String?
    public var isDetached = false
    public var hasNoCommits = false
    public var headShortHash: String?
    public var upstream: String?
    public var ahead = 0
    public var behind = 0
    public var staged = 0
    public var unstaged = 0
    public var untracked = 0
    public var conflicted = 0
    public var changes: [GitChange] = []
    public var totalChanges = 0
    public var recentCommits: [GitCommit] = []
    public var error: String?

    public init(root: String) { self.root = root }
}

public struct SystemSummary: Sendable, Equatable {
    public var operatingSystem: String
    public var architecture: String
    public var logicalProcessors: Int
    public var memoryBytes: UInt64

    public init(operatingSystem: String, architecture: String, logicalProcessors: Int, memoryBytes: UInt64) {
        self.operatingSystem = operatingSystem
        self.architecture = architecture
        self.logicalProcessors = logicalProcessors
        self.memoryBytes = memoryBytes
    }
}

public struct Snapshot: Sendable {
    public var capturedAt: Date
    public var timeZone: TimeZone
    public var foregroundApp: String?
    public var foregroundTitle: String?
    public var visibleText: Section<WindowText>
    public var runningApps: Section<RunningApp>
    public var git: Section<GitRepository>
    public var system: Section<SystemSummary>

    public init(capturedAt: Date, timeZone: TimeZone = .current, foregroundApp: String?, foregroundTitle: String?,
                visibleText: Section<WindowText>, runningApps: Section<RunningApp>, git: Section<GitRepository>, system: Section<SystemSummary>) {
        self.capturedAt = capturedAt
        self.timeZone = timeZone
        self.foregroundApp = foregroundApp
        self.foregroundTitle = foregroundTitle
        self.visibleText = visibleText
        self.runningApps = runningApps
        self.git = git
        self.system = system
    }
}
