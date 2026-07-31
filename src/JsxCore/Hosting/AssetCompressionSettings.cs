namespace JsxCore.Hosting;

/// <summary>Whether responses for assets are compressed.</summary>
/// <remarks>
/// A type of its own so it can be resolved from the container: the decision is made once during
/// registration, from the project file, the options and the environment together.
/// </remarks>
public sealed record AssetCompressionSettings(bool Enabled);
