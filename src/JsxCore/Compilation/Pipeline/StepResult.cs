namespace JsxCore.Compilation.Pipeline;

public sealed record StepResult(string? Fingerprint = null)
{
    public static readonly StepResult None = new();
}
