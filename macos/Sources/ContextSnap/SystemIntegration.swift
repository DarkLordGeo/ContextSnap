import AppKit
import ApplicationServices
import Carbon.HIToolbox
import ServiceManagement

/// A system-wide shortcut through Carbon's RegisterEventHotKey. It only reacts to that one key
/// combination and needs no Accessibility or Input Monitoring permission.
final class HotKeyManager {
    static let shared = HotKeyManager()

    var onPress: (() -> Void)?
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?

    /// Registers the shortcut from preferences. Returns false when another app already owns it.
    @discardableResult
    func applyPreferences() -> Bool {
        let shortcut = Shortcut.current()
        guard let keyCode = shortcut.keyCode, shortcut.hasModifier else {
            unregister()
            return false
        }
        return register(keyCode: keyCode, modifiers: shortcut.carbonModifiers)
    }

    func register(keyCode: UInt32, modifiers: UInt32) -> Bool {
        unregister()
        if handler == nil {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            let status = InstallEventHandler(GetApplicationEventTarget(), { _, _, userData in
                guard let userData else { return noErr }
                let manager = Unmanaged<HotKeyManager>.fromOpaque(userData).takeUnretainedValue()
                DispatchQueue.main.async { manager.onPress?() }
                return noErr
            }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &handler)
            guard status == noErr else { return false }
        }
        let identifier = EventHotKeyID(signature: OSType(0x4353_4E50), id: 1) // "CSNP"
        return RegisterEventHotKey(keyCode, modifiers, identifier, GetApplicationEventTarget(), 0, &hotKey) == noErr
    }

    func unregister() {
        if let hotKey {
            UnregisterEventHotKey(hotKey)
            self.hotKey = nil
        }
    }
}

enum Permissions {
    static var accessibilityGranted: Bool { AXIsProcessTrusted() }

    /// Shows the system prompt that leads to System Settings → Privacy & Security → Accessibility.
    static func requestAccessibility() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    static var screenRecordingGranted: Bool { CGPreflightScreenCaptureAccess() }

    static func requestScreenRecording() {
        _ = CGRequestScreenCaptureAccess()
    }

    static func openAccessibilitySettings() {
        open("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
    }

    static func openScreenRecordingSettings() {
        open("x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")
    }

    private static func open(_ address: String) {
        if let url = URL(string: address) { NSWorkspace.shared.open(url) }
    }
}

enum LaunchAtLogin {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    /// Returns an error message, or nil on success.
    @discardableResult
    static func set(_ enabled: Bool) -> String? {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return nil
        } catch {
            return error.localizedDescription
        }
    }
}

/// Remembers which app was active before ContextSnap's menu was used, so "Capture" from the menu
/// reads the app the user was working in. Only the app's identity is kept, never its content.
final class LastActiveApp {
    static let shared = LastActiveApp()

    private(set) var target: CaptureTarget?
    private var observer: NSObjectProtocol?

    func start() {
        observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] notification in
            guard let app = notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
                  app.bundleIdentifier != Bundle.main.bundleIdentifier else { return }
            self?.target = CaptureTarget(app)
        }
        if let front = NSWorkspace.shared.frontmostApplication, front.bundleIdentifier != Bundle.main.bundleIdentifier {
            target = CaptureTarget(front)
        }
    }
}

struct CaptureTarget: Sendable {
    var pid: pid_t
    var name: String
    var bundleID: String?

    init(_ app: NSRunningApplication) {
        pid = app.processIdentifier
        name = app.localizedName ?? app.bundleIdentifier ?? "Unknown app"
        bundleID = app.bundleIdentifier
    }
}
