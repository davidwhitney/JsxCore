using System.Text.Json.Serialization;

namespace SampleApp.Models.Catalogue;

// Deliberately named "Product" as well: a second namespace with the same simple name is exactly
// the case a single flat module could not express.

/// <summary>A catalogue listing, distinct from the storefront product.</summary>
public sealed record Product(string Code, string Description, Availability Availability);
