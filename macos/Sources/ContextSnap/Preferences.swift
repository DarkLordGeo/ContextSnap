import Carbon.HIToolbox
import Foundation

/// Local settings stored in UserDefaults. Nothing here is ever transmitted.
enum PreferenceKey {
    static let shortcutKey = "shortcutKey"
    static let shortcutCommand = "shortcutCommand"
    static let shortcutOption = "shortcutOption"
    static let shortcutControl = "shortcutControl"
    static let shortcutShift = "shortcutShift"
    static let captureVisibleText = "captureVisibleText"
    static let useOCR = "useOCR"
    static let showNotification = "showNotification"
    static let copyWithoutReview = "copyWithoutReview"
    static let redactEmails = "redactEmails"
    static let hideIdentity = "hideIdentity"
    static let concealFromClipboardManagers = "concealFromClipboardManagers"
    static let maxCharactersPerWindow = "maxCharactersPerWindow"
    static let maxOutputCharacters = "maxOutputCharacters"
    static let projectFolders = "projectFolders"
    static let excludedApps = "excludedApps"
    static let excludedTitleKeywords = "excludedTitleKeywords"
    static let onboardingShown = "onboardingShown"
    static let launchAtLoginInitialized = "launchAtLoginInitialized"

    static func registerDefaults() {
        UserDefaults.standard.register(defaults: [
            shortcutKey: "C", shortcutCommand: true, shortcutOption: true, shortcutControl: true, shortcutShift: false,
            captureVisibleText: true, useOCR: true, showNotification: true, copyWithoutReview: false, redactEmails: true, hideIdentity: true,
            concealFromClipboardManagers: true, maxCharactersPerWindow: 4_000, maxOutputCharacters: 12_000,
            projectFolders: "", excludedApps: "1Password\nKeePassXC\nBitwarden\nKeychain Access",
            excludedTitleKeywords: "InPrivate\nIncognito\nPrivate Browsing\nPassword",
            onboardingShown: false, launchAtLoginInitialized: false,
        ])
    }
}

/// A snapshot of the preferences taken at capture time.
struct Preferences: Sendable {
    var captureVisibleText: Bool
    var useOCR: Bool
    var showNotification: Bool
    var copyWithoutReview: Bool
    var redactEmails: Bool
    var hideIdentity: Bool
    var concealFromClipboardManagers: Bool
    var maxCharactersPerWindow: Int
    var maxOutputCharacters: Int
    var projectFolders: [String]
    var excludedApps: [String]
    var excludedTitleKeywords: [String]

    static func current(_ defaults: UserDefaults = .standard) -> Preferences {
        Preferences(
            captureVisibleText: defaults.bool(forKey: PreferenceKey.captureVisibleText),
            useOCR: defaults.bool(forKey: PreferenceKey.useOCR),
            showNotification: defaults.bool(forKey: PreferenceKey.showNotification),
            copyWithoutReview: defaults.bool(forKey: PreferenceKey.copyWithoutReview),
            redactEmails: defaults.bool(forKey: PreferenceKey.redactEmails),
            hideIdentity: defaults.bool(forKey: PreferenceKey.hideIdentity),
            concealFromClipboardManagers: defaults.bool(forKey: PreferenceKey.concealFromClipboardManagers),
            maxCharactersPerWindow: min(max(defaults.integer(forKey: PreferenceKey.maxCharactersPerWindow), 200), 50_000),
            maxOutputCharacters: min(max(defaults.integer(forKey: PreferenceKey.maxOutputCharacters), 1_000), 200_000),
            projectFolders: lines(defaults.string(forKey: PreferenceKey.projectFolders)),
            excludedApps: lines(defaults.string(forKey: PreferenceKey.excludedApps)),
            excludedTitleKeywords: lines(defaults.string(forKey: PreferenceKey.excludedTitleKeywords)))
    }

    func isExcluded(appName: String, bundleID: String?) -> Bool {
        excludedApps.contains { entry in
            entry.caseInsensitiveCompare(appName) == .orderedSame || entry.caseInsensitiveCompare(bundleID ?? "") == .orderedSame
        }
    }

    /// Private-browsing and password windows are skipped by title, matching the Windows defaults.
    func isTitleExcluded(_ title: String?) -> Bool {
        guard let title, !title.isEmpty else { return false }
        return excludedTitleKeywords.contains { title.localizedCaseInsensitiveContains($0) }
    }

    private static func lines(_ text: String?) -> [String] {
        (text ?? "").components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }
}

struct Shortcut: Equatable {
    var key: String
    var command: Bool
    var option: Bool
    var control: Bool
    var shift: Bool

    static func current(_ defaults: UserDefaults = .standard) -> Shortcut {
        Shortcut(
            key: (defaults.string(forKey: PreferenceKey.shortcutKey) ?? "C").uppercased(),
            command: defaults.bool(forKey: PreferenceKey.shortcutCommand),
            option: defaults.bool(forKey: PreferenceKey.shortcutOption),
            control: defaults.bool(forKey: PreferenceKey.shortcutControl),
            shift: defaults.bool(forKey: PreferenceKey.shortcutShift))
    }

    var display: String {
        (control ? "⌃" : "") + (option ? "⌥" : "") + (shift ? "⇧" : "") + (command ? "⌘" : "") + key
    }

    var keyCode: UInt32? { Shortcut.keyCodes[key] }

    var carbonModifiers: UInt32 {
        (command ? UInt32(cmdKey) : 0) | (option ? UInt32(optionKey) : 0) | (control ? UInt32(controlKey) : 0) | (shift ? UInt32(shiftKey) : 0)
    }

    var hasModifier: Bool { command || option || control }

    static let availableKeys = keyCodes.keys.sorted()

    static let keyCodes: [String: UInt32] = [
        "A": UInt32(kVK_ANSI_A), "B": UInt32(kVK_ANSI_B), "C": UInt32(kVK_ANSI_C), "D": UInt32(kVK_ANSI_D),
        "E": UInt32(kVK_ANSI_E), "F": UInt32(kVK_ANSI_F), "G": UInt32(kVK_ANSI_G), "H": UInt32(kVK_ANSI_H),
        "I": UInt32(kVK_ANSI_I), "J": UInt32(kVK_ANSI_J), "K": UInt32(kVK_ANSI_K), "L": UInt32(kVK_ANSI_L),
        "M": UInt32(kVK_ANSI_M), "N": UInt32(kVK_ANSI_N), "O": UInt32(kVK_ANSI_O), "P": UInt32(kVK_ANSI_P),
        "Q": UInt32(kVK_ANSI_Q), "R": UInt32(kVK_ANSI_R), "S": UInt32(kVK_ANSI_S), "T": UInt32(kVK_ANSI_T),
        "U": UInt32(kVK_ANSI_U), "V": UInt32(kVK_ANSI_V), "W": UInt32(kVK_ANSI_W), "X": UInt32(kVK_ANSI_X),
        "Y": UInt32(kVK_ANSI_Y), "Z": UInt32(kVK_ANSI_Z),
        "0": UInt32(kVK_ANSI_0), "1": UInt32(kVK_ANSI_1), "2": UInt32(kVK_ANSI_2), "3": UInt32(kVK_ANSI_3),
        "4": UInt32(kVK_ANSI_4), "5": UInt32(kVK_ANSI_5), "6": UInt32(kVK_ANSI_6), "7": UInt32(kVK_ANSI_7),
        "8": UInt32(kVK_ANSI_8), "9": UInt32(kVK_ANSI_9),
    ]
}
