using System.Text.Json;
using System.Text.Json.Serialization;
using JsxCore.Tests.Other;
using JsxCore.Tests.Shadowing;
using JsxCore.Compilation;
using JsxCore.TypeScript;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Assets;

namespace JsxCore.Tests.Unit.TypeScript;

public class TypeGenerationTests
{
    private static GeneratedTypeScript GenerateFiles(
        Action<TypeDefinitionOptions> configure,
        JsonSerializerOptions? json = null)
    {
        var options = new TypeDefinitionOptions();
        configure(options);
        return new TypeScriptDefinitionGenerator(options, json ?? new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Generate();
    }

    /// <summary>All generated modules concatenated, for assertions about type mapping.</summary>
    private static string Generate(Action<TypeDefinitionOptions> configure, JsonSerializerOptions? json = null) =>
        string.Join(Environment.NewLine, GenerateFiles(configure, json).Files.Select(f => f.Contents));

    /// <summary>
    /// The module holding the declarations. The others are namespace facades that alias into it,
    /// so anything asserting about a declaration is asserting about this one.
    /// </summary>
    private static bool IsRoot(GeneratedTypeScriptFile file) =>
        !file.RelativePath.Contains(Path.DirectorySeparatorChar);

    private static string GenerateFor<T>(JsonSerializerOptions? json = null) =>
        Generate(o => o.Add<T>(), json);

    [Fact]
    public void Generate_PrimitiveMembers_MapToJavaScriptEquivalents()
    {
        var output = GenerateFor<Primitives>();

        output.ShouldContain("text: string;");
        output.ShouldContain("count: number;");
        output.ShouldContain("price: number;");
        output.ShouldContain("big: number;");
        output.ShouldContain("flag: boolean;");
        output.ShouldContain("id: string;");
    }

    [Fact]
    public void Generate_DateAndTimeMembers_MapToStringAsJsonCarriesThem()
    {
        var output = GenerateFor<Temporal>();

        output.ShouldContain("moment: string;");
        output.ShouldContain("offset: string;");
        output.ShouldContain("day: string;");
        output.ShouldContain("duration: string;");
    }

    [Fact]
    public void Generate_PropertyNamingPolicyIsSet_IsApplied()
    {
        GenerateFor<Primitives>().ShouldContain("text: string;");

        // No policy means the .NET names travel verbatim.
        var noPolicy = GenerateFor<Primitives>(new JsonSerializerOptions());
        noPolicy.ShouldContain("Text: string;");
    }

    [Fact]
    public void Generate_JsonPropertyNameAndJsonIgnore_AreHonoured()
    {
        var output = GenerateFor<Annotated>();

        output.ShouldContain("sku: string;");
        output.ShouldNotContain("stockKeepingUnit");
        output.ShouldNotContain("secret");
    }

    [Fact]
    public void Generate_MemberIsNullable_IsMarkedOptionalAndNullable()
    {
        var output = GenerateFor<Nullables>();

        output.ShouldContain("required: string;");
        output.ShouldContain("optionalText?: string | null;");
        output.ShouldContain("optionalNumber?: number | null;");
    }

    [Fact]
    public void Generate_CollectionsAndDictionaries_MapToArraysAndRecords()
    {
        var output = GenerateFor<Collections>();

        output.ShouldContain("names: string[];");
        output.ShouldContain("numbers: number[];");
        output.ShouldContain("readOnly: string[];");
        output.ShouldContain("lookup: Record<string, number>;");
    }

    [Fact]
    public void Generate_TypeReferencesAnother_EmitsItTransitively()
    {
        var output = GenerateFor<Outer>();

        output.ShouldContain("interface Outer {");
        output.ShouldContain("interface Inner {");
        output.ShouldContain("inner: Inner;");
        output.ShouldContain("many: Inner[];");
    }

    [Fact]
    public void Generate_TypeIsRecursive_TerminatesWithoutLooping()
    {
        var output = GenerateFor<TreeNode>();

        output.ShouldContain("interface TreeNode {");
        output.ShouldContain("children: TreeNode[];");
    }

    [Fact]
    public void Generate_EnumWithNoConfiguration_IsEmittedAsNumbers()
    {
        var output = GenerateFor<WithPlainEnum>();

        output.ShouldContain("type PlainEnum =");
        output.ShouldContain("| 0");
        output.ShouldContain("| 1");
    }

    [Fact]
    public void Generate_EnumIsAttributedAsStrings_IsEmittedAsAStringUnion()
    {
        // JsonStringEnumConverter<TEnum> is not assignable to the non-generic converter, so this
        // pins the detection that a plain assignability check would miss.
        var output = GenerateFor<WithStringEnum>();

        output.ShouldContain("""| "Alpha" """.TrimEnd());
        output.ShouldContain("""| "Beta" """.TrimEnd());
        output.ShouldNotContain("| 0");
    }

    [Fact]
    public void Generate_SerializerConvertsEnumsToStrings_EmitsAStringUnion()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());

