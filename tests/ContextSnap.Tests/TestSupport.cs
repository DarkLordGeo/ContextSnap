using ContextSnap.Collectors.Git;

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
