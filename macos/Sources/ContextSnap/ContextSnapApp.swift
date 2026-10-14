import AppKit
import SwiftUI

@main
struct ContextSnapApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra("ContextSnap", systemImage: "text.viewfinder") {
            MenuContent(capture: appDelegate.capture)
        }
        Settings {
            SettingsView()
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    let capture = CaptureService()
    private var onboardingWindow: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        PreferenceKey.registerDefaults()
        LastActiveApp.shared.start()

        HotKeyManager.shared.onPress = { [weak self] in self?.capture.captureAndCopy() }
        HotKeyManager.shared.applyPreferences()

        // Launch at login is on by default; it is set once and can be turned off in the menu or settings.
        let defaults = UserDefaults.standard
        if !defaults.bool(forKey: PreferenceKey.launchAtLoginInitialized) {
            LaunchAtLogin.set(true)
            defaults.set(true, forKey: PreferenceKey.launchAtLoginInitialized)
        }

        if !defaults.bool(forKey: PreferenceKey.onboardingShown) {
            showOnboarding()
        }
    }

    func showOnboarding() {
        if let onboardingWindow {
            onboardingWindow.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 460),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Welcome to ContextSnap"
        window.isReleasedWhenClosed = false
        window.contentView = NSHostingView(rootView: OnboardingView { [weak self] in
            UserDefaults.standard.set(true, forKey: PreferenceKey.onboardingShown)
            self?.onboardingWindow?.close()
        })
        window.center()
        onboardingWindow = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}

struct MenuContent: View {
    @ObservedObject var capture: CaptureService
    @State private var launchAtLogin = LaunchAtLogin.isEnabled

    var body: some View {
        Button("Capture & Copy  \(Shortcut.current().display)") { capture.captureAndCopy() }
            .disabled(capture.isCapturing)
        Text(capture.lastStatus)
        Divider()
        Toggle("Launch at Login", isOn: Binding(
            get: { launchAtLogin },
            set: { newValue in
                LaunchAtLogin.set(newValue)
                launchAtLogin = LaunchAtLogin.isEnabled
            }))
        Button("Permissions…") {
            (NSApp.delegate as? AppDelegate)?.showOnboarding()
        }
        SettingsLink { Text("Settings…") }
        Divider()
        Button("Quit ContextSnap") { NSApp.terminate(nil) }
    }
}

struct OnboardingView: View {
    var onDone: () -> Void
    @State private var accessibility = Permissions.accessibilityGranted
    @State private var screenRecording = Permissions.screenRecordingGranted
    private let refresh = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("ContextSnap captures your current context only when you press \(Shortcut.current().display), redacts it on this Mac, and copies Markdown to your clipboard. Nothing is sent anywhere.")
                .fixedSize(horizontal: false, vertical: true)

            PermissionRow(
                title: "Accessibility",
                explanation: "Reads the text and title of the window you are using (for example, a chat, document, or terminal). Password fields are never read.",
                granted: accessibility,
                grant: { Permissions.requestAccessibility(); Permissions.openAccessibilitySettings() })

            PermissionRow(
                title: "Screen Recording",
                explanation: "Optional. Lets ContextSnap use on-device OCR (Apple Vision) for apps that do not expose their text. Used only when you capture.",
                granted: screenRecording,
                grant: { Permissions.requestScreenRecording(); Permissions.openScreenRecordingSettings() })

            Text("Without these permissions ContextSnap still works and includes app, Git, and system context. You can change them any time in System Settings → Privacy & Security.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Spacer()
                Button("Done", action: onDone).keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 520)
        .onReceive(refresh) { _ in
            accessibility = Permissions.accessibilityGranted
            screenRecording = Permissions.screenRecordingGranted
        }
    }
}

struct PermissionRow: View {
    var title: String
    var explanation: String
    var granted: Bool
    var grant: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: granted ? "checkmark.circle.fill" : "circle")
                .foregroundStyle(granted ? Color.green : Color.secondary)
                .font(.title3)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.headline)
                Text(explanation).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            if !granted {
                Button("Grant…", action: grant)
            } else {
                Text("Granted").foregroundStyle(.secondary)
            }
        }
    }
}

