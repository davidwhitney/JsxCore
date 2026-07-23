using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    public void Parse_VersionIsWellFormed_ReadsItsComponents(string text, int major, int minor, int patch)
    {
        var version = SemanticVersion.Parse(text);

        (version.Major, version.Minor, version.Patch).ShouldBe((major, minor, patch));
    }

    [Fact]
    public void Parse_VersionHasPrereleaseAndBuild_SeparatesThem()
    {
        var version = SemanticVersion.Parse("1.2.3-beta.1+sha.abc");

        version.Prerelease.ShouldBe("beta.1");
        version.Build.ShouldBe("sha.abc");
        version.IsPrerelease.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not-a-version", false)]
    [InlineData("1.2.3.4", false)]
    public void TryParse_TextIsNotAVersion_ReturnsFalse(string text, bool expected) =>
        SemanticVersion.TryParse(text, out _).ShouldBe(expected);

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.1")]
    // A prerelease always sorts below the release it leads to.
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    // Fewer identifiers sorts lower.
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    // Numeric identifiers sort below alphanumeric ones.
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    public void CompareTo_TwoVersions_OrdersThemAsNpmDoes(string lower, string higher) =>
        SemanticVersion.Parse(lower).CompareTo(SemanticVersion.Parse(higher)).ShouldBeLessThan(0);

    [Fact]
    public void CompareTo_VersionsDifferOnlyByBuildMetadata_AreEqual() =>
        SemanticVersion.Parse("1.0.0+a").ShouldBe(SemanticVersion.Parse("1.0.0+b"));
}
