using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// The bar for this implementation is not "looks right", it is "npm agrees". Each row was produced
// by npm's own semver package, so a disagreement here is a disagreement with npm.
public class SemverAgreementTests
{
    public static TheoryData<string, string, bool> NpmAnswers()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var line in File.ReadAllLines(Path.Combine(
                     JsxProjectFixture.RepositoryRoot(), "test", "JsxCore.Tests",
                     "Fixtures", "npm-semver-answers.txt")))
        {
            if (line.Trim().Length == 0) continue;
            var parts = line.Split('\t');
            data.Add(parts[0], parts[1], bool.Parse(parts[2]));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NpmAnswers))]
    public void Satisfies_ComparedWithNpmSemver_Agrees(string range, string version, bool npmSaysYes) =>
        VersionRange.Parse(range).Satisfies(SemanticVersion.Parse(version)).ShouldBe(npmSaysYes);
}
