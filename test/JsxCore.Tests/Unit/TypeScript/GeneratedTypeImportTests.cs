using System.Text.Json;
using JsxCore.Tests.Catalogue;
using JsxCore.Tests.Catalogue.Pricing;
using JsxCore.Tests.Fixtures;
using JsxCore.TypeScript;
using Shouldly;

namespace JsxCore.Tests.Unit.TypeScript;

/// <summary>
/// Every way a view can import the types generated from .NET.
/// </summary>
/// <remarks>
/// These compile real views with the real compiler rather than asserting on generated text,
/// because the thing being tested is whether TypeScript resolves the specifier and binds the type.
/// A declaration file that looks right and does not resolve would pass any lesser test.
/// </remarks>
public class GeneratedTypeImportTests
{
    /// <param name="typeChecking">
    /// Error for the cases that must compile, so a diagnostic fails the test loudly. Warn for the
    /// cases that must not, because Error throws instead of returning the failed build to assert on.
    /// </param>
    private static JsxProjectFixture Project(TypeCheckingMode typeChecking = TypeCheckingMode.Error)
    {
        var project = JsxProjectFixture.Create();
        project.Options.TypeDefinitions.Add<Listing>();
        project.Options.TypeDefinitions.Add<Money>();
        project.Options.TypeChecking = typeChecking;
        return project;
    }

