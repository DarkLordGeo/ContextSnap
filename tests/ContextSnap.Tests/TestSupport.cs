using ContextSnap.Collectors.Git;
using ContextSnap.Collectors.ScreenText;

namespace ContextSnap.Tests;

/// <summary>A fact that is reported as skipped, not passed, when Git is not installed.</summary>
public sealed class GitFactAttribute : FactAttribute
{
    private static readonly bool GitAvailable = new GitProcessRunner().IsAvailable;

    public GitFactAttribute()
    {
        if (!GitAvailable)
            Skip = "Git is not installed (git.exe was not found on PATH).";
    }
}

/// <summary>
/// A fact that needs a real, interactive Windows desktop (windows, UI Automation, optionally OCR). It is
/// reported as skipped, not passed, on CI and in non-interactive sessions.
/// </summary>
public sealed class InteractiveDesktopFactAttribute : FactAttribute
{
    private static readonly string? DesktopUnavailable =
        Environment.GetEnvironmentVariable("CI") is not null ? "Needs an interactive Windows desktop; skipped on CI."
        : !Environment.UserInteractive ? "Needs an interactive Windows desktop; this session is not interactive."
        : null;

    private static readonly Lazy<string?> OcrUnavailable = new(() =>
    {
        try
        {
            return new WindowsOcrReader().UnavailableReason;
        }
        catch (Exception ex)
        {
            return $"Windows OCR could not be initialized ({ex.GetType().Name}).";
        }
    });

    public InteractiveDesktopFactAttribute()
    {
        Skip = DesktopUnavailable;
    }

    /// <summary>Also skip, with the reason, when no Windows OCR language is installed.</summary>
    public bool RequiresOcr
    {
        get => _requiresOcr;
        set
        {
            _requiresOcr = value;
            if (value && Skip is null && OcrUnavailable.Value is { } reason)
                Skip = "Needs Windows OCR: " + reason;
        }
    }

    private bool _requiresOcr;
}
