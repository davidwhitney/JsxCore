using JsxCore.Compilation;
using JsxCore.Tool;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Modules;

namespace JsxCore.Tests.Unit.Build;

/// <summary>
/// The logic the MSBuild targets rely on. All of this used to live in the .targets as property
/// functions and regular expressions, where none of it could be tested.
/// </summary>
public class BuildLogicTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jsxcore-build-" + Guid.NewGuid().ToString("n")[..8]);

    private string Write(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------------------------------
    // NodeModulesLayout
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FindFile_InstallIsAboveTheProject_WalksUpAndFindsIt()
    {
        Write("node_modules/marked/package.json", "{}");
        var layout = NodeModulesLayout.For(Path.Combine(_root, "src", "app"));

        layout.FindFile("marked/package.json").ShouldNotBeNull();
    }

    [Fact]
    public void FindFile_TwoInstallsInTheTree_PrefersTheNearest()
    {
        Write("node_modules/marked/package.json", """{"version":"1"}""");
        Write("app/node_modules/marked/package.json", """{"version":"2"}""");

        var found = NodeModulesLayout.For(Path.Combine(_root, "app")).FindFile("marked/package.json").ShouldNotBeNull();

        File.ReadAllText(found).ShouldContain("\"2\"");
    }

    [Fact]
    public void FindFile_PathIsConfiguredExplicitly_IsSearchedFirst()
    {
        Write("elsewhere/node_modules/marked/package.json", """{"version":"elsewhere"}""");
        Write("app/node_modules/marked/package.json", """{"version":"app"}""");

        var layout = NodeModulesLayout.For(Path.Combine(_root, "app"), [Path.Combine(_root, "elsewhere")]);

        File.ReadAllText(layout.FindFile("marked/package.json")!).ShouldContain("elsewhere");
    }

    [Fact]
    public void FindPackage_PackageIsNotInstalled_ReturnsNull()
    {
        Write("node_modules/marked/package.json", "{}");

        NodeModulesLayout.For(_root).FindPackage("not-installed").ShouldBeNull();
    }

    [Fact]
    public void RootsFor_ImporterHasANestedInstall_SearchesItFirst()
    {
        Write("node_modules/outer/package.json", "{}");
        Write("node_modules/outer/node_modules/inner/package.json", "{}");
        var layout = NodeModulesLayout.For(_root);

        var roots = layout.RootsFor(Path.Combine(_root, "node_modules", "outer", "index.js")).ToList();

        // The package's own node_modules comes first, so a nested version wins over the top-level one.
        roots[0].ShouldContain(Path.Combine("outer", "node_modules"));
    }

    [Fact]
    public void Contains_PathIsInsideNodeModules_ReturnsTrue()
    {
        Write("node_modules/marked/index.js", "");
        var layout = NodeModulesLayout.For(_root);

        layout.Contains(Path.Combine(_root, "node_modules", "marked", "index.js")).ShouldBeTrue();
        layout.Contains(Path.Combine(_root, "Views", "Index.tsx")).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // PackageManifest
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Read_ManifestHasBothKinds_SeparatesRuntimeFromDevelopment()
    {
        var path = Write("package.json", """
            {
              "dependencies": { "marked": "^18.0.0", "classnames": "2.5.1" },
              "devDependencies": { "typescript": "^7.0.2" }
            }
            """);

        var manifest = PackageManifest.Read(path).ShouldNotBeNull();

        manifest.Dependencies.Select(d => d.Name).ShouldBe(["marked", "classnames"]);
        manifest.DevDependencies.Select(d => d.Name).ShouldBe(["typescript"]);
        manifest.RuntimeNames.ShouldNotContain("typescript");
    }

    [Fact]
    public void Read_ManifestHasCommentsAndNesting_IsStillReadCorrectly()
    {
        // A nested object before the block, comments and a trailing comma: all legal to npm, and
        // all things the previous scrape of this file in MSBuild would have read incorrectly.
        var path = Write("package.json", """
            {
              "exports": { "./thing": { "import": "./a.js" } },
              // the compiler
              "devDependencies": { "typescript": "^7.0.2" },
              "dependencies": { "marked": "^18.0.0", }
            }
            """);

        var manifest = PackageManifest.Read(path).ShouldNotBeNull();

        manifest.Dependencies.Select(d => d.Name).ShouldBe(["marked"]);
        manifest.DevDependencies.Select(d => d.Name).ShouldBe(["typescript"]);
    }

    [Fact]
    public void Read_ScopedPackage_ReadsItsNameAndRange()
    {
        var path = Write("package.json", """{"dependencies":{"@scope/widgets":"1.2.3"}}""");

        var package = PackageManifest.Read(path).ShouldNotBeNull().Dependencies.Single();

        package.Name.ShouldBe("@scope/widgets");
        package.Range.ShouldBe("1.2.3");
    }

    [Fact]
    public void Read_ManifestIsUnreadable_ReturnsNullRatherThanThrowing()
    {
        PackageManifest.Read(Write("package.json", "{ not json")).ShouldBeNull();
        PackageManifest.Read(Path.Combine(_root, "absent.json")).ShouldBeNull();
    }

    [Fact]
    public void Nearest_ManifestIsAboveTheDirectory_FindsIt()
    {
        Write("package.json", """{"dependencies":{"marked":"^18.0.0"}}""");
        Directory.CreateDirectory(Path.Combine(_root, "src", "app"));

        PackageManifest.Nearest(Path.Combine(_root, "src", "app"))
            .ShouldNotBeNull().Dependencies.Single().Name.ShouldBe("marked");
    }

    [Fact]
    public void Read_ManifestHasResolutionFields_ExposesThemAll()
    {
        // The same reader serves the build and module resolution, so it has to carry the fields
        // that decide which file a specifier lands on, not just what is declared.
        var path = Write("package.json", """
            {
              "version": "1.2.3",
              "type": "module",
              "main": "./index.cjs",
              "module": "./index.mjs",
              "exports": { ".": { "import": "./index.mjs" } },
              "dependencies": { "marked": "^18.0.0" }
            }
            """);

        var manifest = PackageManifest.Read(path).ShouldNotBeNull();

        manifest.Type.ShouldBe("module");
        manifest.Field("version").ShouldBe("1.2.3");
        manifest.Field("main").ShouldBe("./index.cjs");
        manifest.Field("module").ShouldBe("./index.mjs");
        manifest.TryGetExports(out _).ShouldBeTrue();
        manifest.RuntimeNames.ShouldContain("marked");
    }

    [Fact]
    public void Type_ManifestDeclaresNoType_DefaultsToCommonJs()
    {
        // What Node does, and what decides whether a .js file gets wrapped.
        PackageManifest.Read(Write("package.json", "{}")).ShouldNotBeNull().Type.ShouldBe("commonjs");
    }

    [Fact]
    public void TryGetExports_ManifestHasNoExportsMap_ReturnsFalse()
    {
        PackageManifest.Read(Write("package.json", """{"main":"./index.js"}"""))
            .ShouldNotBeNull().TryGetExports(out _).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // Tree labels. These become path segments, and a version range is not obliged to be a legal
    // file name, so anything that is not a plain version is dropped rather than sanitised.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("^18.0.7", "marked@^18.0.7")]
    [InlineData("~4.5.6", "marked@~4.5.6")]
    [InlineData("1.2.3", "marked@1.2.3")]
    [InlineData("2.0.0-beta.1", "marked@2.0.0-beta.1")]
    [InlineData(">=1.0.0 <2.0.0", "marked")]
    [InlineData("^1.0.0 || ^2.0.0", "marked")]
    [InlineData("*", "marked")]
    [InlineData("latest", "marked")]
    [InlineData("npm:other@^1.0.0", "marked")]
    [InlineData("github:user/repo", "marked")]
    [InlineData("file:../local", "marked")]
    public void Label_RangeIsNotALegalFileName_FallsBackToTheNameAlone(string range, string expected) =>
        AnalyseCommand.Label(new DeclaredPackage("marked", range, Development: false)).ShouldBe(expected);
}
