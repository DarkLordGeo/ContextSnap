using System.Text.RegularExpressions;

namespace ContextSnap.Formatting;

public static partial class PrivacyFilter
{
    [GeneratedRegex(@"(?i)\b(api[_-]?key|access[_-]?token|password|secret)\s*[:=]\s*['""]?[^'""\s,;]+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"(?i)\b[\w.+-]+@[\w.-]+\.[a-z]{2,}\b")]
    private static partial Regex EmailAddress();

    public static string Redact(string input)
    {
        var result = SecretAssignment().Replace(input, "$1=[REDACTED]");
        return EmailAddress().Replace(result, "[REDACTED_EMAIL]");
    }
}