    [Fact]
    public async Task RootImport_TypeQualifiedByItsNamespace_Resolves()
    {
        using var project = Project();
        project.AddView("Home/Index.tsx", """
            import type JsxCore from "dotnet:JsxCore.Tests";

            export default function Index({ model }: { model: JsxCore.Tests.Catalogue.Listing }) {
                return <p>{model.code}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
    }

    [Fact]
    public async Task NamespaceImport_NamesTheDotNetNamespace_Resolves()
    {
        // The form a .NET developer reaches for: the import path is the namespace.
        using var project = Project();
        project.AddView("Home/Index.tsx", """
            import type { Listing } from "dotnet:JsxCore.Tests/Catalogue";

            export default function Index({ model }: { model: Listing }) {
                return <p>{model.code}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
    }

    [Fact]
    public async Task NestedNamespaceImport_Resolves()
    {
        using var project = Project();
        project.AddView("Home/Index.tsx", """
            import type { Money } from "dotnet:JsxCore.Tests/Catalogue/Pricing";

            export default function Index({ model }: { model: Money }) {
                return <p>{model.amount} {model.currency}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
    }

    [Fact]
    public async Task BothForms_UsedInOneView_AgreeOnTheType()
    {
        // The namespace module aliases the root rather than redeclaring anything, so the two names
        // have to be the same type. Assigning one to the other is what proves it.
        using var project = Project();
        project.AddView("Home/Index.tsx", """
            import type JsxCore from "dotnet:JsxCore.Tests";
            import type { Listing } from "dotnet:JsxCore.Tests/Catalogue";

            export default function Index({ model }: { model: Listing }) {
                const sameType: JsxCore.Tests.Catalogue.Listing = model;
                return <p>{sameType.code}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
    }

    [Fact]
    public async Task NamespaceImport_ViewMisusesTheType_FailsToCompile()
    {
        // Resolving is half of it: a facade that resolved to any would pass every test above.
        using var project = Project(TypeCheckingMode.Warn);
        project.AddView("Home/Index.tsx", """
            import type { Listing } from "dotnet:JsxCore.Tests/Catalogue";

            export default function Index({ model }: { model: Listing }) {
                return <p>{model.doesNotExist}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeFalse();
        build.Result.Errors.ShouldContain(d => d.Message.Contains("doesNotExist"));
    }

    [Fact]
    public async Task NestedNamespaceImport_ViewMisusesTheType_FailsToCompile()
    {
        using var project = Project(TypeCheckingMode.Warn);
        project.AddView("Home/Index.tsx", """
            import type { Money } from "dotnet:JsxCore.Tests/Catalogue/Pricing";

            export default function Index({ model }: { model: Money }) {
                const wrong: Money = { amount: "not a number", currency: "GBP" };
                return <p>{wrong.currency}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeFalse();
        build.Result.Errors.ShouldContain(d => d.Message.Contains("not assignable"));
    }

    [Fact]
    public async Task NamespaceImport_NamespaceDoesNotExist_FailsToResolve()
    {
        using var project = Project(TypeCheckingMode.Warn);
        project.AddView("Home/Index.tsx", """
            import type { Listing } from "dotnet:JsxCore.Tests/NoSuchNamespace";

            export default function Index({ model }: { model: Listing }) {
                return <p>{model.code}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Generate_TypesBelowTheAssemblyNamespace_ProduceAModuleEach()
    {
        var files = Generate();

        var specifiers = files.Select(file => file.ModuleSpecifier).ToList();

        specifiers.ShouldContain("dotnet:JsxCore.Tests");
        specifiers.ShouldContain("dotnet:JsxCore.Tests/Catalogue");
        specifiers.ShouldContain("dotnet:JsxCore.Tests/Catalogue/Pricing");
    }

    [Fact]
    public void Generate_NamespaceModule_AliasesTheRootRatherThanRedeclaring()
    {
        // Declaring a type twice would let the two drift, and would break references between
        // namespaces, which resolve because everything is declared in one place.
        var module = Generate().Single(file => file.ModuleSpecifier == "dotnet:JsxCore.Tests/Catalogue");

        module.Contents.ShouldContain("""import type JsxCore from "dotnet:JsxCore.Tests";""");
        module.Contents.ShouldContain("export type Listing = JsxCore.Tests.Catalogue.Listing;");
        module.Contents.ShouldNotContain("interface");
    }

    [Fact]
    public void Generate_NamespaceIsTheAssemblyItself_HasNoModuleOfItsOwn()
    {
        // "dotnet:JsxCore.Tests/" is not a thing; that namespace is the root module.
        var paths = Generate().Select(file => file.RelativePath).ToList();

        paths.ShouldContain("JsxCore.Tests.d.ts");
        paths.ShouldNotContain(path => path.EndsWith(Path.DirectorySeparatorChar + ".d.ts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReservedNames_ShareTheSchemeWithAssemblies_AndStillResolve()
    {
        // One scheme now covers assemblies and the two names JsxCore reserves. The exact entries
        // have to win over the wildcard, or "dotnet:rendering" would be looked for as an assembly
        // called rendering and resolve to nothing.
        using var project = Project();
        project.AddView("Home/Index.tsx", """
            import type { Listing } from "dotnet:JsxCore.Tests/Catalogue";
            import { isServerRender } from "dotnet:rendering";
            import type { ViewProps } from "dotnet:rendering";

            export default function Index({ model }: ViewProps<Listing>) {
                return <p>{isServerRender() ? model.code : "client"}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
    }

    [Theory]
    [InlineData("globals")]
    [InlineData("rendering")]
    public void Generate_AssemblyUsesAReservedName_FailsRatherThanBeingShadowed(string name)
    {
        // The one cost of putting assemblies and the built-in modules under one scheme. Silently
        // resolving the assembly's types to JsxCore's own module is the outcome worth avoiding.
        var options = new TypeDefinitionOptions { ApplicationAssembly = AssemblyNamed(name) };

        var exception = Should.Throw<JsxCoreException>(() =>
            new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions()).Generate());

        exception.Message.ShouldContain(name);
        exception.Message.ShouldContain("reserved");
    }

    [Theory]
    [InlineData("Rendering")]
    [InlineData("Globals")]
    [InlineData("MyCompany.Rendering")]
    public void Generate_AssemblyMerelyResemblesAReservedName_IsFine(string name)
    {
        // Matched exactly, so the names a real project would actually use are unaffected.
        var options = new TypeDefinitionOptions { ApplicationAssembly = AssemblyNamed(name) };

        Should.NotThrow(() => new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions()).Generate());
    }

    private static System.Reflection.Assembly AssemblyNamed(string name) =>
        System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName(name),
            System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);

    private static IReadOnlyList<GeneratedTypeScriptFile> Generate()
    {
        var options = new TypeDefinitionOptions();
        options.Add<Listing>();
        options.Add<Money>();

        return new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Generate().Files;
    }
}
