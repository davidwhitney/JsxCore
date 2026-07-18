using System.Text.Json.Serialization;
using JsxCore.TypeScript;

// Model shapes the generator tests describe. Kept beside the other fixtures so their
// namespace does not follow whichever test file happens to use them.
namespace JsxCore.Tests;

[JsxModel(Name = "ProductSummary")]
internal sealed record Renamed(string Name);

/// <summary>Public so that attribute-based discovery can find it.</summary>
[JsxModel]
public sealed record MarkedForGeneration(string Name);

/// <summary>Public but unmarked, so discovery should skip it.</summary>
public sealed record NotMarkedForGeneration(string Name);

/// <summary>
/// The generated declarations have to describe the model as it arrives in JavaScript, not as it
/// exists in .NET. Every case here is one where those two differ.
/// </summary>

// Model types used by the tests above. Deliberately top-level: nested types are
// name-prefixed with their declaring type to avoid collisions, which is correct but not
// what a real view model looks like.

internal sealed record Primitives(string Text, int Count, decimal Price, long Big, bool Flag, Guid Id);

internal sealed record Temporal(DateTime Moment, DateTimeOffset Offset, DateOnly Day, TimeSpan Duration);

internal sealed record Annotated
{
    [JsonPropertyName("sku")]
    public string StockKeepingUnit { get; init; } = "";

    [JsonIgnore]
    public string Secret { get; init; } = "";
}

internal sealed record Nullables(string Required, string? OptionalText, int? OptionalNumber);

internal sealed record Collections(
    string[] Names,
    List<int> Numbers,
    IReadOnlyList<string> ReadOnly,
    Dictionary<string, int> Lookup);

internal sealed record Inner(string Label, int Value);

internal sealed record Outer(Inner Inner, IReadOnlyList<Inner> Many);

internal sealed record TreeNode(string Name, IReadOnlyList<TreeNode> Children);

internal enum PlainEnum { First, Second }

internal sealed record WithPlainEnum(PlainEnum Value);

[JsonConverter(typeof(JsonStringEnumConverter<StringEnum>))]
internal enum StringEnum { Alpha, Beta }

internal sealed record WithStringEnum(StringEnum Value);

internal sealed record AwkwardNames
{
    [JsonPropertyName("content-type")]
    public string ContentType { get; init; } = "";
}
