namespace JsxCore.Tests.Conventional.ModelBinding;

// "ModelBinding" starts with "Model" but is not a models namespace; the convention matches whole
// namespace segments, not prefixes.

/// <summary>Should not be picked up by the "Models" convention.</summary>
public sealed record NotAModel(string Name);
