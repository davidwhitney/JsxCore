using JsxCore.Analyzers;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Build;

/// <summary>
/// The annotations that let an IDE resolve a View() call returning a .tsx file.
/// </summary>
public class ViewLocationAnnotationsTests
{
    private static ViewLocationSettings Settings(
        string views = "Views",
        bool defineAttributes = true) =>
        new(Enabled: true,
            ViewsDirectory: views,
            Extensions: [".tsx"],
            ViewFormats: ViewLocationAnnotations.DefaultViewLocationFormats,
            AreaFormats: ViewLocationAnnotations.DefaultAreaViewLocationFormats,
            DefineAttributes: defineAttributes);

    [Fact]
    public void Defaults_ComparedWithJsxCoreOptions_Agree()
    {
        // An analyzer loads into the compiler, so it cannot reference JsxCore and has to repeat
        // these. This is what stops the copy drifting.
        var options = new JsxCoreOptions();

        ViewLocationAnnotations.DefaultViewLocationFormats.ShouldBe(options.ViewLocationFormats);
        ViewLocationAnnotations.DefaultAreaViewLocationFormats.ShouldBe(options.AreaViewLocationFormats);
        ViewLocationAnnotations.DefaultExtensions.ShouldBe(options.Extensions);
    }

    [Fact]
    public void Emit_DefaultSettings_ProducesAnAnnotationPerViewLocation()
    {
        var source = ViewLocationAnnotations.Emit(Settings());

        source.ShouldContain("""[assembly: JetBrains.Annotations.AspMvcViewLocationFormat("~/Views/{1}/{0}.tsx")]""");
        source.ShouldContain("""[assembly: JetBrains.Annotations.AspMvcViewLocationFormat("~/Views/Shared/{0}.tsx")]""");
        source.ShouldContain("""[assembly: JetBrains.Annotations.AspMvcAreaViewLocationFormat("~/Areas/{2}/Views/{1}/{0}.tsx")]""");
    }

    [Fact]
    public void Emit_AttributesAreDefined_PlacesAssemblyAttributesFirst()
    {
        // C# requires it, and getting this wrong fails the consuming build rather than ours.
        var source = ViewLocationAnnotations.Emit(Settings());

        source.IndexOf("[assembly:", StringComparison.Ordinal)
            .ShouldBeLessThan(source.IndexOf("namespace JetBrains.Annotations", StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_ViewsDirectoryIsRelocated_FollowsIt()
    {
        ViewLocationAnnotations.Emit(Settings(views: "Client/Pages"))
            .ShouldContain("~/Client/Pages/{1}/{0}.tsx");
    }

    [Fact]
    public void Emit_ProjectAlreadyHasTheAttributes_LeavesThemOut()
    {
        var source = ViewLocationAnnotations.Emit(Settings(defineAttributes: false));

        source.ShouldNotContain("namespace JetBrains.Annotations");
        source.ShouldContain("[assembly: JetBrains.Annotations.AspMvcViewLocationFormat");
    }

    [Fact]
    public void Emit_AttributesAreDefined_MarksThemConditionalSoTheyAreNotCompiled()
    {
        // Conditional on a symbol nothing defines, so this is metadata for the editor only.
        ViewLocationAnnotations.Emit(Settings())
            .ShouldContain("""System.Diagnostics.Conditional("JSXCORE_ANNOTATIONS_NEVER_DEFINED")""");
    }
}
