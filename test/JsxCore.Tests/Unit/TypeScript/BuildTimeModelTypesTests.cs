using JsxCore.Compilation;
using JsxCore.TypeScript;
using SampleApp.Models;
using Shouldly;

namespace JsxCore.Tests.Unit.TypeScript;

public class BuildTimeModelTypesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-buildtypes-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Generate_ApplicationAssembly_DescribesItsModelTypes()
    {
        var result = BuildTimeModelTypes.Generate(typeof(Product).Assembly, _root);

        result.Generated.ShouldBeTrue(result.Failure);
        result.TypeCount.ShouldBeGreaterThan(0);

        var declarations = File.ReadAllText(Path.Combine(_root, ModelTypeDeclarations.FileName));
        declarations.ShouldContain("namespace SampleApp.Models");
        declarations.ShouldContain("interface Product");
    }

    [Fact]
    public void Generate_SameTypesAsTheApplicationWouldUse_ProducesTheSameDeclarations()
    {
        // The build and the running application must not describe a model differently, or a view
        // would compile against one shape and receive another.
        var options = new JsxCoreOptions();
        options.TypeDefinitions.ApplicationAssembly = typeof(Product).Assembly;

        var atRuntime = new TypeScriptDefinitionGenerator(options.TypeDefinitions, options.JsonSerializerOptions)
            .Generate().Files.Single().Contents;

        BuildTimeModelTypes.Generate(typeof(Product).Assembly, _root);

        File.ReadAllText(Path.Combine(_root, ModelTypeDeclarations.FileName)).ShouldBe(atRuntime);
    }

    [Fact]
    public void Generate_AssemblyWithNoModelTypes_WritesNothingRatherThanAnEmptyModule()
    {
        // An empty module is worse than no module: every named import from it becomes an error,
        // where an absent one falls back to the ambient declaration and types as any.
        var result = BuildTimeModelTypes.Generate(typeof(string).Assembly, _root);

        result.Generated.ShouldBeFalse();
        File.Exists(Path.Combine(_root, ModelTypeDeclarations.FileName)).ShouldBeFalse();
    }

    [Fact]
    public void Generate_PathIsNotAnAssembly_ReportsFailureRatherThanThrowing()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "not-an-assembly.dll");
        File.WriteAllText(path, "this is not a PE file");

        var result = BuildTimeModelTypes.Generate(path, _root);

        result.Generated.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
    }

    [Fact]
    public void TryLoad_AssemblyOnDisk_LoadsItWithoutRunningIt()
    {
        var path = typeof(Product).Assembly.Location;

        var loaded = ApplicationAssembly.TryLoad(path);

        loaded.ShouldNotBeNull();
        loaded.GetName().Name.ShouldBe(typeof(Product).Assembly.GetName().Name);
    }

    [Fact]
    public void TryLoad_PathDoesNotExist_ReturnsNull() =>
        ApplicationAssembly.TryLoad(Path.Combine(_root, "missing.dll")).ShouldBeNull();

    [Fact]
    public void Build_DeclarationsNotGeneratedYet_MapsNoPathAndDeclaresAnAmbientModule()
    {
        var options = new JsxCoreOptions { WorkingDirectory = _root };
        var layout = CompilationLayout.Create(options, _root);

        var config = TsConfigWriter.Build(options, layout);

        var paths = config["compilerOptions"]!["paths"]!.AsObject();
        paths.ContainsKey(options.TypeDefinitions.ModuleSpecifier).ShouldBeFalse();

        // A mapping to a file that is not there resolves to nothing, so the stand-in has to be in
        // the program instead.
        var included = config["include"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        included.ShouldContain(path => path.EndsWith(ModelTypeDeclarations.PendingFileName, StringComparison.Ordinal));

        var pending = File.ReadAllText(Path.Combine(layout.GeneratedTypesDirectory, ModelTypeDeclarations.PendingFileName));
        pending.ShouldContain($"declare module \"{options.TypeDefinitions.ModuleSpecifier}\";");
    }

    [Fact]
    public void Build_DeclarationsGenerated_MapsThePathAndDropsTheAmbientModule()
    {
        var options = new JsxCoreOptions { WorkingDirectory = _root };
        var layout = CompilationLayout.Create(options, _root);

        BuildTimeModelTypes.Generate(typeof(Product).Assembly, layout.GeneratedTypesDirectory);
        var config = TsConfigWriter.Build(options, layout);

        var paths = config["compilerOptions"]!["paths"]!.AsObject();
        paths[options.TypeDefinitions.ModuleSpecifier].ShouldNotBeNull();

        var included = config["include"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        included.ShouldNotContain(path => path.EndsWith(ModelTypeDeclarations.PendingFileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_AmbientStandInWasWritten_ReplacesIt()
    {
        // Both describe the same module, so leaving the stand-in behind would make the compiler
        // choose between two declarations of it.
        ModelTypeDeclarations.WritePending(_root, "@jsxcore/generated");

        BuildTimeModelTypes.Generate(typeof(Product).Assembly, _root);

        File.Exists(Path.Combine(_root, ModelTypeDeclarations.PendingFileName)).ShouldBeFalse();
    }
}
