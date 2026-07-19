using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Tests.Unit.PackageManagement;

public class ToolchainLocatorTests
{
    [Fact]
    public void CandidatePaths_AnyPlatform_ArePlatformSpecific()
    {
        var candidates = TypeScriptToolchainLocator.CandidatePaths("/some/app");

        candidates.ShouldNotBeEmpty();
        candidates.ShouldAllBe(path => path.Contains("node_modules") && path.Contains("@typescript"));
        candidates[0].ShouldContain(TypeScriptToolchainLocator.PlatformPackageName());
    }

    [Fact]
    public void CandidatePaths_InstallIsAtSolutionLevel_IncludesAncestorDirectories()
    {
        // Rooted the way this platform roots things: "/a/b/c" is absolute on Unix but relative to
        // the current drive on Windows, where the walk would start somewhere else entirely.
        var deepest = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jsxcore-locator", "b", "c"));
        var ancestor = Path.GetDirectoryName(Path.GetDirectoryName(deepest)!)!;

        var candidates = TypeScriptToolchainLocator.CandidatePaths(deepest);

        candidates.ShouldContain(path =>
            path.StartsWith(deepest + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        candidates.ShouldContain(path =>
            path.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    [Fact]
    public void Locate_CompilerIsInstalled_IdentifiesTheNativeCompiler()
    {
        var toolchain = JsxProjectFixture.Toolchain;

        toolchain.MajorVersion.ShouldBeGreaterThanOrEqualTo(7);
        toolchain.IsNative.ShouldBeTrue();
        File.Exists(toolchain.ExecutablePath).ShouldBeTrue();
    }

    [Fact]
    public void Probe_PathIsNotACompiler_ReturnsNull()
    {
        TypeScriptToolchainLocator.Locate("/nonexistent", explicitPath: "/definitely/not/here").ShouldBeNull();
    }
}
