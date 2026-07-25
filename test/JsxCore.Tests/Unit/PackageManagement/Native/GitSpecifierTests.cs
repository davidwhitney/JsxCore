using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

public class GitSpecifierTests
{
    [Theory]
    [InlineData("github:user/repo", "github", "user", "repo", "HEAD")]
    [InlineData("user/repo", "github", "user", "repo", "HEAD")]
    [InlineData("github:user/repo#v1.2.3", "github", "user", "repo", "v1.2.3")]
    [InlineData("git+https://github.com/user/repo.git", "github", "user", "repo", "HEAD")]
    [InlineData("git+https://github.com/user/repo.git#abc123", "github", "user", "repo", "abc123")]
    [InlineData("git+ssh://git@github.com/user/repo.git", "github", "user", "repo", "HEAD")]
    [InlineData("gitlab:group/project", "gitlab", "group", "project", "HEAD")]
    [InlineData("bitbucket:team/repo#main", "bitbucket", "team", "repo", "main")]
    public void TryParse_KnownForm_ReadsHostOwnerRepositoryAndReference(
        string text, string host, string owner, string repository, string reference)
    {
        GitSpecifier.TryParse(text, out var specifier).ShouldBeTrue();

        specifier.Host.ShouldBe(host);
        specifier.Owner.ShouldBe(owner);
        specifier.Repository.ShouldBe(repository);
        specifier.Reference.ShouldBe(reference);
    }

    [Theory]
    [InlineData("^1.0.0")]
    [InlineData("1.2.3")]
    [InlineData("git+https://example.com/thing.git")]
    [InlineData("github:user/repo#semver:^1.0.0")]
    public void TryParse_NotSomethingWeCanFetch_ReturnsFalse(string text) =>
        GitSpecifier.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void ArchiveUrl_GitHub_PointsAtTheArchiveRatherThanRequiringAClone()
    {
        GitSpecifier.TryParse("github:user/repo#main", out var specifier).ShouldBeTrue();

        specifier.ArchiveUrl.ShouldBe("https://codeload.github.com/user/repo/tar.gz/main");
        specifier.ResolvedUrl.ShouldBe("git+https://github.com/user/repo.git#main");
    }
}
