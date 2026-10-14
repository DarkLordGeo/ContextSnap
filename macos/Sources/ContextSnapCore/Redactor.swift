import Foundation

public struct RedactionOptions: Sendable {
    public var redactSecrets: Bool
    public var redactEmails: Bool
    public var hideUserIdentity: Bool
    public var customTerms: [String]
    public var homeDirectory: String?
    public var userName: String?
    public var hostName: String?

    public init(
        redactSecrets: Bool = true,
        redactEmails: Bool = true,
        hideUserIdentity: Bool = false,
        customTerms: [String] = [],
        homeDirectory: String? = nil,
        userName: String? = nil,
        hostName: String? = nil
    ) {
        self.redactSecrets = redactSecrets
        self.redactEmails = redactEmails
        self.hideUserIdentity = hideUserIdentity
        self.customTerms = customTerms
        self.homeDirectory = homeDirectory
        self.userName = userName
        self.hostName = hostName
    }
}

/// Best-effort, pattern-based redaction. A port of the Windows redactor; both must satisfy
/// `spec/redaction-cases.json`. It cannot recognize every secret.
public final class Redactor: @unchecked Sendable {
    public static let redacted = "[REDACTED]"
    public static let redactedEmail = "[REDACTED_EMAIL]"
    public static let redactedToken = "[REDACTED_TOKEN]"
    public static let redactedPrivateKey = "[REDACTED_PRIVATE_KEY]"

    /// Secrets and emails only.
    public static let `default` = Redactor(options: RedactionOptions())

    private static let genericAccountNames: Set<String> = ["user", "users", "admin", "administrator", "owner", "dev", "test", "guest", "pc", "home", "desktop"]

