using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

public class VersionRangeTests
{
    private static bool Allows(string range, string version) =>
        VersionRange.Parse(range).Satisfies(SemanticVersion.Parse(version));

    [Theory]
    // Caret keeps the leftmost non-zero component, so it means something different below 1.0.0.
    [InlineData("^1.2.3", "1.2.3", true)]
    [InlineData("^1.2.3", "1.9.9", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^1.2.3", "1.2.2", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.3", true)]
    [InlineData("^0.0.3", "0.0.4", false)]
    public void Satisfies_CaretRange_AllowsOnlyCompatibleVersions(string range, string version, bool expected) =>
        Allows(range, version).ShouldBe(expected);

    [Theory]
    [InlineData("~1.2.3", "1.2.9", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("~1.2", "1.2.9", true)]
    [InlineData("~1.2", "1.3.0", false)]
    public void Satisfies_TildeRange_AllowsPatchesOnly(string range, string version, bool expected) =>
        Allows(range, version).ShouldBe(expected);

    [Theory]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData("1.2.3 - 2.3.4", "2.3.4", true)]
    [InlineData("1.2.3 - 2.3.4", "2.3.5", false)]
    [InlineData("^1.0.0 || ^2.0.0", "2.5.0", true)]
    [InlineData("^1.0.0 || ^2.0.0", "3.0.0", false)]
    public void Satisfies_ComparatorsAndUnions_BehaveAsNpmDoes(string range, string version, bool expected) =>
        Allows(range, version).ShouldBe(expected);

    [Theory]
    [InlineData("1.x", "1.9.9", true)]
    [InlineData("1.x", "2.0.0", false)]
    [InlineData("1.2.x", "1.2.9", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData("*", "9.9.9", true)]
    [InlineData("", "9.9.9", true)]
    public void Satisfies_WildcardRange_MatchesTheWholeLine(string range, string version, bool expected) =>
        Allows(range, version).ShouldBe(expected);

    [Theory]
    // The subtle one: a prerelease only satisfies a range that mentions a prerelease of the same
    // three numbers, so ^1.0.0 does not quietly pick up 2.0.0-beta.
    [InlineData("^1.0.0", "2.0.0-beta", false)]
    [InlineData("^1.0.0", "1.5.0-beta", false)]
    [InlineData("^1.0.0-alpha", "1.0.0-beta", true)]
    [InlineData(">=1.0.0-alpha <2.0.0", "1.0.0-beta", true)]
    public void Satisfies_VersionIsAPrerelease_OnlyMatchesWhenTheRangeAsksForOne(
        string range, string version, bool expected) => Allows(range, version).ShouldBe(expected);

    [Fact]
    public void Best_SeveralCandidates_ReturnsTheHighestThatSatisfies()
    {
        SemanticVersion[] candidates =
        [
            SemanticVersion.Parse("1.0.0"), SemanticVersion.Parse("1.4.2"),
            SemanticVersion.Parse("1.9.0"), SemanticVersion.Parse("2.0.0")
        ];

        VersionRange.Parse("^1.0.0").Best(candidates)!.ToString().ShouldBe("1.9.0");
    }

    [Theory]
    [InlineData("github:user/repo")]
    [InlineData("npm:other@^1.0.0")]
    [InlineData("file:../local")]
    public void Parse_RangeIsNotAVersionRange_IsReportedAsUnsupported(string range) =>
        VersionRange.Parse(range).IsUnsupported.ShouldBeTrue();
}
