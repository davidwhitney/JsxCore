using JsxCore.Compilation.Provisioning;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Unit.Build;

public class ConfiguredRuntimeTests
{
    [Fact]
    public void CallsUsePreact_AssemblyCallsIt_IsDetected()
    {
        // The sample application calls options.UsePreact(), which is exactly the configuration the
        // build cannot see in the project file.
        var assembly = typeof(SampleApp.Models.Product).Assembly.Location;

        ConfiguredRuntime.CallsUsePreact(assembly).ShouldBeTrue();
    }

    [Fact]
    public void CallsUsePreact_AssemblyDoesNot_IsNotDetected() =>
        ConfiguredRuntime.CallsUsePreact(typeof(string).Assembly.Location).ShouldBeFalse();

    [Fact]
    public void CallsUsePreact_PathIsMissingOrNotAnAssembly_IsFalseRatherThanThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "jsxcore-not-an-assembly-" + Guid.NewGuid().ToString("n")[..8] + ".dll");
        File.WriteAllText(path, "this is not a PE file");

        try
        {
            ConfiguredRuntime.CallsUsePreact(path).ShouldBeFalse();
            ConfiguredRuntime.CallsUsePreact(Path.Combine(Path.GetTempPath(), "absent.dll")).ShouldBeFalse();
            ConfiguredRuntime.CallsUsePreact("").ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
