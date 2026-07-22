using JsxCore.Compilation;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Provisioning.PackageManagement;

namespace JsxCore.Tests.Unit.PackageManagement;

/// <summary>
/// Selecting a restore strategy, which is the seam a native implementation arrives through.
/// </summary>
public class PackageManagementTests
{
    private sealed class StubManager(string name, bool available) : IPackageManager
    {
        public string Name => name;
        public bool IsAvailable() => available;
        public int Adds { get; private set; }

        public PackageOperationResult CreateManifest(string directory) => PackageOperationResult.Ok("created");
        public PackageOperationResult RestoreFromLockFile(string directory) => PackageOperationResult.Ok("restored");
        public PackageOperationResult InstallDeclared(string directory) => PackageOperationResult.Ok("installed");

        public PackageOperationResult Add(string directory, IReadOnlyCollection<PackageRequest> packages)
        {
            Adds++;
            return PackageOperationResult.Ok("added");
        }
    }

    [Fact]
    public void Select_FirstStrategyCannotRun_PicksTheNextThatCan()
    {
        var selector = new PackageManagerSelector([new StubManager("npm", false), new StubManager("native", true)]);

        selector.Select()!.Name.ShouldBe("native");
    }

    [Fact]
    public void Select_BothStrategiesCanRun_PrefersTheEarlier()
    {
        // Order is preference: an installed npm keeps doing what it does today.
        var selector = new PackageManagerSelector([new StubManager("npm", true), new StubManager("native", true)]);

        selector.Select()!.Name.ShouldBe("npm");
    }

    [Fact]
    public void Select_NoStrategyCanRun_ReturnsNull()
    {
        new PackageManagerSelector([new StubManager("npm", false)]).Select().ShouldBeNull();
    }

    [Fact]
    public void Select_StrategyIsNamed_ReturnsThatStrategy()
    {
        var selector = new PackageManagerSelector([new StubManager("npm", true), new StubManager("native", true)]);

        selector.Select("native")!.Name.ShouldBe("native");
    }

    [Fact]
    public void Select_NamedStrategyCannotRun_ReturnsNullRatherThanFallingBack()
    {
        // Asking for one and silently getting another is how a build becomes unreproducible.
        var selector = new PackageManagerSelector([new StubManager("npm", true), new StubManager("native", false)]);

        selector.Select("native").ShouldBeNull();
    }

    [Fact]
    public void DescribeAll_Queried_NamesEveryStrategy()
    {
        new PackageManagerSelector([new StubManager("npm", false), new StubManager("native", false)])
            .DescribeAll().ShouldBe("npm, native");
    }

    [Fact]
    public void Default_Constructed_OffersTheNativeClientThenNpm()
    {
        // Order is the policy: the native client needs nothing installed, so it goes first, and
        // npm is there for what it does not cover and for anyone who asks for it by name.
        PackageManagerSelector.Default(new JsxCoreOptions()).Managers
            .Select(manager => manager.Name).ShouldBe(["native", "npm"]);
    }

    [Fact]
    public void Select_NoPreference_ChoosesTheNativeClient() =>
        PackageManagerSelector.Default(new JsxCoreOptions()).Select()!.Name.ShouldBe("native");

    [Fact]
    public void Select_NpmIsNamed_UsesNpmWhenItIsThereAndNothingWhenItIsNot()
    {
        var selected = PackageManagerSelector.Default(new JsxCoreOptions()).Select("npm");

        // Naming a strategy that cannot run reports nothing rather than quietly using another.
        selected?.Name.ShouldBe("npm");
        (selected is not null).ShouldBe(Npm.IsInstalled);
    }

    [Fact]
    public void Select_NpmIsUnavailable_FallsBackToTheNativeClient()
    {
        var selector = new PackageManagerSelector(
            [new StubManager("npm", false), new NativePackageManager()]);

        selector.Select()!.Name.ShouldBe("native");
    }

    [Fact]
    public void IsAvailable_NpmIsOrIsNotPresent_ReportsAccordingly()
    {
        // Checked against an independent probe: npm is no longer required to run this suite.
        new NpmPackageManager().IsAvailable().ShouldBe(Npm.IsInstalled);
        new NpmPackageManager("/does/not/exist/npm").IsAvailable().ShouldBeFalse();
    }

    [Fact]
    public void Specifier_RangeIsPresent_IncludesIt()
    {
        new PackageRequest("typescript", "^7").Specifier.ShouldBe("typescript@^7");
        new PackageRequest("preact").Specifier.ShouldBe("preact");
    }

    [Fact]
    public void Restore_ManifestIsMissing_CreatesItBeforeInstalling()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jsxcore-pm-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(directory);
        try
        {
            var manager = new StubManager("stub", true);
            var result = new DependencyRestorer(manager).Restore(directory, [new PackageRequest("typescript", "^7", true)]);

            result.Actions.ShouldContain("created");
            manager.Adds.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Restore_EverythingIsInstalled_DoesNothing()
    {
        // The repository root has its packages installed, so this is the ordinary build case.
        var manager = new StubManager("stub", true);
        var result = new DependencyRestorer(manager).Restore(JsxProjectFixture.RepositoryRoot(), []);

        result.Succeeded.ShouldBeTrue();
        result.DidAnything.ShouldBeFalse();
        manager.Adds.ShouldBe(0);
    }
}
