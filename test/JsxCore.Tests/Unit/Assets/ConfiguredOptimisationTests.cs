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

    [Theory]
    [InlineData(null, null, true, true)]    // development, nothing said: on
    [InlineData(null, null, false, false)]  // production, nothing said: off, unlike minification
    [InlineData(null, true, false, true)]   // the build asked for them in a Release build
    [InlineData(false, true, true, false)]  // the application overrides the build
    public void ResolveDevelopmentDefault_SourcesDisagree_TakesTheMostSpecific(
        bool? configured, bool? fromBuild, bool isDevelopment, bool expected) =>
        ConfiguredOptimisation.ResolveDevelopmentDefault(configured, fromBuild, isDevelopment).ShouldBe(expected);

    [Fact]
    public void SourceMaps_AssemblyCarriesTheAttribute_ReadsIt()
    {
        ConfiguredOptimisation.SourceMaps(AssemblyWith(ConfiguredOptimisation.SourceMapsKey, "true")).ShouldBe(true);
        ConfiguredOptimisation.SourceMaps(AssemblyWith(ConfiguredOptimisation.SourceMapsKey, "false")).ShouldBe(false);

        // An application built by an older JsxCore carries no such attribute, and the development
        // default then applies rather than silently turning maps off in development.
        ConfiguredOptimisation.SourceMaps(AssemblyWith("Unrelated", "true")).ShouldBeNull();
    }

    [Fact]
    public void Read_AssemblySaysNothingOrSomethingUnparseable_IsSilent()
    {
        // Silence has to mean "no opinion" rather than "off": a project that suppresses generated
        // assembly info carries no attribute, and should still get production defaults.
        ConfiguredOptimisation.Minify(null).ShouldBeNull();
        ConfiguredOptimisation.Minify(AssemblyWith("Unrelated", "true")).ShouldBeNull();
        ConfiguredOptimisation.Minify(AssemblyWith(ConfiguredOptimisation.MinifyKey, "yes please")).ShouldBeNull();
    }

    [Fact]
    public void Precompiled_BuildSaidNothing_CompilesOnStartupAsItAlwaysDid()
    {
        // No environment fallback here, unlike minification: an assembly that says nothing must
        // behave as it did before the build ever stamped this, or an application built by an older
        // JsxCore would start refusing to compile.
        ConfiguredOptimisation.Precompiled(configured: null, assembly: null).ShouldBeFalse();
        ConfiguredOptimisation.Precompiled(null, AssemblyWith("Unrelated", "true")).ShouldBeFalse();
    }

    [Fact]
    public void Precompiled_ReleaseBuildStampedIt_ServesWhatTheBuildProduced() =>
        ConfiguredOptimisation.Precompiled(
            null, AssemblyWith(ConfiguredOptimisation.PrecompiledKey, "true")).ShouldBeTrue();

    [Theory]
    [InlineData(true, "false", true)]
    [InlineData(false, "true", false)]
    public void Precompiled_ApplicationDisagreesWithTheBuild_TheApplicationWins(
        bool configured, string stamped, bool expected) =>
        ConfiguredOptimisation.Precompiled(
            configured, AssemblyWith(ConfiguredOptimisation.PrecompiledKey, stamped)).ShouldBe(expected);

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
