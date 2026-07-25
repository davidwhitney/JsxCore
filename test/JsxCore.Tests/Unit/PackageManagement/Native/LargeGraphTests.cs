using System.Text.Json;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// Real graphs, because the interesting behaviour only appears at size: hoisting, nesting when two
// dependents disagree, aliases, and platform specific optional dependencies all at once.
[Trait("Category", "Network")]
public class LargeGraphTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-large-" + Guid.NewGuid().ToString("n")[..8]);

    private static NpmRegistry Registry() => new(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("eslint")]
    [InlineData("webpack")]
    [InlineData("jest")]
    public async Task Resolve_LargeGraph_ProducesATreeWhereEveryDependencyResolves(string package)
    {
        var placed = await new PackageResolver(Registry()).ResolveAsync([new PackageRequest(package, "")]);

        placed.Count.ShouldBeGreaterThan(50);
        PackageResolver.Validate(placed).ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolve_TwoDependentsDisagree_NestsTheLoserRatherThanFailing()
    {
        // eslint is the smallest real graph with a genuine conflict: eslint-visitor-keys is needed
        // at two incompatible majors, and npm answers it by nesting one copy.
        var placed = await new PackageResolver(Registry()).ResolveAsync([new PackageRequest("eslint", "")]);

        var nested = placed.Where(p => p.IsNested).ToList();

        nested.ShouldNotBeEmpty();
        nested.ShouldContain(p => p.Name == "eslint-visitor-keys");
        placed.Count(p => p.Name == "eslint-visitor-keys").ShouldBe(2);
    }

    [Fact]
    public async Task Resolve_MostOfTheGraphHasNoConflict_HoistsItToTheTopLevel()
    {
        // Nesting everything would also be a working tree, and a useless one: the point of hoisting
        // is that a package appears once.
        var placed = await new PackageResolver(Registry()).ResolveAsync([new PackageRequest("webpack", "")]);

        placed.Count(p => p.IsNested).ShouldBeLessThan(placed.Count / 10);
    }

    [Fact]
    public async Task Resolve_DependencyUsesAnNpmAlias_InstallsItUnderTheAliasName()
    {
        // jest reaches an entry like "react-is-18": "npm:react-is@^18.3.1", which used to stop
        // resolution entirely.
        var placed = await new PackageResolver(Registry()).ResolveAsync([new PackageRequest("jest", "")]);

        var aliased = placed.Where(p => p.IsAlias).ToList();

        aliased.ShouldNotBeEmpty();
        aliased.ShouldAllBe(p => p.Path.EndsWith(p.InstallName));
    }

    [Fact]
    public void Install_LargeGraph_ProducesALockFileRealNpmCanInstallFrom()
    {
        var npm = NpmPackageManager.Find();
        if (npm is null)
        {
            return;
        }

        Directory.CreateDirectory(_root);
        var manager = new NativePackageManager(report: _ => { });
        manager.CreateManifest(_root);

        manager.Add(_root, [new PackageRequest("webpack", "^5.0.0")]).Succeeded.ShouldBeTrue();

        var written = JsonDocument.Parse(File.ReadAllText(LockFile.PathIn(_root)))
            .RootElement.GetProperty("packages").EnumerateObject().Count();
        written.ShouldBeGreaterThan(50);

        Directory.Delete(Path.Combine(_root, "node_modules"), recursive: true);

        var (exitCode, output) = Fixtures.Npm.Run(_root, TimeSpan.FromMinutes(5), "ci");

        exitCode.ShouldBe(0, $"npm ci rejected a {written} package lock file:\n{output}");
        Directory.Exists(Path.Combine(_root, "node_modules", "webpack")).ShouldBeTrue();
    }
}
