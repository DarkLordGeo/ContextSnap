using System.Text.Json;
using ContextSnap.Formatting;
using ContextSnap.Notifications;
using ContextSnap.Privacy;

namespace ContextSnap.Tests;

/// <summary>
/// Runs the cross-platform fixtures in <c>spec/</c>. The macOS implementation runs the same files,
/// so both platforms redact and format identically.
/// </summary>
public class SharedSpecTests
{
    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", name))).RootElement;

    public static TheoryData<string, string, string> RedactionCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var item in Load("redaction-cases.json").GetProperty("cases").EnumerateArray())
            data.Add(item.GetProperty("name").GetString()!, item.GetProperty("input").GetString()!, item.GetProperty("expected").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(RedactionCases))]
    public void Redaction_MatchesSharedSpec(string name, string input, string expected)
    {
        Assert.True(expected == Redactor.Default.Redact(input), name);
    }

    [Fact]
    public void Markdown_MatchesSharedSpec()
    {
        var spec = Load("markdown-cases.json");

        Assert.Equal(spec.GetProperty("truncationNotice").GetString(), MarkdownFormatter.TruncationNotice);
        foreach (var item in spec.GetProperty("escape").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetString(), MarkdownText.Escape(item.GetProperty("input").GetString()!));
        foreach (var item in spec.GetProperty("codeSpan").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetString(), MarkdownText.Code(item.GetProperty("input").GetString()!));
        foreach (var item in spec.GetProperty("fence").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetString(), MarkdownText.Fence(item.GetProperty("input").GetString()!));
        foreach (var item in spec.GetProperty("limitOutput").EnumerateArray())
        {
            Assert.Equal(item.GetProperty("expected").GetString(),
                MarkdownFormatter.LimitOutput(item.GetProperty("input").GetString()!, item.GetProperty("maxCharacters").GetInt32()));
        }

        foreach (var item in spec.GetProperty("textStatistics").EnumerateArray())
        {
            var stats = TextStatistics.Measure(item.GetProperty("input").GetString()!);
            Assert.Equal(item.GetProperty("characters").GetInt32(), stats.Characters);
            Assert.Equal(item.GetProperty("words").GetInt32(), stats.Words);
            Assert.Equal(item.GetProperty("utf8Bytes").GetInt64(), stats.Bytes);
        }

        foreach (var item in spec.GetProperty("formatSize").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetString(), MarkdownFormatter.FormatSize(item.GetProperty("bytes").GetInt64()));
    }
}
