using JsxCore.Tests.Conventional.Models;
using JsxCore.Tests.Conventional.Models.Nested;
using JsxCore.Tests.Conventional.ModelBinding;
using JsxCore.Tests.Elsewhere;
using JsxCore.TypeScript;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.TypeScript;

/// <summary>
/// Covers which .NET types get exported. The default matters most: a conventional MVC application
/// should get working model types without configuring anything at all.
/// </summary>
public class TypeSourceTests
{
    private static readonly System.Reflection.Assembly Here = typeof(TypeSourceTests).Assembly;

    [Fact]
    public void NamespaceContaining_NoOptionGiven_IncludesChildNamespaces()
    {
        var types = TypesFrom.NamespaceContaining<OrderModel>().Resolve();

        types.ShouldContain(typeof(OrderModel));
        types.ShouldContain(typeof(NestedModel));
    }

    [Fact]
    public void NamespaceContaining_ChildrenExcluded_ReturnsOnlyThatNamespace()
    {
        var types = TypesFrom.NamespaceContaining<OrderModel>(includeChildNamespaces: false).Resolve();

        types.ShouldContain(typeof(OrderModel));
        types.ShouldNotContain(typeof(NestedModel));
    }

    [Fact]
    public void Namespace_NamedDirectly_ReturnsItsTypes()
    {
        var types = TypesFrom.Namespace("JsxCore.Tests.Conventional.Models", Here).Resolve();

        types.ShouldContain(typeof(OrderModel));
    }

    [Fact]
    public void Scan_TypeCannotDescribeData_IsNotPickedUp()
    {
        var types = TypesFrom.NamespaceContaining<OrderModel>().Resolve();

        types.ShouldNotContain(typeof(SomeDelegate));
        types.ShouldNotContain(typeof(SomeAttribute));
        types.ShouldNotContain(typeof(SomeException));
        types.ShouldNotContain(typeof(StaticHelpers));
    }

    [Fact]
    public void Scan_TypeIsNotPublic_IsSkipped()
    {
        TypesFrom.NamespaceContaining<OrderModel>().Resolve()
            .ShouldAllBe(type => type.IsPublic || type.IsNestedPublic);
    }

    [Fact]
    public void Sources_Composed_NarrowToTheIntersection()
    {
        var combined = TypesFrom.Types(typeof(OrderModel)) + TypesFrom.Types(typeof(NestedModel));
        combined.Resolve().Count.ShouldBe(2);

        (combined | TypesFrom.Nothing).Resolve().Count.ShouldBe(2);
        combined.Except<NestedModel>().Resolve().ShouldHaveSingleItem().ShouldBe(typeof(OrderModel));
        combined.Where(t => t.Name.StartsWith("Order")).Resolve().ShouldHaveSingleItem();
    }

    [Fact]
    public void Sources_ResolvedTwice_AreLazyAndDeterministic()
    {
        var source = TypesFrom.NamespaceContaining<OrderModel>();

        // Same order every time, so generated output does not churn between runs.
        source.Resolve().ShouldBe(source.Resolve());
    }

    [Fact]
    public void None_Resolved_YieldsNoTypes()
    {
        TypesFrom.Nothing.Resolve().ShouldBeEmpty();
    }

    [Fact]
    public void ConventionalNamespace_NameIsAPrefixNotASegment_DoesNotMatch()
    {
        var types = TypesFrom.ConventionalNamespaces(Here, "Models").Resolve();

        types.ShouldContain(typeof(OrderModel));
        types.ShouldContain(typeof(NestedModel));

        // "ModelBinding" is not a models namespace, however much it looks like one.
        types.ShouldNotContain(typeof(NotAModel));
    }

    [Fact]
    public void DefaultConvention_NoConfiguration_ExportsModelNamespaces()
    {
        var options = new TypeDefinitionOptions { ApplicationAssembly = Here };

        var types = options.ResolveTypes();

        // Found purely because it sits in a "Models" namespace.
        types.ShouldContain(typeof(OrderModel));
        types.ShouldContain(typeof(NestedModel));
    }

