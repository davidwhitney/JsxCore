using System.Text.Json;
using JsxCore.Compilation.Assets;
using JsxCore.Interop;
using JsxCore.Tests.Catalogue.Pricing;
using JsxCore.TypeScript;
using Shouldly;

namespace JsxCore.Tests.Unit.TypeScript;

/// <summary>
/// Importing registered .NET objects by name from <c>dotnet:globals</c>.
/// </summary>
public class GlobalImportTests
{
    private sealed class Basket
    {
        public int Count => 0;
        public Money Total(string currency) => new(0, currency);
        public void Clear() { }
    }

    private static IReadOnlyList<GeneratedTypeScriptFile> Generate(params (string Name, Type? Type)[] globals)
    {
        var options = new TypeDefinitionOptions
        {
            GlobalTypes = globals.ToDictionary(g => g.Name, g => g.Type, StringComparer.Ordinal)
        };

        return new TypeScriptDefinitionGenerator(options, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Generate().Files;
    }

    private static string GlobalsDeclaration(params (string Name, Type? Type)[] globals) =>
        Generate(globals).Single(file => file.ModuleSpecifier == TypeDefinitionOptions.GlobalsSpecifier).Contents;

    [Fact]
    public void Generate_GlobalHasAKnownType_DescribesItsMethods()
    {
        var declaration = GlobalsDeclaration(("Basket", typeof(Basket)));

        declaration.ShouldContain("export declare const Basket: {");
        declaration.ShouldContain("clear(): void;");
        declaration.ShouldContain("count: number;");
    }

    [Fact]
    public void Generate_MethodTakesAndReturnsModelTypes_ReferencesThemRatherThanAny()
    {
        // The point of typing a global: a call is checked against what the C# actually returns.
        var declaration = GlobalsDeclaration(("Basket", typeof(Basket)));

        declaration.ShouldContain("total(currency: string): JsxCore.Tests.Catalogue.Pricing.Money;");
        declaration.ShouldContain("""import type JsxCore from "dotnet:types";""");
    }

    [Fact]
    public void Generate_TypeReferencedOnlyByAGlobal_IsDeclaredInTheAssemblyModule()
    {
        // Money is reachable only through Basket.Total, so nothing else would have collected it.
        var root = Generate(("Basket", typeof(Basket)))
            .Single(file => file.ModuleSpecifier == "dotnet:types");

        root.Contents.ShouldContain("interface Money");
    }

    [Fact]
    public void Generate_GlobalRegisteredWithAFactory_IsAny()
    {
        // A factory returns object, so there is nothing to describe and saying so is honest.
        GlobalsDeclaration(("Anything", null)).ShouldContain("export declare const Anything: any;");
    }

    [Fact]
    public void Generate_NothingRegistered_WritesNoModuleAtAll()
    {
        // Absent rather than empty, so the ambient stand-in applies and imports type as any until
        // the application has run.
        Generate().ShouldNotContain(file => file.ModuleSpecifier == TypeDefinitionOptions.GlobalsSpecifier);
    }

    [Fact]
    public void Module_Always_BindsLazilyRatherThanAtImport()
    {
        // Evaluated on the client too, where there is no bridge. Resolving at module scope would
        // throw on import, breaking a client-rendered view that merely mentions a global.
        var module = GeneratedGlobalsModule.For(["Inventory"]);

        module.ShouldContain("""import { dotnetGlobal } from "./dotnet.js";""");
        module.ShouldContain("""export const Inventory = dotnetGlobal("Inventory");""");
    }

    [Fact]
    public void Module_NameIsNotAValidIdentifier_IsLeftOut()
    {
        // "my service" cannot be an export name. Emitting it would be a syntax error, which would
        // take down every other global in the module with it.
        var module = GeneratedGlobalsModule.For(["Inventory", "my service"]);

        module.ShouldContain("export const Inventory");
        module.ShouldNotContain("my service");
    }

    [Fact]
    public void Module_NothingRegistered_IsStillAModule()
    {
        // Still a module, and still able to reach a global by name.
        GeneratedGlobalsModule.For([]).ShouldContain("""export { dotnet } from "./dotnet.js";""");
    }

    [Fact]
    public void Registry_RegisteredByType_RemembersItForTheDeclaration()
    {
        var registry = new JsxGlobalRegistry();
        registry.Register<Basket>("Basket");

        registry.Registrations["Basket"].ServiceType.ShouldBe(typeof(Basket));
    }

    [Fact]
    public void Registry_RegisteredAsAnInstance_UsesTheInstancesType()
    {
        var registry = new JsxGlobalRegistry();
        registry.Register("Basket", new Basket());

        registry.Registrations["Basket"].ServiceType.ShouldBe(typeof(Basket));
    }

    [Fact]
    public void Registry_RegisteredWithAFactory_HasNoType()
    {
        var registry = new JsxGlobalRegistry();
        registry.Register("Anything", _ => new Basket());

        registry.Registrations["Anything"].ServiceType.ShouldBeNull();
    }
}
