using ContextSnap.Formatting;

namespace ContextSnap.Tests;

public class PrivacyFilterTests
{
    [Theory]
    [InlineData("api_key=abc123", "api_key=[REDACTED]")]
    [InlineData("password: hunter2", "password=[REDACTED]")]
    [InlineData("Contact me at person@example.com", "Contact me at [REDACTED_EMAIL]")]
    public void Redact_RemovesSensitiveValues(string input, string expected)
    {
        Assert.Equal(expected, PrivacyFilter.Redact(input));
    }

    [Fact]
    public void LimitOutput_TruncatesLongMarkdown()
    {
        var result = MarkdownFormatter.LimitOutput(new string('x', 100), 40);

        Assert.True(result.Length <= 40);
        Assert.Contains("truncated", result);
    }

    [Fact]
    public void LimitOutput_LeavesShortMarkdownUnchanged()
    {
        Assert.Equal("# Hello", MarkdownFormatter.LimitOutput("# Hello"));
    }
}
