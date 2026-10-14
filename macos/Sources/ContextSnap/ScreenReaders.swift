import AppKit
import ApplicationServices
import ContextSnapCore
import ScreenCaptureKit
import Vision

struct AccessibilityCapture {
    var windowTitle: String?
    var documentPath: String?
    var windowFrame: CGRect?
    var root: AXNode?
    var warnings: [String] = []
}

/// Reads the focused window of an app through the macOS Accessibility API. Read-only: it never
/// focuses, scrolls, clicks, or types. Secure text fields are never read.
enum AccessibilityReader {
    static let maxNodes = 5_000
    static let maxDepth = 60

    static func capture(pid: pid_t, deadline: Date) -> AccessibilityCapture {
        guard AXIsProcessTrusted() else {
            return AccessibilityCapture(warnings: ["Accessibility permission is not granted, so on-screen text could not be read through Accessibility."])
        }

        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 1.0)
        // Chromium and Electron apps build their accessibility tree only for clients that ask for it.
        AXUIElementSetAttributeValue(app, "AXManualAccessibility" as CFString, kCFBooleanTrue)

        guard let window = element(app, kAXFocusedWindowAttribute) ?? element(app, kAXMainWindowAttribute) else {
            return AccessibilityCapture(warnings: ["The app has no focused window that Accessibility can read."])
        }

        var result = AccessibilityCapture()
        result.windowTitle = string(window, kAXTitleAttribute)
        if let document = string(window, kAXDocumentAttribute), let url = URL(string: document), url.isFileURL {
            result.documentPath = url.path
        }
        result.windowFrame = frame(of: window)

        var budget = maxNodes
        result.root = build(window, depth: 0, budget: &budget, deadline: deadline)
        if budget <= 0 {
            result.warnings.append("The window is very large; only the first \(maxNodes) elements were read.")
        } else if Date() >= deadline {
            result.warnings.append("Reading stopped at the time limit; the app responded slowly.")
        }
        return result
    }

    /// The on-screen window number of the app's frontmost normal window (used for OCR).
    static func windowID(for pid: pid_t) -> CGWindowID? {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else {
            return nil
        }
        let match = list.first { info in
            (info[kCGWindowOwnerPID as String] as? Int).map(pid_t.init) == pid && (info[kCGWindowLayer as String] as? Int) == 0
        }
        return (match?[kCGWindowNumber as String] as? Int).map(CGWindowID.init)
    }

    private static func build(_ node: AXUIElement, depth: Int, budget: inout Int, deadline: Date) -> AXNode? {
        guard budget > 0, depth < maxDepth, Date() < deadline else { return nil }
        budget -= 1

        let role = string(node, kAXRoleAttribute) ?? "AXUnknown"
        let subrole = string(node, kAXSubroleAttribute)
        let nodeFrame = frame(of: node) ?? .zero
        if subrole == (kAXSecureTextFieldSubrole as String) {
            return AXNode(role: role, subrole: subrole, frame: nodeFrame)
        }

        var text: String?
        switch role {
        case "AXStaticText", "AXTextField", "AXHeading", "AXCell":
            text = string(node, kAXValueAttribute) ?? string(node, kAXTitleAttribute)
        case "AXTextArea":
            text = visibleText(node) ?? string(node, kAXValueAttribute)
        case "AXLink", "AXButton", "AXCheckBox", "AXRadioButton", "AXPopUpButton", "AXMenuButton":
            text = string(node, kAXTitleAttribute) ?? string(node, kAXDescriptionAttribute)
        default:
            break
        }

        var children: [AXNode] = []
        let childElements = elements(node, kAXVisibleChildrenAttribute) ?? elements(node, kAXChildrenAttribute) ?? []
        for child in childElements {
            guard budget > 0 else { break }
            if let built = build(child, depth: depth + 1, budget: &budget, deadline: deadline) {
                children.append(built)
            }
        }
        return AXNode(role: role, subrole: subrole, text: text, frame: nodeFrame, children: children)
    }

    /// Only the part of a text area that is scrolled into view (for example, a terminal's visible output).
    private static func visibleText(_ node: AXUIElement) -> String? {
        var range: AnyObject?
        guard AXUIElementCopyAttributeValue(node, kAXVisibleCharacterRangeAttribute as CFString, &range) == .success, let range else {
            return nil
        }
        var value: AnyObject?
        guard AXUIElementCopyParameterizedAttributeValue(node, kAXStringForRangeParameterizedAttribute as CFString, range, &value) == .success else {
            return nil
        }
        return value as? String
    }

    private static func string(_ node: AXUIElement, _ attribute: String) -> String? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(node, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private static func element(_ node: AXUIElement, _ attribute: String) -> AXUIElement? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(node, attribute as CFString, &value) == .success, let value,
              CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    private static func elements(_ node: AXUIElement, _ attribute: String) -> [AXUIElement]? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(node, attribute as CFString, &value) == .success, let array = value as? [AnyObject] else {
            return nil
        }
        return array.compactMap { item in
            CFGetTypeID(item) == AXUIElementGetTypeID() ? (item as! AXUIElement) : nil
        }
    }

    private static func frame(of node: AXUIElement) -> CGRect? {
        var positionValue: AnyObject?
        var sizeValue: AnyObject?
        guard AXUIElementCopyAttributeValue(node, kAXPositionAttribute as CFString, &positionValue) == .success,
              AXUIElementCopyAttributeValue(node, kAXSizeAttribute as CFString, &sizeValue) == .success,
              let positionValue, let sizeValue,
              CFGetTypeID(positionValue) == AXValueGetTypeID(), CFGetTypeID(sizeValue) == AXValueGetTypeID() else {
            return nil
        }
        var point = CGPoint.zero
        var size = CGSize.zero
        AXValueGetValue(positionValue as! AXValue, .cgPoint, &point)
        AXValueGetValue(sizeValue as! AXValue, .cgSize, &size)
        return CGRect(origin: point, size: size)
    }
}

/// On-demand OCR of one window with ScreenCaptureKit and Apple Vision. Runs only during a capture
/// the user started, and only with Screen Recording permission. Images stay in memory and are released.
enum ScreenOCR {
    static func recognize(windowID: CGWindowID) async throws -> (lines: [OCRLine], warning: String?) {
        guard CGPreflightScreenCaptureAccess() else {
            return ([], "Screen Recording permission is not granted, so OCR was skipped.")
        }

        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
            return ([], "The window could not be captured for OCR.")
        }

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let configuration = SCStreamConfiguration()
        let scale = CGFloat(filter.pointPixelScale)
        configuration.width = max(1, Int(window.frame.width * scale))
        configuration.height = max(1, Int(window.frame.height * scale))
        configuration.showsCursor = false

        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])

        let width = CGFloat(image.width)
        let height = CGFloat(image.height)
        let lines = (request.results ?? []).compactMap { observation -> OCRLine? in
            guard let candidate = observation.topCandidates(1).first else { return nil }
            let box = observation.boundingBox // Normalized, origin at bottom-left.
            return OCRLine(text: candidate.string, frame: CGRect(
                x: box.minX * width, y: (1 - box.maxY) * height, width: box.width * width, height: box.height * height))
        }
        return (lines, nil)
    }
}
