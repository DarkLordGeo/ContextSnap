import CoreGraphics
import Foundation

/// A detached snapshot of one accessibility element (top-left screen coordinates).
public struct AXNode: Sendable {
    public var role: String
    public var subrole: String?
    public var text: String?
    public var frame: CGRect
    public var children: [AXNode]

    public init(role: String, subrole: String? = nil, text: String? = nil, frame: CGRect, children: [AXNode] = []) {
        self.role = role
        self.subrole = subrole
        self.text = text
        self.frame = frame
        self.children = children
    }
}

public struct AssembledText: Sendable, Equatable {
    public var text: String
    public var truncated: Bool
    public var skippedSecureFields: Int
}

/// Turns an accessibility tree into visible text in reading order: only elements inside the visible
/// window area, never secure text fields, leaf text preferred over container labels, same-line
/// fragments joined, consecutive duplicates removed, and a character limit applied.
public enum VisibleTextAssembler {
    /// Roles whose value or title is text a user can see.
    static let textRoles: Set<String> = ["AXStaticText", "AXTextField", "AXTextArea", "AXHeading", "AXLink", "AXCell"]
    /// Roles whose title is visible outside web content (native buttons and menus show their titles).
    /// Tab titles are deliberately absent: they name other documents or pages (same rule as Windows).
    static let nativeLabelRoles: Set<String> = ["AXButton", "AXCheckBox", "AXRadioButton", "AXPopUpButton", "AXMenuButton"]

    private struct Fragment {
        var text: String
        var frame: CGRect
    }

    public static func assemble(_ root: AXNode, visibleArea: CGRect, maxCharacters: Int) -> AssembledText {
        var fragments: [Fragment] = []
        var secure = 0
        visit(root, visibleArea: visibleArea, inWebArea: false, into: &fragments, secure: &secure)
        let lines = removeConsecutiveDuplicates(join(fragments))
        let (text, truncated) = limit(lines, maxCharacters: maxCharacters)
        return AssembledText(text: text, truncated: truncated, skippedSecureFields: secure)
    }

    private static func visit(_ node: AXNode, visibleArea: CGRect, inWebArea: Bool, into output: inout [Fragment], secure: inout Int) {
        if node.subrole == "AXSecureTextField" || node.role == "AXSecureTextField" {
            secure += 1
            return
        }
        if node.role == "AXTab" || node.subrole == "AXTabButton" {
            return // Tab titles name other documents or pages; the selected tab's content is read separately.
        }
        let visible = node.frame.width >= 3 && node.frame.height >= 3 && node.frame.intersects(visibleArea)
        let childWebArea = inWebArea || node.role == "AXWebArea"
        let before = output.count
        for child in node.children {
            visit(child, visibleArea: visibleArea, inWebArea: childWebArea, into: &output, secure: &secure)
        }
        guard visible, let text = node.text?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else { return }

        let hasChildText = output.count > before
        let isText = textRoles.contains(node.role)
        let isNativeLabel = !inWebArea && nativeLabelRoles.contains(node.role)
        guard (isText || isNativeLabel) && !hasChildText else { return }

        let lines = text.components(separatedBy: .newlines).map(normalize).filter { !$0.isEmpty }
        output.insert(contentsOf: lines.map { Fragment(text: $0, frame: node.frame) }, at: before)
    }

    /// Joins fragments that continue the same visual line.
    private static func join(_ fragments: [Fragment]) -> [String] {
        var lines: [String] = []
        var current = ""
        var previous: Fragment?
        for fragment in fragments {
            if let previous, !current.isEmpty, sameLine(previous.frame, fragment.frame) {
                let needsSpace = !(current.last?.isWhitespace ?? true) && !".,;:!?)]}%".contains(fragment.text.first ?? " ")
                current += (needsSpace ? " " : "") + fragment.text
            } else {
                if !current.isEmpty { lines.append(current) }
                current = fragment.text
            }
            previous = fragment
        }
        if !current.isEmpty { lines.append(current) }
        return lines
    }

