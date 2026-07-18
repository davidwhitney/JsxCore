namespace JsxCore.Tests.Shadowing;

// A namespace that declares a type whose simple name matches a namespace-less one, and references
// both: the case where a bare reference would silently bind to the wrong declaration.

/// <summary>Shadows the namespace-less GlobalNamespaceModel.</summary>
internal sealed record GlobalNamespaceModel(int Number);

/// <summary>Forces the generator to disambiguate.</summary>
internal sealed record Shadow(GlobalNamespaceModel Local, global::GlobalNamespaceModel Global);
