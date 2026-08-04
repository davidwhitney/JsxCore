using System.Reflection;
using JsxCore.TypeScript;
using Shouldly;

namespace JsxCore.Tests.Unit.TypeScript;

/// <summary>
/// Reading the globals an application registers off its own assembly, so the build can describe
/// <c>dotnet:globals</c> without running it.
/// </summary>
/// <remarks>
/// The failure this exists to prevent is a strict build that only passes on a machine which has run
/// the application: globals were <c>any</c> at build time, and a callback over an <c>any</c> result
/// is an error under <c>noImplicitAny</c>.
/// </remarks>
public class RegisteredGlobalsTests
{
    private static TypeDefinitionOptions Apply(params (string Key, string Value)[] metadata)
    {
        var options = new TypeDefinitionOptions();
        RegisteredGlobals.Apply(options, new StubAssembly(metadata));
        return options;
    }

    [Fact]
    public void Apply_CompleteRegistrations_AreDescribedWithTheirTypes()
    {
        var options = Apply(
            (RegisteredGlobals.MetadataKey, $"Inventory={typeof(StubService).FullName}"),
            (RegisteredGlobals.CompleteMetadataKey, "true"));

        options.GlobalsAreKnown.ShouldBeTrue();
        options.GlobalTypes.Keys.ShouldBe(["Inventory"]);
        options.GlobalTypes["Inventory"].ShouldBe(typeof(StubService));
    }

    [Fact]
    public void Apply_RegistrationWithNoType_IsKnownButUntyped()
    {
        // The factory overload returns object, so the name exists and the global is any. The
        // running application describes it the same way.
        var options = Apply(
            (RegisteredGlobals.MetadataKey, "Config="),
            (RegisteredGlobals.CompleteMetadataKey, "true"));

        options.GlobalsAreKnown.ShouldBeTrue();
        options.GlobalTypes.ShouldContainKey("Config");
        options.GlobalTypes["Config"].ShouldBeNull();
    }

    [Fact]
    public void Apply_ListIsIncomplete_IsRefused()
    {
        // Naming three when the application exposes four turns the fourth from any into "has no
        // exported member", which breaks a build that worked. Nothing is better than nearly right.
        var options = Apply(
            (RegisteredGlobals.MetadataKey, $"Inventory={typeof(StubService).FullName}"),
            (RegisteredGlobals.CompleteMetadataKey, "false"));

        options.GlobalsAreKnown.ShouldBeFalse();
        options.GlobalTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_AssemblySaysNothing_LeavesTheStandInAlone()
    {
        var options = Apply();

        options.GlobalsAreKnown.ShouldBeFalse();
        RegisteredGlobals.Apply(new TypeDefinitionOptions(), assembly: null).ShouldBeFalse();
    }

    [Fact]
    public void Apply_TypeCannotBeResolved_KeepsTheNameAsAny()
    {
        // A type renamed between the generator running and the assembly loading is not a reason to
        // lose the name: the import still has to resolve.
        var options = Apply(
            (RegisteredGlobals.MetadataKey, "Ghost=Nowhere.NoSuchType"),
            (RegisteredGlobals.CompleteMetadataKey, "true"));

        options.GlobalsAreKnown.ShouldBeTrue();
        options.GlobalTypes["Ghost"].ShouldBeNull();
    }

    private sealed class StubService
    {
        public int Count() => 0;
    }

    /// <summary>
    /// Carries metadata and resolves types out of the test assembly, which is where the stubs are.
    /// </summary>
    private sealed class StubAssembly(params (string Key, string Value)[] metadata) : Assembly
    {
        private readonly Assembly _real = typeof(RegisteredGlobalsTests).Assembly;

        // Typed as the attribute rather than as object, because the generic overload casts the
        // result to Attribute[] and an object[] does not survive that.
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            attributeType == typeof(AssemblyMetadataAttribute)
                ? metadata.Select(entry => new AssemblyMetadataAttribute(entry.Key, entry.Value)).ToArray()
                : [];

        public override Type? GetType(string name, bool throwOnError) => _real.GetType(name, throwOnError);
    }
}