        GenerateFor<WithPlainEnum>(json).ShouldContain("""| "First" """.TrimEnd());
    }

    [Fact]
    public void Generate_EnumRepresentationIsForced_OverridesDetection()
    {
        var output = Generate(o =>
        {
            o.Add<WithStringEnum>();
            o.EnumsAsStrings = false;
        });

        output.ShouldContain("| 0");
    }

    [Fact]
    public void Generate_PropertyNameIsNotAValidIdentifier_IsQuoted()
    {
        GenerateFor<AwkwardNames>().ShouldContain("\"content-type\"");
    }

    [Fact]
    public void Generate_AttributeSuppliesAName_UsesIt()
    {
        GenerateFor<Renamed>().ShouldContain("interface ProductSummary {");
    }

    [Fact]
    public void Generate_ModelSetIsUnchanged_ProducesIdenticalOutput()
    {
        // Stability matters: the file feeds the build id, which feeds browser cache keys.
        var first = GenerateFiles(o => o.Add(typeof(Outer), typeof(Collections), typeof(WithStringEnum)));
        var second = GenerateFiles(o => o.Add(typeof(WithStringEnum), typeof(Outer), typeof(Collections)));

        AssetStage.Fingerprint(second).ShouldBe(AssetStage.Fingerprint(first));
    }

    [Fact]
    public void Generate_NoTypesAreRegistered_WritesNothing()
    {
        GenerateFiles(_ => { }).Files.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_TypesHaveNamespaces_DoesNotReExportThemAtTheRoot()
    {
        // A type is reachable at its namespace path and nowhere else, so there is exactly one way
        // to name it.
        var contents = GenerateFiles(o => o.Add(typeof(Outer), typeof(Wrapper)))
            .Files.Single(IsRoot).Contents;

        contents.ShouldNotContain("export type {");
        contents.ShouldNotContain("export interface Outer");
    }

    [Fact]
    public void Generate_TypeHasNoNamespace_SitsAtTheTopLevel()
    {
        var contents = GenerateFiles(o => o.Add(typeof(GlobalNamespaceModel)))
            .Files.Single(IsRoot).Contents;

        contents.ShouldContain("export interface GlobalNamespaceModel");
        contents.ShouldNotContain("declare namespace");
    }

    [Fact]
    public void Generate_SeveralNamespaces_EmitsOneFileWithANamespaceEach()
    {
        var file = GenerateFiles(o => o.Add<Outer>()).Files.ShouldHaveSingleItem();

        file.RelativePath.ShouldBe("JsxCore.Tests.d.ts");
        file.ModuleSpecifier.ShouldBe("dotnet:JsxCore.Tests");

        // The test models live in JsxCore.Tests, so that is the namespace they mirror.
        file.Contents.ShouldContain("declare namespace JsxCore.Tests {");
        file.Contents.ShouldContain("interface Outer {");
    }

    [Fact]
    public void Generate_SameNamedTypesInDifferentNamespaces_AreKeptApart()
    {
        var contents = GenerateFiles(o => o.Add(typeof(Inner), typeof(Wrapper)))
            .Files.Single(IsRoot).Contents;

        // Same simple name, two namespaces, no aliasing needed.
        contents.ShouldContain("declare namespace JsxCore.Tests {");
        contents.ShouldContain("declare namespace JsxCore.Tests.Other {");
        contents.Split("interface Inner ").Length.ShouldBe(3);
    }

    [Fact]
    public void Generate_ReferenceCrossesNamespaces_IsQualifiedRatherThanImported()
    {
        var contents = GenerateFiles(o => o.Add<Wrapper>()).Files.Single(IsRoot).Contents;

        // Wrapper declares its own Inner and references the one from the parent namespace, so the
        // outsider has to be fully qualified or it would bind to the local declaration.
        contents.ShouldContain("own: Inner;");
        contents.ShouldContain("fromParent: JsxCore.Tests.Inner;");

        // A single module means no import statements at all (the header comment shows one as an
        // example, hence anchoring to the start of a line).
        contents.ShouldNotContain(Environment.NewLine + "import type");
    }

    [Fact]
    public void Generate_NamespaceMirroringIsOff_PutsEverythingAtTheTopLevel()
    {
        var contents = GenerateFiles(o =>
        {
            o.MirrorNamespaces = false;
            o.Add(typeof(Outer), typeof(Wrapper));
        }).Files.Single(IsRoot).Contents;

        contents.ShouldNotContain("declare namespace");
        contents.ShouldContain("export interface Outer");
    }

    [Fact]
    public void Generate_NamespacePrefixIsConfigured_IsTrimmed()
    {
        var contents = GenerateFiles(o =>
        {
            o.TrimNamespacePrefix = "JsxCore.Tests";
            o.Add(typeof(Wrapper));
        }).Files.Single(IsRoot).Contents;

        contents.ShouldContain("declare namespace Other {");
        contents.ShouldNotContain("namespace JsxCore.Tests.Other");
    }

    [Fact]
    public void Generate_TypeNameIsShadowed_IsAliasedRatherThanBoundToTheWrongOne()
    {
        // Shadow declares its own GlobalNamespaceModel and also references the namespace-less one;
        // a bare reference would silently resolve to the local declaration.
        var contents = GenerateFiles(o => o.Add<Shadow>()).Files.Single(IsRoot).Contents;

        contents.ShouldContain("type GlobalNamespaceModel$Global = GlobalNamespaceModel;");
        contents.ShouldContain("global: GlobalNamespaceModel$Global;");
    }

    [Fact]
    public void Generate_TypeIsAttributed_IsDiscovered()
    {
        var output = Generate(o => o.AddMarkedTypesFrom(typeof(TypeGenerationTests).Assembly));

        output.ShouldContain("interface MarkedForGeneration {");
        output.ShouldNotContain("NotMarkedForGeneration");
    }

    [Fact]
    public async Task GeneratedDeclarations_UsedByARealView_TypeCheck()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeDefinitions.Add<Outer>();
        project.Options.TypeChecking = TypeCheckingMode.Error;

        project.AddView("Home/Index.tsx", """
            import type JsxCore from "dotnet:JsxCore.Tests";
            export default function Index({ model }: { model: JsxCore.Tests.Outer }) {
                return <ul>{model.many.map((i) => <li key={i.label}>{i.label}: {i.value}</li>)}</ul>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
        File.Exists(Path.Combine(project.Layout.GeneratedTypesDirectory, "JsxCore.Tests.d.ts")).ShouldBeTrue();
    }

    [Fact]
    public async Task GeneratedDeclarations_ViewMisusesAType_FailsToCompile()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeDefinitions.Add<Outer>();

        project.AddView("Home/Index.tsx", """
            import type JsxCore from "dotnet:JsxCore.Tests";
            export default function Index({ model }: { model: JsxCore.Tests.Outer }) {
                return <p>{model.doesNotExist}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeFalse();
        build.Result.Errors.ShouldContain(d => d.Message.Contains("doesNotExist"));
    }
}