struct SettingsView: View {
    @AppStorage(PreferenceKey.shortcutKey) private var key = "C"
    @AppStorage(PreferenceKey.shortcutCommand) private var command = true
    @AppStorage(PreferenceKey.shortcutOption) private var option = true
    @AppStorage(PreferenceKey.shortcutControl) private var control = true
    @AppStorage(PreferenceKey.shortcutShift) private var shift = false
    @AppStorage(PreferenceKey.captureVisibleText) private var captureVisibleText = true
    @AppStorage(PreferenceKey.useOCR) private var useOCR = true
    @AppStorage(PreferenceKey.showNotification) private var showNotification = true
    @AppStorage(PreferenceKey.redactEmails) private var redactEmails = true
    @AppStorage(PreferenceKey.hideIdentity) private var hideIdentity = true
    @AppStorage(PreferenceKey.concealFromClipboardManagers) private var conceal = true
    @AppStorage(PreferenceKey.maxCharactersPerWindow) private var maxPerWindow = 4_000
    @AppStorage(PreferenceKey.maxOutputCharacters) private var maxOutput = 12_000
    @AppStorage(PreferenceKey.projectFolders) private var projectFolders = ""
    @AppStorage(PreferenceKey.excludedApps) private var excludedApps = ""
    @State private var launchAtLogin = LaunchAtLogin.isEnabled
    @State private var shortcutStatus = ""

    var body: some View {
        Form {
            Section("Shortcut") {
                HStack {
                    Toggle("⌃ Control", isOn: $control)
                    Toggle("⌥ Option", isOn: $option)
                    Toggle("⇧ Shift", isOn: $shift)
                    Toggle("⌘ Command", isOn: $command)
                    Picker("Key", selection: $key) {
                        ForEach(Shortcut.availableKeys, id: \.self) { Text($0).tag($0) }
                    }
                    .frame(width: 110)
                }
                Text(shortcutStatus).font(.caption).foregroundStyle(.secondary)
            }

            Section("What to capture") {
                Toggle("Visible text of the active window (Accessibility)", isOn: $captureVisibleText)
                Toggle("Use on-device OCR when an app does not expose its text (Screen Recording)", isOn: $useOCR)
                Stepper("Maximum characters per window: \(maxPerWindow)", value: $maxPerWindow, in: 200...50_000, step: 500)
                Stepper("Maximum snapshot size: \(maxOutput)", value: $maxOutput, in: 1_000...200_000, step: 1_000)
                VStack(alignment: .leading) {
                    Text("Project folders for Git (one per line)").font(.caption)
                    TextEditor(text: $projectFolders).font(.system(.body, design: .monospaced)).frame(height: 54)
                }
            }

            Section("Privacy") {
                Toggle("Redact email addresses", isOn: $redactEmails)
                Toggle("Hide my identity (home folder becomes ~, user and computer names are replaced)", isOn: $hideIdentity)
                Toggle("Ask clipboard managers not to record copied snapshots", isOn: $conceal)
                VStack(alignment: .leading) {
                    Text("Never read these apps (names or bundle IDs, one per line)").font(.caption)
                    TextEditor(text: $excludedApps).font(.system(.body, design: .monospaced)).frame(height: 70)
                }
                Text("Secrets and tokens are always redacted. Redaction is pattern-based and cannot catch everything, so review what you paste.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("General") {
                Toggle("Launch at login", isOn: Binding(
                    get: { launchAtLogin },
                    set: { newValue in
                        LaunchAtLogin.set(newValue)
                        launchAtLogin = LaunchAtLogin.isEnabled
                    }))
                Toggle("Show a notification after copying", isOn: $showNotification)
            }
        }
        .formStyle(.grouped)
        .frame(width: 560)
        .onAppear(perform: applyShortcut)
        .onChange(of: key) { _, _ in applyShortcut() }
        .onChange(of: command) { _, _ in applyShortcut() }
        .onChange(of: option) { _, _ in applyShortcut() }
        .onChange(of: control) { _, _ in applyShortcut() }
        .onChange(of: shift) { _, _ in applyShortcut() }
    }

    private func applyShortcut() {
        let shortcut = Shortcut.current()
        if !shortcut.hasModifier {
            shortcutStatus = "Choose at least Control, Option, or Command."
            HotKeyManager.shared.unregister()
        } else if HotKeyManager.shared.applyPreferences() {
            shortcutStatus = "Active: \(shortcut.display)"
        } else {
            shortcutStatus = "\(shortcut.display) is already used by another app. Choose another combination."
        }
    }
}
