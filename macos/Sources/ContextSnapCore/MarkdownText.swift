import Foundation

/// Helpers for writing untrusted text into Markdown. Matches `spec/markdown-cases.json`.
public enum MarkdownText {
    private static let escaped: Set<Character> = ["\\", "`", "*", "_", "[", "]", "<", ">", "|", "~"]
    private static let placeholder = try! NSRegularExpression(pattern: #"\[(?:REDACTED(?:_[A-Z]+)*|USER|HOST)\]"#)

    /// Escapes Markdown control characters but keeps redaction placeholders readable.
    public static func escape(_ text: String) -> String {
        guard !text.isEmpty else { return "" }
        let ns = text as NSString
        var output = ""
        var position = 0
        for match in placeholder.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            output += escapeSegment(ns.substring(with: NSRange(location: position, length: match.range.location - position)))
            output += ns.substring(with: match.range)
            position = match.range.location + match.range.length
        }
        output += escapeSegment(ns.substring(from: position))
        return output
    }

    /// Inline code span with a fence that cannot be closed early.
    public static func code(_ text: String) -> String {
        if text.isEmpty { return "` `" }
        let longest = longestBacktickRun(text)
        if longest == 0 { return "`\(text)`" }
        let fence = String(repeating: "`", count: longest + 1)
        return "\(fence) \(text) \(fence)"
    }

    /// Backtick fence for a fenced block: at least three, longer than any run in the text.
    public static func fence(_ text: String) -> String {
        String(repeating: "`", count: max(3, longestBacktickRun(text) + 1))
    }

    /// Collapses whitespace to single spaces and truncates with an ellipsis.
    public static func singleLine(_ text: String?, maxLength: Int) -> String {
        guard let text, !text.isEmpty else { return "" }
        var words: [Substring] = []
        for part in text.split(whereSeparator: { $0.isWhitespace || $0.isNewline || ($0.unicodeScalars.first.map { CharacterSet.controlCharacters.contains($0) } ?? false) }) {
            words.append(part)
        }
        let joined = words.joined(separator: " ")
        guard joined.count > maxLength else { return joined }
        return String(joined.prefix(max(0, maxLength - 1))).trimmingCharacters(in: .whitespaces) + "…"
    }

    /// Multi-line text for a fenced block: LF line endings, no control characters except tabs.
    public static func block(_ text: String) -> String {
        let normalized = text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
        let filtered = normalized.unicodeScalars.filter { $0 == "\n" || $0 == "\t" || !CharacterSet.controlCharacters.contains($0) }
        return String(String.UnicodeScalarView(filtered)).trimmingCharacters(in: CharacterSet(charactersIn: "\n"))
    }

    private static func escapeSegment(_ text: String) -> String {
        var output = ""
        for character in text {
            if escaped.contains(character) { output.append("\\") }
            output.append(character)
        }
        return output
    }

    private static func longestBacktickRun(_ text: String) -> Int {
        var longest = 0, run = 0
        for character in text {
            run = character == "`" ? run + 1 : 0
            longest = max(longest, run)
        }
        return longest
    }
}

/// Character, word, and UTF-8 byte counts. Characters are UTF-16 code units, matching Windows.
public struct TextStatistics: Equatable, Sendable {
    public let characters: Int
    public let words: Int
    public let bytes: Int

    public static func measure(_ text: String) -> TextStatistics {
        var words = 0
        var inToken = false
        var hasWordCharacter = false
        for character in text {
            if character.isWhitespace {
                if inToken && hasWordCharacter { words += 1 }
                inToken = false
                hasWordCharacter = false
                continue
            }
            inToken = true
            if character.isLetter || character.isNumber { hasWordCharacter = true }
        }
        if inToken && hasWordCharacter { words += 1 }
        return TextStatistics(characters: text.utf16.count, words: words, bytes: text.utf8.count)
    }

    /// "8,420 characters · 1,240 words · 8.2 KB"
    public var summary: String {
        "\(Self.count(characters, "character")) · \(Self.count(words, "word")) · \(MarkdownFormatter.formatSize(bytes))"
    }

    private static func count(_ value: Int, _ noun: String) -> String {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.groupingSeparator = ","
        formatter.usesGroupingSeparator = true
        return "\(formatter.string(from: NSNumber(value: value)) ?? String(value)) \(noun)\(value == 1 ? "" : "s")"
    }
}
