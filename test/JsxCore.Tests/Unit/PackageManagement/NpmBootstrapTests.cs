using JsxCore.Compilation;
using JsxCore.TypeScript;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Tests.Unit.PackageManagement;

/// <summary>
/// The bootstrapper writes to the developer's project, so what it decides to do matters as much as
/// whether it works. Most of these check the decision without running npm at all.
/// </summary>
public class NpmBootstrapTests : IDisposable
{
    private readonly List<string> _temporary = [];

    private string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "jsxcore-npm", Guid.NewGuid().ToString("n")[..10]);
        Directory.CreateDirectory(path);
        _temporary.Add(path);
        return path;
    }

    [Fact]
    public void RequiredPackages_AnyConfiguration_IncludeTypeScriptAtTheMinimumVersion()
    {
        var packages = NpmBootstrapper.RequiredPackages(new JsxCoreOptions());

        var typescript = packages.ShouldHaveSingleItem();
        typescript.Package.ShouldBe("typescript");
        typescript.VersionRange.ShouldBe("^7");
        typescript.DevDependency.ShouldBeTrue();
    }

    [Fact]
    public void RequiredPackages_PreactMode_IncludePreactAsARuntimeDependency()
    {
        var options = new JsxCoreOptions();
        options.UsePreact();

        var packages = NpmBootstrapper.RequiredPackages(options);

        packages.Select(p => p.Package)
            .ShouldBe(["typescript", "preact", "preact-render-to-string"]);

        // Preact ships in the published output, so it is not a development-only dependency.
        packages.Single(p => p.Package == "preact").DevDependency.ShouldBeFalse();
        packages.Single(p => p.Package == "typescript").DevDependency.ShouldBeTrue();
    }

    [Fact]
    public void MissingPackages_EverythingIsInstalled_ReportsNothing()
    {
        var options = new JsxCoreOptions();
        options.UsePreact();
        options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());

        NpmBootstrapper.MissingPackages(options, JsxProjectFixture.RepositoryRoot()).ShouldBeEmpty();
    }

    [Fact]
    public void MissingPackages_DirectoryIsEmpty_ReportsEverything()
    {
        var options = new JsxCoreOptions();
        options.UsePreact();

        var missing = NpmBootstrapper.MissingPackages(options, TempDirectory());

        missing.ShouldBe(["typescript", "preact", "preact-render-to-string"]);
    }

    [Fact]
    public void ResolveProjectDirectory_ManifestExistsAbove_UsesItRatherThanCreatingAnother()
    {
        var root = TempDirectory();
        var nested = Path.Combine(root, "src", "MyApp");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "package.json"), "{}");

        var (directory, hasManifest) = NpmBootstrapper.ResolveProjectDirectory(nested);

        // A solution-level manifest is the right place for a package the whole solution shares.
        directory.ShouldBe(Path.GetFullPath(root));
        hasManifest.ShouldBeTrue();
    }

    [Fact]
    public void ResolveProjectDirectory_NoManifestAnywhere_FallsBackToTheContentRoot()
    {
        var root = TempDirectory();

        var (directory, hasManifest) = NpmBootstrapper.ResolveProjectDirectory(root);

        directory.ShouldBe(Path.GetFullPath(root));
        hasManifest.ShouldBeFalse();
    }

    [Fact]
    public void EnsureDependencies_NothingIsMissing_DoesNothing()
    {
        var options = new JsxCoreOptions();
        options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());

        var messages = new List<string>();
        var result = new NpmBootstrapper(messages.Add, TimeSpan.FromSeconds(30))
            .EnsureDependencies(options, JsxProjectFixture.RepositoryRoot());

        result.DidAnything.ShouldBeFalse();
        result.Failure.ShouldBeNull();
        messages.ShouldBeEmpty();
    }

    [Fact]
    public void EnsureDependencies_NoPackageManagerAvailable_ReportsWhy()
    {
        var options = new JsxCoreOptions { NpmPath = "/definitely/not/npm" };

        var result = new NpmBootstrapper(_ => { }, TimeSpan.FromSeconds(5), options.NpmPath)
            .EnsureDependencies(options, TempDirectory());

        result.Failure.ShouldNotBeNull();
        result.Failure.ShouldContain("npm");
        result.DidAnything.ShouldBeFalse();
    }

    [Fact]
    public void Verify_BootstrapFailed_StillProducesManualInstructions()
    {
        var root = TempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "Views"));

        var exception = Should.Throw<JsxCoreEnvironmentException>(() =>
            EnvironmentVerifier.Verify(new JsxCoreOptions(), root, bootstrapFailure: "npm was not found on PATH."));

        // The developer needs both: what to run, and why it was not run for them.
        exception.Message.ShouldContain("npm install --save-dev typescript@^7");
        exception.Message.ShouldContain("tried to install this automatically");
        exception.Message.ShouldContain("npm was not found on PATH.");
    }

    [Fact]
    public void LocateNpm_NpmIsOrIsNotOnThisMachine_ReportsAccurately()
    {
        // Compared against an independent probe rather than an assumption: the suite restores its
        // own packages natively now, so npm may genuinely not be here.
        (NpmBootstrapper.LocateNpm() is not null).ShouldBe(Npm.IsInstalled);
        NpmBootstrapper.LocateNpm("/definitely/not/npm").ShouldBeNull();
    }

    public void Dispose()
    {
        foreach (var path in _temporary)
        {
            try { Directory.Delete(path, recursive: true); } catch (IOException) { }
        }
    }
}
