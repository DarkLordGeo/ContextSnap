using ContextSnap.Privacy;

namespace ContextSnap.Tests;

public class RedactorTests
{
    [Theory]
    [InlineData("api_key=abc123", "api_key=[REDACTED]")]
    [InlineData("password: hunter2", "password: [REDACTED]")]
    [InlineData("GITHUB_TOKEN=\"s3cr3t\"", "GITHUB_TOKEN=\"[REDACTED]\"")]
    [InlineData("client_secret = 'xyz'", "client_secret = '[REDACTED]'")]
    [InlineData("Contact me at person@example.com", "Contact me at [REDACTED_EMAIL]")]
    [InlineData("Server=db;User Id=sa;Password=p@ss word;Database=x", "Server=db;User Id=sa;Password=[REDACTED];Database=x")]
    [InlineData("curl -H \"Authorization: Bearer abcdefghijklmnop1234\"", "curl -H \"Authorization: Bearer [REDACTED]\"")]
    [InlineData("https://admin:hunter2@example.org/path", "https://[REDACTED]@example.org/path")]
    [InlineData("https://x.test/cb?code=abc&state=1", "https://x.test/cb?code=[REDACTED]&state=1")]
    public void Redact_RemovesSensitiveValues(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Default.Redact(input));
    }

    [Theory]
    [InlineData("ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnop")]
    [InlineData("glpat-abcdefghijklmnopqrstu")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("AIzaSyA1234567890abcdefghijklmnopqrstuv")]
    [InlineData("sk_live_abcdefghijklmnop1234")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("Q2xpZW50U2VjcmV0X2Zvcl9hcHBfMTIzNDU2Nzg5MA9xZ")]
    public void Redact_RemovesKnownTokenFormats(string token)
    {
        var result = Redactor.Default.Redact($"value {token} end");

        Assert.DoesNotContain(token, result);
        Assert.Contains("[REDACTED", result);
        Assert.StartsWith("value ", result);
        Assert.EndsWith(" end", result);
    }

    [Fact]
    public void Redact_RemovesPrivateKeyBlocks()
    {
        var input = "before -----BEGIN RSA PRIVATE KEY-----\nMIIEow\nabc\n-----END RSA PRIVATE KEY----- after";

        Assert.Equal("before [REDACTED_PRIVATE_KEY] after", Redactor.Default.Redact(input));
    }

    [Theory]
    [InlineData("Program.cs - ContextSnap - Visual Studio Code")]
    [InlineData("ed0064a Apply redaction and output limits")]
    [InlineData("C:\\Users\\Public\\Documents\\report_final_v2.docx")]
    [InlineData("3f786850e387550fdab836ed7e6dc881de23001b")]
    [InlineData("Token count: tokens_used=42")]
    [InlineData("git@github")]
    [InlineData("Basic setup guide")]
    [InlineData("MyVeryLongComponentNameForTestingStuff")]
    public void Redact_LeavesOrdinaryTextUnchanged(string input)
    {
        Assert.Equal(input, Redactor.Default.Redact(input));
    }

    [Fact]
    public void Redact_CanDisableSecretAndEmailRedaction()
    {
        var redactor = new Redactor(new RedactionOptions(RedactSecrets: false, RedactEmails: false));

        Assert.Equal("password=x a@b.com", redactor.Redact("password=x a@b.com"));
    }

    [Fact]
    public void Redact_HidesUserProfileUserNameAndHost()
    {
        var redactor = new Redactor(new RedactionOptions(
            HideUserIdentity: true,
            UserProfilePath: @"C:\Users\alice",
            UserName: "alice",
            MachineName: "WORKSTATION-7"));

        Assert.Equal(@"~\Desktop\app", redactor.Redact(@"C:\Users\alice\Desktop\app"));
        Assert.Equal("~/repo", redactor.Redact("c:/users/alice/repo"));
        Assert.Equal("[USER]@[HOST]: ~", redactor.Redact("alice@workstation-7: ~"));
        Assert.Equal(@"C:\Users\alicex\file", redactor.Redact(@"C:\Users\alicex\file"));
        Assert.Equal("malice", redactor.Redact("malice"));
    }

    [Fact]
    public void Redact_SkipsGenericAccountNames()
    {
        var redactor = new Redactor(new RedactionOptions(HideUserIdentity: true, UserName: "admin"));

        Assert.Equal("WordPress admin panel", redactor.Redact("WordPress admin panel"));
    }

    [Fact]
    public void Redact_AppliesCustomTermsCaseInsensitively()
    {
        var redactor = new Redactor(new RedactionOptions(CustomTerms: ["Project Falcon", "acme"]));

        Assert.Equal("[REDACTED] for [REDACTED] Corp", redactor.Redact("project falcon for ACME Corp"));
    }

    [Theory]
    [InlineData("https://example.com/a?token=1#frag", true, "https://example.com/a")]
    [InlineData("https://example.com/a?page=2", false, "https://example.com/a?page=2")]
    [InlineData("https://example.com/a?api_key=abc&page=2", false, "https://example.com/a?api_key=[REDACTED]&page=2")]
    public void RedactUrl_HandlesQueryStrings(string url, bool strip, string expected)
    {
        var redactor = new Redactor(new RedactionOptions(StripUrlQueryStrings: strip));

        Assert.Equal(expected, redactor.RedactUrl(url));
    }

    [Fact]
    public void Redact_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, Redactor.Default.Redact(null));
        Assert.Equal(string.Empty, Redactor.Default.Redact(string.Empty));
    }
}
