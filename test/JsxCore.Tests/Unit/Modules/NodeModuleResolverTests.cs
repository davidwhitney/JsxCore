using JsxCore.Compilation;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Modules;

namespace JsxCore.Tests.Unit.Modules;

public class NodeModuleResolverTests
{
    private static NodeModuleResolver Resolver() => new(JsxProjectFixture.RepositoryRoot());

    [Fact]
    public void Resolve_PackageHasAnExportsMap_ReturnsItsEsModuleEntry()
    {
        var resolved = Resolver().Resolve("nanoid").ShouldNotBeNull();

        resolved.Kind.ShouldBe(NodeModuleKind.EsModule);
        resolved.Path.ShouldContain("nanoid");
    }

    [Fact]
    public void Resolve_SpecifierIsASubpath_ResolvesThroughTheExportsMap()
    {
        var resolved = Resolver().Resolve("nanoid/non-secure").ShouldNotBeNull();

        resolved.Path.Replace('\\', '/').ShouldContain("non-secure");
    }

    [Fact]
    public void Resolve_PackageShipsBothBuilds_PrefersTheImportCondition()
    {
        // date-fns ships both; the ESM entry is the one that can be loaded.
        var resolved = Resolver().Resolve("date-fns").ShouldNotBeNull();

        resolved.Kind.ShouldBe(NodeModuleKind.EsModule);
        resolved.Path.ShouldNotEndWith(".cjs");
    }

    [Fact]
    public void KindOf_CommonJsPackage_IsIdentifiedAsCommonJs()
    {
        Resolver().Resolve("classnames").ShouldNotBeNull().Kind.ShouldBe(NodeModuleKind.CommonJs);
    }

    [Fact]
    public void Resolve_PackageIsNotInstalled_ReturnsNull()
    {
        Resolver().Resolve("this-package-does-not-exist").ShouldBeNull();
    }

    [Fact]
    public void RuntimeDependencies_ManifestIsPresent_ExcludesDevDependencies()
    {
        var dependencies = Resolver().RuntimeDependencies;

        dependencies.ShouldContain("marked");
        // typescript builds the application; it is a devDependency and must not count as one.
        dependencies.ShouldNotContain("typescript");
    }

    [Theory]
    [InlineData("marked", "marked")]
    [InlineData("nanoid/non-secure", "nanoid")]
    [InlineData("@scope/widgets/dist/x.js", "@scope/widgets")]
    public void PackageNameOf_SpecifierIsASubpath_ReturnsThePackageName(string specifier, string expected) =>
        NodeModuleResolver.PackageNameOf(specifier).ShouldBe(expected);

    [Theory]
    // Compiled JSX puts prose next to element calls, so a view whose text contains "from" ends up
    // emitting from ", _jsx(" and similar. None of these are module specifiers.
    [InlineData(", _jsx(")]
    [InlineData("some words")]
    [InlineData("a; b")]
    [InlineData("x)")]
    public void IsBareSpecifier_TextFromCompiledJsx_ReturnsFalse(string text) =>
        NodeModuleResolver.IsBareSpecifier(text).ShouldBeFalse();
}
