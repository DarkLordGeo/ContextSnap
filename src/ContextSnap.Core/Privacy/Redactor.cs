using System.Text.RegularExpressions;
using ContextSnap.Configuration;

namespace ContextSnap.Privacy;

public sealed record RedactionOptions(
    bool RedactSecrets = true,
    bool RedactEmails = true,
    bool HideUserIdentity = false,
    bool StripUrlQueryStrings = true,
    IReadOnlyList<string>? CustomTerms = null,
    string? UserProfilePath = null,
    string? UserName = null,
    string? MachineName = null)
{
    public static RedactionOptions FromSettings(AppSettings settings) => new(
        settings.RedactSecrets,
        settings.RedactEmails,
        settings.HideUserIdentity,
        settings.StripUrlQueryStrings,
        settings.CustomRedactionTerms,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.UserName,
        Environment.MachineName);
}

/// <summary>
/// Best-effort, pattern-based redaction applied to every text value before it is written to
/// Markdown. It catches common credential formats but cannot recognize every secret.
/// </summary>
public sealed partial class Redactor
{
    public const string Redacted = "[REDACTED]";
    public const string RedactedEmail = "[REDACTED_EMAIL]";
    public const string RedactedToken = "[REDACTED_TOKEN]";
    public const string RedactedPrivateKey = "[REDACTED_PRIVATE_KEY]";
    public const string UserPlaceholder = "[USER]";
    public const string HostPlaceholder = "[HOST]";

    private const int MinIdentityLength = 3;
    private const int MinRandomTokenLength = 32;
    private const double MinRandomTokenEntropy = 4.0;

