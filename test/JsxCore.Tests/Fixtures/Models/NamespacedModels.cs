// Types in namespaces below the assembly's own, which is what produces a module per namespace.
// The models elsewhere sit directly in "JsxCore.Tests", the assembly name, and that namespace is
// the root module rather than a separate one.

namespace JsxCore.Tests.Catalogue
{
    public sealed record Listing(string Code, Pricing.Money Price);
}

namespace JsxCore.Tests.Catalogue.Pricing
{
    public sealed record Money(decimal Amount, string Currency);
}
