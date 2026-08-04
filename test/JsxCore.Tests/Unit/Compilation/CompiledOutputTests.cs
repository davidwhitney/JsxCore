using JsxCore.Compilation;
using Shouldly;

namespace JsxCore.Tests.Unit.Compilation;

/// <summary>
/// Compiled modules whose view is gone.
/// </summary>
/// <remarks>
/// The compiler emits into a directory it never revisits, so a deleted view used to leave its
/// JavaScript behind, get published with the rest of the directory, and go on being served by name
/// from a precompiled application.
/// </remarks>
public class CompiledOutputTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-prune-" + Guid.NewGuid().ToString("n")[..8]);

    private readonly CompilationLayout _layout;

    public CompiledOutputTests()
    {
        var options = new JsxCoreOptions { WorkingDirectory = _root, ViewsDirectory = "Views" };
        _layout = CompilationLayout.Create(options, _root);

        Directory.CreateDirectory(Path.Combine(_layout.ViewsDirectory, "Home"));
        Directory.CreateDirectory(Path.Combine(_layout.OutputDirectory, "Home"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void View(string relative) =>
        File.WriteAllText(Path.Combine(_layout.ViewsDirectory, relative), "export default () => null;");

    private void Emitted(string relative)
    {
        var path = Path.Combine(_layout.OutputDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// emitted");
    }

    private bool EmittedExists(string relative) =>
        File.Exists(Path.Combine(_layout.OutputDirectory, relative));

    private int Prune() => CompiledOutput.PruneOrphans(_layout, new[] { ".tsx", ".jsx" });

    [Fact]
    public void Prune_ViewStillExists_KeepsItsModuleAndMap()
    {
        View("Home/Index.tsx");
        Emitted("Home/Index.js");
        Emitted("Home/Index.js.map");

        Prune().ShouldBe(0);

        EmittedExists("Home/Index.js").ShouldBeTrue();
        EmittedExists("Home/Index.js.map").ShouldBeTrue();
    }

    [Fact]
    public void Prune_ViewWasDeleted_RemovesItsModuleAndMap()
    {
        Emitted("Home/Gone.js");
        Emitted("Home/Gone.js.map");

        Prune().ShouldBe(2);

        EmittedExists("Home/Gone.js").ShouldBeFalse();
        EmittedExists("Home/Gone.js.map").ShouldBeFalse();
    }

    [Fact]
    public void Prune_GeneratedOutput_IsLeftAlone()
    {
        // Neither has a source, and neither is an orphan: the manifest and the asset modules are
        // written by JsxCore rather than compiled from anything.
        Emitted("jsxcore-views.json");
        Emitted("_dist/modules/assets/favicon.ico.js");

        Prune().ShouldBe(0);

        EmittedExists("jsxcore-views.json").ShouldBeTrue();
        EmittedExists("_dist/modules/assets/favicon.ico.js").ShouldBeTrue();
    }

    [Fact]
    public void Prune_ViewWrittenInAnotherAcceptedExtension_IsStillItsSource()
    {
        View("Home/Legacy.jsx");
        Emitted("Home/Legacy.js");

        Prune().ShouldBe(0);
        EmittedExists("Home/Legacy.js").ShouldBeTrue();
    }

    [Fact]
    public void Prune_PlainModuleBesideAView_IsNotAnOrphan()
    {
        // A view may import an ordinary .ts module next to it, which the compiler emits too.
        View("Home/helpers.ts");
        Emitted("Home/helpers.js");

        Prune().ShouldBe(0);
        EmittedExists("Home/helpers.js").ShouldBeTrue();
    }

    [Fact]
    public void Prune_StylesheetKeepsItsOwnName_IsMatchedByThatName()
    {
        View("Home/Card.module.css");
        Emitted("Home/Card.module.css");
        Emitted("Home/Orphan.module.css");

        Prune().ShouldBe(1);

        EmittedExists("Home/Card.module.css").ShouldBeTrue();
        EmittedExists("Home/Orphan.module.css").ShouldBeFalse();
    }

    [Fact]
    public void Prune_EveryViewInAFolderWasDeleted_LeavesNoEmptyFolder()
    {
        Emitted("Removed/One.js");

        Prune().ShouldBe(1);

        Directory.Exists(Path.Combine(_layout.OutputDirectory, "Removed")).ShouldBeFalse();
        Directory.Exists(_layout.OutputDirectory).ShouldBeTrue();
    }

    [Fact]
    public void Prune_ViewsWereNeverDeployed_DeletesNothing()
    {
        // A published application: the sources are not there, and every module would look orphaned.
        Emitted("Home/Index.js");
        Directory.Delete(_layout.ViewsDirectory, recursive: true);

        Prune().ShouldBe(0);
        EmittedExists("Home/Index.js").ShouldBeTrue();
    }
}
