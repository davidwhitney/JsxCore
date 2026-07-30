using JsxCore.Compilation.Modules;
using Shouldly;

namespace JsxCore.Tests.Unit.Modules;

/// <summary>
/// A CommonJS entry point that is nothing but a re-export names nothing of its own.
/// </summary>
/// <remarks>
/// React's entry files are exactly this: a NODE_ENV branch choosing between a development and a
/// production build. Wrapping one without following it left a module with only a default export,
/// so a view importing jsxs from react/jsx-runtime could not be linked at all.
/// </remarks>
public class CommonJsReExportTests
{
    private const string BranchedReExport = """
        'use strict';
        if (process.env.NODE_ENV === 'production') {
          module.exports = require('./cjs/runtime.production.js');
        } else {
          module.exports = require('./cjs/runtime.development.js');
        }
        """;

    private static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["./cjs/runtime.production.js"] = "exports.jsx = function () {}; exports.jsxs = function () {};",
        ["./cjs/runtime.development.js"] = "exports.jsx = function () {}; exports.jsxs = function () {}; exports.jsxDEV = function () {};"
    };

    private static string Wrap(string source) =>
        CommonJsInterop.Wrap(
            source,
            Sources.Keys.ToDictionary(k => k, k => "/resolved" + k[1..], StringComparer.Ordinal),
            specifier => Sources.GetValueOrDefault(specifier));

    [Fact]
    public void Wrap_EntryOnlyReExports_ExposesTheNamesItPointsAt()
    {
        var wrapped = Wrap(BranchedReExport);

        wrapped.ShouldContain("export const jsx =");
        wrapped.ShouldContain("export const jsxs =");
        wrapped.ShouldContain("export default module.exports;");
    }

    [Fact]
    public void Wrap_BranchesExportDifferentNames_TakesTheUnionWithoutDuplicating()
    {
        var wrapped = Wrap(BranchedReExport);

        // Only the development build has jsxDEV. Exporting it regardless is safe: the value is read
        // off module.exports at run time, so it is undefined when that branch did not run, exactly
        // as it would be in Node. Declaring it twice would not be safe, hence the deduplication.
        wrapped.ShouldContain("export const jsxDEV =");
        wrapped.Split("export const jsx =").Length.ShouldBe(2);
        wrapped.Split("export const jsxs =").Length.ShouldBe(2);
    }

    [Fact]
    public void Wrap_NothingToReadTheTargetsWith_StillProducesAValidModule()
    {
        var wrapped = CommonJsInterop.Wrap(BranchedReExport, new Dictionary<string, string>(StringComparer.Ordinal));

        wrapped.ShouldContain("export default module.exports;");
        wrapped.ShouldNotContain("export const jsx ");
    }

    [Fact]
    public void Wrap_EntryHasItsOwnExportsToo_KeepsBoth()
    {
        var wrapped = Wrap("""
            module.exports = require('./cjs/runtime.production.js');
            exports.extra = 1;
            """);

        wrapped.ShouldContain("export const extra =");
        wrapped.ShouldContain("export const jsxs =");
    }
}
