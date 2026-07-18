using JsxCore.TypeScript;

namespace JsxCore.Tests.Elsewhere;

// Deliberately not a "Models" namespace: the attribute is the escape hatch for types that live
// somewhere the convention does not reach.

/// <summary>Opted in with the attribute despite living outside a models namespace.</summary>
[JsxModel]
public sealed record ExportedFromElsewhere(string Name);

/// <summary>Not opted in, so the convention should leave it alone.</summary>
public sealed record NotExportedFromElsewhere(string Name);