    private static readonly HashSet<string> GenericAccountNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "users", "admin", "administrator", "owner", "dev", "test", "guest", "pc", "home", "desktop",
    };

    private readonly RedactionOptions _options;
    private readonly List<(Regex Pattern, string Replacement)> _identityRules = [];
    private readonly Regex? _customTerms;

    public Redactor(RedactionOptions options)
    {
        _options = options;

        if (options.HideUserIdentity)
            BuildIdentityRules(options);

        var terms = (options.CustomTerms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Regex.Escape(t.Trim()))
            .ToArray();
        if (terms.Length > 0)
            _customTerms = new Regex(string.Join('|', terms), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Secrets and email redaction only; no identity or custom terms.</summary>
    public static Redactor Default { get; } = new(new RedactionOptions());

    public RedactionOptions Options => _options;

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text;

        if (_options.RedactSecrets)
            result = RedactSecrets(result);

        if (_options.RedactEmails)
            result = EmailAddress().Replace(result, RedactedEmail);

        foreach (var (pattern, replacement) in _identityRules)
            result = pattern.Replace(result, replacement);

        if (_customTerms is not null)
            result = _customTerms.Replace(result, Redacted);

        return result;
    }

    /// <summary>Optionally strips query string and fragment, then applies <see cref="Redact"/>.</summary>
    public string RedactUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        var value = url.Trim();
        if (_options.StripUrlQueryStrings)
        {
            var cut = value.IndexOfAny(['?', '#']);
            if (cut >= 0)
                value = value[..cut];
        }

        return Redact(value);
    }

    private static string RedactSecrets(string text)
    {
        var result = PrivateKeyBlock().Replace(text, RedactedPrivateKey);
        result = UrlCredentials().Replace(result, "$1" + Redacted + "@");
        result = AuthorizationHeader().Replace(result, "$1" + Redacted);
        result = BearerToken().Replace(result, "$1 " + Redacted);
        result = BasicCredentials().Replace(result, "$1 " + Redacted);
        result = JsonWebToken().Replace(result, RedactedToken);
        result = KnownTokenFormat().Replace(result, RedactedToken);
        result = ConnectionStringSecret().Replace(result, "$1=" + Redacted);
        result = SensitiveQueryParameter().Replace(result, "$1" + Redacted);
        result = SecretAssignment().Replace(result, "${key}${sep}${quote}" + Redacted);
        result = RandomLookingToken().Replace(result, m => LooksRandom(m.Value) ? RedactedToken : m.Value);
        return result;
    }

    private void BuildIdentityRules(RedactionOptions options)
    {
        var profile = options.UserProfilePath?.TrimEnd('\\', '/');
        if (!string.IsNullOrWhiteSpace(profile) && profile.Length > 3)
        {
            foreach (var variant in new[] { profile, profile.Replace('\\', '/') }.Distinct())
            {
                _identityRules.Add((
                    new Regex(Regex.Escape(variant) + @"(?![\w.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    "~"));
            }
        }

        AddWholeWordRule(options.UserName, UserPlaceholder);
        AddWholeWordRule(options.MachineName, HostPlaceholder);
    }

    private void AddWholeWordRule(string? word, string replacement)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < MinIdentityLength || GenericAccountNames.Contains(word))
            return;

        _identityRules.Add((
            new Regex(@"(?<![\w.-])" + Regex.Escape(word) + @"(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            replacement));
    }

    internal static bool LooksRandom(string token)
    {
        if (token.Length < MinRandomTokenLength)
            return false;

        bool upper = false, lower = false, digit = false;
        foreach (var c in token)
        {
            upper |= char.IsAsciiLetterUpper(c);
            lower |= char.IsAsciiLetterLower(c);
            digit |= char.IsAsciiDigit(c);
        }

        return upper && lower && digit && ShannonEntropy(token) >= MinRandomTokenEntropy;
    }

    private static double ShannonEntropy(string value)
    {
        var counts = new Dictionary<char, int>();
        foreach (var c in value)
            counts[c] = counts.GetValueOrDefault(c) + 1;

        double entropy = 0;
        foreach (var count in counts.Values)
        {
            var p = (double)count / value.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 ]*PRIVATE KEY-----|$)")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"(?i)\b([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"(?i)\b(authorization\s*[:=]\s*[""']?(?:bearer|basic|token|digest)\s+)(?!\[REDACTED)[^\s""']+")]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"(?i)\b(bearer)\s+(?!\[REDACTED)[A-Za-z0-9\-._~+/]{16,}=*")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)\b(basic)\s+[A-Za-z0-9+/]{16,}={0,2}")]
    private static partial Regex BasicCredentials();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(
        @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}" +
        @"|github_pat_[A-Za-z0-9_]{20,}" +
        @"|glpat-[A-Za-z0-9_-]{20,}" +
        @"|xox[abposr]-[A-Za-z0-9-]{10,}" +
        @"|(?:AKIA|ASIA)[0-9A-Z]{16}" +
        @"|AIza[0-9A-Za-z_-]{35}" +
        @"|[rsp]k_(?:live|test)_[0-9A-Za-z]{16,}" +
        @"|sk-(?:ant-|proj-)?[A-Za-z0-9_-]{20,}" +
        @"|npm_[A-Za-z0-9]{36}" +
        @"|hf_[A-Za-z0-9]{30,})")]
    private static partial Regex KnownTokenFormat();

    [GeneratedRegex(@"(?i)\b(password|pwd|accountkey|sharedaccesskey|sharedaccesssignature)\s*=\s*(?!\[REDACTED)[^;\r\n]+")]
    private static partial Regex ConnectionStringSecret();

    [GeneratedRegex(@"(?i)([?&](?:access_token|refresh_token|id_token|token|key|api_key|apikey|sig|signature|code|password|secret|auth|session|sid)=)[^&#\s]+")]
    private static partial Regex SensitiveQueryParameter();

    [GeneratedRegex(
        @"(?i)\b(?<key>[a-z0-9_.-]*?(?:api[_-]?key|apikey|access[_-]?key|secret|token|password|passwd|pwd|passphrase|credentials?|private[_-]?key|client[_-]?secret|account[_-]?key|session[_-]?id|sessionid|auth[_-]?token|authorization))" +
        @"(?<sep>\s*[:=]\s*)(?<quote>[""']?)(?!\[REDACTED|(?:bearer|basic|token|digest)\s)(?<value>[^\s""'&;,]+)")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"(?i)(?<![\w.+-])[\w.+-]+@[a-z0-9-]+(?:\.[a-z0-9-]+)*\.[a-z]{2,}\b")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{32,}={0,2}")]
    private static partial Regex RandomLookingToken();
}
