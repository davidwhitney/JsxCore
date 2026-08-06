using JsxCore.Rendering;
using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// Declaring the data types a registered global hands out as unchanging while a view can see them.
/// The option is internal for now, so these tests configure it directly rather than through
/// application configuration.
/// </summary>
public class ImmutableCrossingTests
{
    /// <summary>A view that reaches through two levels of .NET object and reads the deeper one twice.</summary>
    private const string WalkingView = """
        import { dotnet } from "dotnet:globals";
        export default function Index() {
            const catalog = (dotnet as any).Catalog;
            const product = catalog.getProduct();
            const text = product.pricing.currency + ":" + product.pricing.region;
            return <p>{text}</p>;
        }
        """;

    [Fact]
    public async Task DeclaredType_WalkedByAView_ReadsTheSameValues()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.Globals.Register("Catalog", new Catalog());
        project.Options.ServerRendering.ImmutableCrossingTypes.Add(typeof(Product));
        project.AddView("Home/Catalog.tsx", WalkingView);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Catalog");

        result.Html.ShouldBe("<p>EUR:eu</p>");
    }

    [Fact]
    public async Task DeclaredType_MemberReadTwice_ReachesDotnetOnce()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        var catalog = new Catalog();
        project.Options.Globals.Register("Catalog", catalog);
        project.AddView("Home/Catalog.tsx", WalkingView);
        await project.CompileAsync();

        var view = project.Locate("Home/Catalog");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new Dictionary<string, object?>();

        // Each renderer builds its engines when it first renders, so the declaration only reaches
        // the second one.
        var undeclared = project.CreateServerRenderer();
        await undeclared.RenderAsync(view, null, context, services);
        var withoutDeclaration = catalog.PricingReads;

        catalog.Reset();
        project.Options.ServerRendering.ImmutableCrossingTypes.Add(typeof(Product));

        var declared = project.CreateServerRenderer();
        await declared.RenderAsync(view, null, context, services);
        var withDeclaration = catalog.PricingReads;

        withoutDeclaration.ShouldBe(2);
        withDeclaration.ShouldBe(1);
    }

    /// <summary>The registered global, which hands out the record a view walks.</summary>
    public sealed class Catalog
    {
        private readonly Product _product = new();

        public Product GetProduct() => _product;

        /// <summary>How often a render has crossed back into .NET for the product's pricing.</summary>
        public int PricingReads => _product.PricingReads;

        public void Reset() => _product.Reset();
    }

    /// <summary>
    /// A record whose pricing never changes — and which counts how often it is asked for it.
    /// </summary>
    /// <remarks>
    /// The counter is the only thing here that moves, and no view can see it, so the promise a
    /// declaration makes holds: every read resolves to the same values. Counting is what makes
    /// "resolved once per object" observable at all, since two reads of an unchanging member are
    /// otherwise indistinguishable from the outside.
    /// </remarks>
    public sealed class Product
    {
        private readonly Pricing _pricing = new();

        public int PricingReads { get; private set; }

        public Pricing Pricing
        {
            get
            {
                PricingReads++;
                return _pricing;
            }
        }

        public void Reset() => PricingReads = 0;
    }

    public sealed class Pricing
    {
        public string Currency => "EUR";
        public string Region => "eu";
    }
}
