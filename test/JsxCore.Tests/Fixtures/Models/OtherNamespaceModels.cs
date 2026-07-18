namespace JsxCore.Tests.Other;

// A second namespace, so the generator's collision handling has something real to work against.

/// <summary>Shares a simple name with JsxCore.Tests.Inner on purpose.</summary>
internal sealed record Inner(string Other);

/// <summary>References both Inners, which forces an aliased cross-module import.</summary>
internal sealed record Wrapper(Inner Own, Tests.Inner FromParent);