    static func sameLine(_ previous: CGRect, _ next: CGRect) -> Bool {
        guard previous.height > 0, next.height > 0, previous.height < 60, next.height < 60 else { return false }
        let tolerance = min(previous.height, next.height) / 2
        let sameRow = abs(previous.midY - next.midY) <= tolerance
        let gap = next.minX - previous.maxX
        return sameRow && next.minX >= previous.minX && gap <= 3 * max(previous.height, next.height)
    }

    static func normalize(_ text: String) -> String {
        text.split(whereSeparator: { $0.isWhitespace || $0 == "\u{FFFC}" || $0 == "\u{200B}" }).joined(separator: " ")
    }

    static func removeConsecutiveDuplicates(_ lines: [String]) -> [String] {
        var result: [String] = []
        for line in lines where result.last != line { result.append(line) }
        return result
    }

    public static func limit(_ lines: [String], maxCharacters: Int) -> (String, Bool) {
        var output = ""
        for line in lines {
            let needed = line.utf16.count + (output.isEmpty ? 0 : 1)
            if output.utf16.count + needed > maxCharacters {
                if output.isEmpty { output = String(line.prefix(max(0, maxCharacters - 1))) + "…" }
                return (output, true)
            }
            if !output.isEmpty { output += "\n" }
            output += line
        }
        return (output, false)
    }
}

/// One recognized line with its bounding box (top-left coordinates).
public struct OCRLine: Sendable, Equatable {
    public var text: String
    public var frame: CGRect

    public init(text: String, frame: CGRect) {
        self.text = text
        self.frame = frame
    }
}

/// Reading order for OCR lines: columns left to right, each top to bottom (same as Windows).
public enum OCRLayout {
    public static func order(_ lines: [OCRLine]) -> [String] {
        let items = lines.filter { !$0.text.trimmingCharacters(in: .whitespaces).isEmpty && $0.frame.width > 0 && $0.frame.height > 0 }
        guard !items.isEmpty else { return [] }

        var columns: [(minX: CGFloat, maxX: CGFloat, lines: [OCRLine])] = []
        for line in items.sorted(by: { $0.frame.minX < $1.frame.minX }) {
            if let index = columns.firstIndex(where: { line.frame.minX < $0.maxX && $0.minX < line.frame.maxX }) {
                columns[index].lines.append(line)
                columns[index].minX = min(columns[index].minX, line.frame.minX)
                columns[index].maxX = max(columns[index].maxX, line.frame.maxX)
            } else {
                columns.append((line.frame.minX, line.frame.maxX, [line]))
            }
        }

        var merged: [(minX: CGFloat, maxX: CGFloat, lines: [OCRLine])] = []
        for column in columns.sorted(by: { $0.minX < $1.minX }) {
            if let last = merged.last, column.minX < last.maxX {
                merged[merged.count - 1].lines += column.lines
                merged[merged.count - 1].maxX = max(last.maxX, column.maxX)
            } else {
                merged.append(column)
            }
        }

        return merged.flatMap { column in
            column.lines.sorted { ($0.frame.minY, $0.frame.minX) < ($1.frame.minY, $1.frame.minX) }.map { VisibleTextAssembler.normalize($0.text) }
        }.filter { !$0.isEmpty }
    }

    /// Appends OCR lines that accessibility text did not already contain (ignoring case and punctuation).
    public static func merge(accessibility: [String], ocr: [String]) -> (lines: [String], added: Int) {
        var known = canonical(accessibility.joined(separator: " "))
        var result = accessibility
        var added = 0
        for line in ocr {
            let key = canonical(line)
            guard key.count >= 2, !known.contains(key) else { continue }
            result.append(line)
            known += key
            added += line.utf16.count
        }
        return (result, added)
    }

    static func canonical(_ text: String) -> String {
        String(text.precomposedStringWithCompatibilityMapping.lowercased().filter { $0.isLetter || $0.isNumber })
    }
}
