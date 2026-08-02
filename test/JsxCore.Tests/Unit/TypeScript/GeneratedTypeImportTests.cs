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
            import type JsxCore from "dotnet:types";

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
            import type { Listing } from "dotnet:types/JsxCore/Tests/Catalogue";

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
            import type { Money } from "dotnet:types/JsxCore/Tests/Catalogue/Pricing";

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
            import type JsxCore from "dotnet:types";
            import type { Listing } from "dotnet:types/JsxCore/Tests/Catalogue";

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
            import type { Listing } from "dotnet:types/JsxCore/Tests/Catalogue";

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
            import type { Money } from "dotnet:types/JsxCore/Tests/Catalogue/Pricing";

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
            import type { Listing } from "dotnet:types/JsxCore/Tests/NoSuchNamespace";

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

        specifiers.ShouldContain("dotnet:types");
        specifiers.ShouldContain("dotnet:types/JsxCore/Tests/Catalogue");
        specifiers.ShouldContain("dotnet:types/JsxCore/Tests/Catalogue/Pricing");
    }

    [Fact]
    public void Generate_NamespaceModule_AliasesTheRootRatherThanRedeclaring()
    {
        // Declaring a type twice would let the two drift, and would break references between
        // namespaces, which resolve because everything is declared in one place.
        var module = Generate().Single(file => file.ModuleSpecifier == "dotnet:types/JsxCore/Tests/Catalogue");

        module.Contents.ShouldContain("""import type JsxCore from "dotnet:types";""");
        module.Contents.ShouldContain("export type Listing = JsxCore.Tests.Catalogue.Listing;");
        module.Contents.ShouldNotContain("interface");
    }

    [Fact]
    public void Generate_NamespaceModules_CarryTheWholeNamespacePath()
    {
        // Nothing is named after an assembly, so there is no assembly prefix to shed: the path is
        // the .NET namespace, whichever assembly happened to declare it.
        var paths = Generate().Select(file => file.RelativePath).ToList();

        paths.ShouldContain("types.d.ts");
        paths.ShouldContain(Path.Combine("types", "JsxCore", "Tests", "Catalogue.d.ts"));
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
            import type { Listing } from "dotnet:types/JsxCore/Tests/Catalogue";
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
    public void Generate_AssemblyUsesAReservedName_IsNoLongerAProblem(string name)
    {
        // It used to be: the module was named after the assembly, so an assembly called "globals"
        // was shadowed by JsxCore's own module and its types silently resolved to the wrong one.
        // Nothing is named after an assembly now, so the collision cannot arise.
        var options = new TypeDefinitionOptions { ApplicationAssembly = AssemblyNamed(name) };
        options.Add<Catalogue.Listing>();

        var files = new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions()).Generate().Files;

        files.ShouldContain(file => file.ModuleSpecifier == "dotnet:types");
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

    [Fact]
    public void Generate_TypesFromAnotherAssembly_AreReachedByTheirOwnNamespace()
    {
        // The reason the scheme stopped naming assemblies. A model from a referenced project used
        // to be reachable only at a specifier naming the *application* assembly, with the foreign
        // namespace hanging off it: "dotnet:MyApp.Web/Shouldly". Namespaces are what C# addresses,
        // and they do not belong to the assembly that happened to consume them.
        var options = new TypeDefinitionOptions { ApplicationAssembly = typeof(GeneratedTypeImportTests).Assembly };
        options.Add<Catalogue.Listing>();
        options.Add<Shouldly.ShouldAssertException>();

        var specifiers = new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions())
            .Generate().Files.Select(file => file.ModuleSpecifier).ToList();

        specifiers.ShouldContain("dotnet:types/Shouldly");
        specifiers.ShouldContain("dotnet:types/JsxCore/Tests/Catalogue");
        specifiers.ShouldNotContain(specifier => specifier.Contains("JsxCore.Tests", StringComparison.Ordinal));
    }
}
