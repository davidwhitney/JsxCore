namespace JsxCore.Compilation.Assets;

/// <summary>What kind of problem linking ran into.</summary>
/// <remarks>
/// The two are not the same thing and should not read the same. A tool that is absent leaves a
/// feature unavailable, which a project may not be using. A tool that is present and refuses the
/// work leaves output that is wrong rather than merely incomplete, and a build that produced it has
/// nothing to be pleased about.
/// </remarks>
public enum AssetProblem
{
    /// <summary>Something needed is not there. What it would have produced is simply absent.</summary>
    Missing,

    /// <summary>Something was there and did not do the work it was asked to do.</summary>
    Failed
}

/// <summary>A problem found while linking, and how much it matters.</summary>
public sealed record AssetDiagnostic(AssetProblem Problem, string Message)
{
    public static AssetDiagnostic Missing(string message) => new(AssetProblem.Missing, message);

    public static AssetDiagnostic Failed(string message) => new(AssetProblem.Failed, message);
}
