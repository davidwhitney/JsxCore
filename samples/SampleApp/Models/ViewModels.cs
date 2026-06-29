using System.Text.Json.Serialization;

namespace SampleApp.Models;

// Ordinary .NET types with no JsxCore attributes on them at all. They live in a "Models"
// namespace, which the default convention exports automatically, so a view model is described
// once, in C#, and the TSX side cannot drift from it.

/// <summary>Model for the client-rendered home page.</summary>
public sealed record IndexModel(string Greeting, IReadOnlyList<string> Features, DateTimeOffset GeneratedAt);

/// <summary>Model for the server-rendered team page.</summary>
public sealed record TeamModel(string Heading, IReadOnlyList<TeamMember> Rows);

/// <summary>A person on the team page.</summary>
public sealed record TeamMember(string Name, string Role, DateOnly Joined)
{
    /// <summary>Not sent to the browser.</summary>
    [JsonIgnore]
    public string InternalNotes { get; init; } = "";
}

/// <summary>Model for the Preact feature page.</summary>
public sealed record CatalogueModel(string Heading, IReadOnlyList<Product> Products)
{
    /// <summary>Absent when the catalogue has no featured item.</summary>
    public Product? Featured { get; init; }
}

/// <summary>A product, showing how enums and nested types come through.</summary>
public sealed record Product(int Id, string Name, decimal Price, Availability Availability)
{
    /// <summary>Free-form attributes, which become a Record in TypeScript.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();

    [JsonPropertyName("sku")]
    public string StockKeepingUnit { get; init; } = "";
}

/// <summary>Stock state. Serialised as a string, so the generated type is a string union.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Availability>))]
public enum Availability
{
    /// <summary>Available now.</summary>
    InStock,
    /// <summary>Available to order.</summary>
    Backordered,
    /// <summary>No longer sold.</summary>
    Discontinued
}