    [Fact]
    public void DefaultConvention_TypeIsAttributedElsewhere_IsStillExported()
    {
        var options = new TypeDefinitionOptions { ApplicationAssembly = Here };

        var types = options.ResolveTypes();

        // Not in a models namespace; opted in with the attribute, which is what it is for.
        types.ShouldContain(typeof(ExportedFromElsewhere));
        types.ShouldNotContain(typeof(NotExportedFromElsewhere));
    }

    [Fact]
    public void AutoExport_ExplicitSourceIsSet_ReplacesTheConvention()
    {
        var options = new TypeDefinitionOptions
        {
            ApplicationAssembly = Here,
            AutoExport = TypesFrom.Types(typeof(NotAModel))
        };

        var types = options.ResolveTypes();

        types.ShouldContain(typeof(NotAModel));
        types.ShouldNotContain(typeof(OrderModel));
    }

    [Fact]
    public void AutoExport_TurnedOff_ExportsNothing()
    {
        var options = new TypeDefinitionOptions
        {
            ApplicationAssembly = Here,
            AutoExport = TypesFrom.Nothing
        };

        options.ResolveTypes().ShouldBeEmpty();
    }

    [Fact]
    public void AutoExport_TypeIsAddedExplicitly_SurvivesAlongsideTheSource()
    {
        var options = new TypeDefinitionOptions
        {
            ApplicationAssembly = Here,
            AutoExport = TypesFrom.Nothing
        };
        options.Add<NotAModel>();

        options.ResolveTypes().ShouldHaveSingleItem().ShouldBe(typeof(NotAModel));
    }

    [Fact]
    public void ConventionalNamespaceNames_Configured_AreUsedInsteadOfTheDefault()
    {
        var options = new TypeDefinitionOptions { ApplicationAssembly = Here };
        options.ConventionalNamespaceNames.Clear();
        options.ConventionalNamespaceNames.Add("Elsewhere");

        var types = options.ResolveTypes();

        types.ShouldContain(typeof(NotExportedFromElsewhere));
        types.ShouldNotContain(typeof(OrderModel));
    }

    [Fact]
    public void AllUserCode_Resolved_FindsApplicationTypesAndSkipsTheFramework()
    {
        var types = TypesFrom.AllUserCode.Resolve();

        types.ShouldContain(typeof(OrderModel));
        types.ShouldContain(typeof(NotAModel));

        types.ShouldNotContain(typeof(string));
        types.ShouldAllBe(type => type.Assembly != typeof(string).Assembly);
        types.ShouldAllBe(type => type.Assembly != typeof(TypeSource).Assembly);
    }

    [Fact]
    public void AssemblyScan_Resolved_CoversEveryModelLikeType()
    {
        var types = TypesFrom.AssemblyContaining<OrderModel>().Resolve();

        types.ShouldContain(typeof(OrderModel));
        types.ShouldContain(typeof(NotAModel));
        types.ShouldNotContain(typeof(SomeDelegate));
    }

    [Fact]
    public void MarkedTypes_RequestedAlone_ReturnsOnlyAttributedTypes()
    {
        var types = TypesFrom.MarkedTypesIn<TypeSourceTests>().Resolve();

        types.ShouldContain(typeof(ExportedFromElsewhere));
        types.ShouldNotContain(typeof(OrderModel));
    }

    [Fact]
    public void AutoExport_OnJsxCoreOptions_IsAvailableAtTheTopLevel()
    {
        // The shorthand the configuration callback is expected to use.
        var options = new JsxCoreOptions
        {
            AutoExport = TypesFrom.NamespaceContaining<OrderModel>(includeChildNamespaces: false)
        };

        options.TypeDefinitions.AutoExport.ShouldBeSameAs(options.AutoExport);
        options.AutoExport!.Resolve().ShouldContain(typeof(OrderModel));
    }
}
