using System.Text.Json;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// npm's answer is generated here rather than checked in, because these graphs float: a fixture
// recorded today describes a tree that stops existing the moment any of 300 packages publishes.
// Asking npm at the time of the test is the only comparison that stays true.
[Trait("Category", "Network")]
public class NpmParityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-parity-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private IReadOnlyDictionary<string, string>? NpmTreeFor(string package)
    {
        var npm = NpmPackageManager.Find();
        if (npm is null)
        {
            return null;
        }

        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"), """{"name":"parity","version":"1.0.0"}""");

        var (exitCode, output) = Fixtures.Npm.Run(
            _root, TimeSpan.FromMinutes(5), "install", "--package-lock-only", "--silent", package);

        exitCode.ShouldBe(0, $"npm could not resolve {package}, so there is nothing to compare against:\n{output}");

        var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "package-lock.json")));
        return lockFile.RootElement.GetProperty("packages").EnumerateObject()
            .Where(p => p.Name.Length > 0)
            .ToDictionary(p => p.Name, p => p.Value.GetProperty("version").GetString() ?? "");
    }

    [Theory]
    // Three sizes, chosen for what each one forces: eslint has a single conflict, webpack has
    // deeper nesting, and jest is large enough to involve aliases, peers and platform packages
    // all at once.
    [InlineData("eslint")]
    [InlineData("webpack")]
    [InlineData("jest")]
    public async Task Resolve_ComparedWithNpm_ProducesTheSameTree(string package)
    {
        var theirs = NpmTreeFor(package);
        if (theirs is null)
        {
            return;
        }

        var registry = new NpmRegistry(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        var placed = await new PackageResolver(registry).ResolveAsync([new PackageRequest(package, "")]);
        var ours = placed.ToDictionary(p => p.Path, p => p.Package.Version.ToString());

        // Same packages, in the same places, at the same versions. Where a package is decides what
        // every one of its dependents resolves to, so a difference here is a different application.
        ours.Keys.OrderBy(k => k).ShouldBe(theirs.Keys.OrderBy(k => k));
        foreach (var (path, version) in theirs)
        {
            ours[path].ShouldBe(version, $"{path} should be the version npm chose");
        }
    }

    [Theory]
    [InlineData("eslint")]
    [InlineData("jest")]
    public async Task Resolve_AnyGraph_ProducesATreeWhereEveryDependencyResolves(string package)
    {
        // Independent of npm: whatever tree comes out, every dependency of every package in it has
        // to resolve, from where that package sits, to a version that satisfies it.
        var registry = new NpmRegistry(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        var placed = await new PackageResolver(registry).ResolveAsync([new PackageRequest(package, "")]);

        PackageResolver.Validate(placed).ShouldBeEmpty();
    }
}
