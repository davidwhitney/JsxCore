using System.Reflection;
using System.Reflection.Emit;
using JsxCore.Compilation.Provisioning;
using Shouldly;

namespace JsxCore.Tests.Unit.Assets;

/// <summary>
/// How the project file's answer reaches the running application, and who wins.
/// </summary>
public class ConfiguredOptimisationTests
{
    [Theory]
    [InlineData(null, null, true, false)]   // development, nothing said: off
    [InlineData(null, null, false, true)]   // production, nothing said: on
    [InlineData(null, false, false, false)] // the build turned it off, in production
    [InlineData(null, true, true, true)]    // the build turned it on, in development
    [InlineData(true, false, false, true)]  // the application overrides the build
    [InlineData(false, true, false, false)] // and can override it the other way
    public void Resolve_SourcesDisagree_TakesTheMostSpecific(
        bool? configured, bool? fromBuild, bool isDevelopment, bool expected) =>
        ConfiguredOptimisation.Resolve(configured, fromBuild, isDevelopment).ShouldBe(expected);

    [Fact]
    public void Minify_AssemblyCarriesTheAttribute_ReadsIt()
    {
        ConfiguredOptimisation.Minify(AssemblyWith(ConfiguredOptimisation.MinifyKey, "true")).ShouldBe(true);
        ConfiguredOptimisation.Minify(AssemblyWith(ConfiguredOptimisation.MinifyKey, "false")).ShouldBe(false);
    }

    [Fact]
    public void Compress_AssemblyCarriesTheAttribute_ReadsIt() =>
        ConfiguredOptimisation.Compress(AssemblyWith(ConfiguredOptimisation.CompressKey, "true")).ShouldBe(true);

    [Fact]
    public void Read_AssemblySaysNothingOrSomethingUnparseable_IsSilent()
    {
        // Silence has to mean "no opinion" rather than "off": a project that suppresses generated
        // assembly info carries no attribute, and should still get production defaults.
        ConfiguredOptimisation.Minify(null).ShouldBeNull();
        ConfiguredOptimisation.Minify(AssemblyWith("Unrelated", "true")).ShouldBeNull();
        ConfiguredOptimisation.Minify(AssemblyWith(ConfiguredOptimisation.MinifyKey, "yes please")).ShouldBeNull();
    }

    private static Assembly AssemblyWith(string key, string value)
    {
        var name = new AssemblyName("JsxCoreOptimisationProbe" + Guid.NewGuid().ToString("n")[..8]);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);

        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            [key, value]));

        return assembly;
    }
}