    // Applied in this order, matching the Windows implementation.
    private static let secretRules: [(NSRegularExpression, String)] = [
        (regex(#"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 ]*PRIVATE KEY-----|$)"#), redactedPrivateKey),
        (regex(#"(?i)\b([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@"#), "$1" + redacted + "@"),
        (regex(#"(?i)\b(authorization\s*[:=]\s*["']?(?:bearer|basic|token|digest)\s+)(?!\[REDACTED)[^\s"']+"#), "$1" + redacted),
        (regex(#"(?i)\b(bearer)\s+(?!\[REDACTED)[A-Za-z0-9\-._~+/]{16,}=*"#), "$1 " + redacted),
        (regex(#"(?i)\b(basic)\s+[A-Za-z0-9+/]{16,}={0,2}"#), "$1 " + redacted),
        (regex(#"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}"#), redactedToken),
        (regex(#"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_-]{20,}|xox[abposr]-[A-Za-z0-9-]{10,}|(?:AKIA|ASIA)[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|[rsp]k_(?:live|test)_[0-9A-Za-z]{16,}|sk-(?:ant-|proj-)?[A-Za-z0-9_-]{20,}|npm_[A-Za-z0-9]{36}|hf_[A-Za-z0-9]{30,})"#), redactedToken),
        (regex(#"(?i)\b(password|pwd|accountkey|sharedaccesskey|sharedaccesssignature)\s*=\s*(?!\[REDACTED)[^;\r\n]+"#), "$1=" + redacted),
        (regex(#"(?i)([?&](?:access_token|refresh_token|id_token|token|key|api_key|apikey|sig|signature|code|password|secret|auth|session|sid)=)[^&#\s]+"#), "$1" + redacted),
        (regex(#"(?i)\b([a-z0-9_.-]*?(?:api[_-]?key|apikey|access[_-]?key|secret|token|password|passwd|pwd|passphrase|credentials?|private[_-]?key|client[_-]?secret|account[_-]?key|session[_-]?id|sessionid|auth[_-]?token|authorization))(\s*[:=]\s*)(["']?)(?!\[REDACTED|(?:bearer|basic|token|digest)\s)([^\s"'&;,]+)"#), "$1$2$3" + redacted),
    ]

    private static let randomToken = regex(#"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{32,}={0,2}"#)
    private static let email = regex(#"(?i)(?<![\w.+-])[\w.+-]+@[a-z0-9-]+(?:\.[a-z0-9-]+)*\.[a-z]{2,}\b"#)

    public let options: RedactionOptions
    private var identityRules: [(NSRegularExpression, String)] = []
    private let customTerms: NSRegularExpression?

    public init(options: RedactionOptions) {
        self.options = options
        let terms = options.customTerms
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .map { NSRegularExpression.escapedPattern(for: $0) }
        customTerms = terms.isEmpty ? nil : try? NSRegularExpression(pattern: terms.joined(separator: "|"), options: [.caseInsensitive])
        if options.hideUserIdentity {
            buildIdentityRules()
        }
    }

    public func redact(_ text: String?) -> String {
        guard var result = text, !result.isEmpty else { return "" }

        if options.redactSecrets {
            for (pattern, template) in Self.secretRules {
                result = replace(pattern, in: result, with: template)
            }
            result = replaceRandomTokens(in: result)
        }

        if options.redactEmails {
            result = replace(Self.email, in: result, with: Self.redactedEmail)
        }

        for (pattern, template) in identityRules {
            result = replace(pattern, in: result, with: template)
        }

        if let customTerms {
            result = replace(customTerms, in: result, with: Self.redacted)
        }

        return result
    }

    static func looksRandom(_ token: String) -> Bool {
        guard token.utf16.count >= 32 else { return false }
        var upper = false, lower = false, digit = false
        for scalar in token.unicodeScalars {
            switch scalar.value {
            case 65...90: upper = true
            case 97...122: lower = true
            case 48...57: digit = true
            default: break
            }
        }
        return upper && lower && digit && shannonEntropy(token) >= 4.0
    }

    private static func shannonEntropy(_ value: String) -> Double {
        var counts: [UInt16: Int] = [:]
        for unit in value.utf16 { counts[unit, default: 0] += 1 }
        let length = Double(value.utf16.count)
        return counts.values.reduce(0) { entropy, count in
            let p = Double(count) / length
            return entropy - p * log2(p)
        }
    }

    private func replaceRandomTokens(in text: String) -> String {
        let ns = text as NSString
        let matches = Self.randomToken.matches(in: text, range: NSRange(location: 0, length: ns.length))
        guard !matches.isEmpty else { return text }
        let result = NSMutableString(string: text)
        for match in matches.reversed() where Self.looksRandom(ns.substring(with: match.range)) {
            result.replaceCharacters(in: match.range, with: Self.redactedToken)
        }
        return result as String
    }

    private func buildIdentityRules() {
        if var home = options.homeDirectory, home.count > 3 {
            while home.hasSuffix("/") { home.removeLast() }
            if let rule = try? NSRegularExpression(pattern: NSRegularExpression.escapedPattern(for: home) + #"(?![\w.-])"#, options: [.caseInsensitive]) {
                identityRules.append((rule, "~"))
            }
        }
        addWholeWordRule(options.userName, replacement: "[USER]")
        addWholeWordRule(options.hostName, replacement: "[HOST]")
    }

    private func addWholeWordRule(_ word: String?, replacement: String) {
        guard let word, word.count >= 3, !Self.genericAccountNames.contains(word.lowercased()) else { return }
        if let rule = try? NSRegularExpression(pattern: #"(?<![\w.-])"# + NSRegularExpression.escapedPattern(for: word) + #"(?![\w-])"#, options: [.caseInsensitive]) {
            identityRules.append((rule, NSRegularExpression.escapedTemplate(for: replacement)))
        }
    }

    private func replace(_ pattern: NSRegularExpression, in text: String, with template: String) -> String {
        pattern.stringByReplacingMatches(in: text, range: NSRange(location: 0, length: (text as NSString).length), withTemplate: template)
    }

    private static func regex(_ pattern: String) -> NSRegularExpression {
        // Patterns are constants; a failure here is a programming error caught by the shared-spec tests.
        try! NSRegularExpression(pattern: pattern)
    }
}
