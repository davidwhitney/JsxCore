using System.Reflection;
using JsxCore.Compilation.Provisioning;
using Shouldly;

namespace JsxCore.Tests.Unit.Build;

public class ConfiguredFrameworkTests
{
    [Fact]
    public void Read_AssemblyBuiltWithJsxCore_ReportsWhatTheBuildChose()
    {
        // The sample application is built by the real targets, so its assembly carries whatever
        // they stamped on it. This is the channel the application uses to find out.
        ConfiguredFramework.Read(typeof(SampleApp.Models.Product).Assembly).ShouldBe(JsFramework.Preact);
    }

    [Fact]
    public void Read_AssemblyBuiltWithoutJsxCore_ReportsNothingRatherThanGuessing() =>
        ConfiguredFramework.Read(typeof(string).Assembly).ShouldBeNull();

    [Fact]
    public void Read_NoAssembly_IsNullRatherThanThrowing() =>
        ConfiguredFramework.Read(null).ShouldBeNull();

    [Theory]
    [InlineData("preact", JsFramework.Preact)]
    [InlineData("react", JsFramework.React)]
    [InlineData("  React  ", JsFramework.React)]
    public void Parse_KnownName_IsUnderstood(string name, JsFramework expected) =>
        ConfiguredFramework.Parse(name).ShouldBe(expected);

    [Theory]
    [InlineData("vue")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_AnythingElse_IsNull(string? name) => ConfiguredFramework.Parse(name).ShouldBeNull();
}
