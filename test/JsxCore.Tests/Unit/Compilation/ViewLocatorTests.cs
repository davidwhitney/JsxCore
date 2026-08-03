using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Compilation;

public class ViewLocatorTests
{
    private static (ViewLocator Locator, string Root) Setup(params string[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-locator", Guid.NewGuid().ToString("n")[..8]);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "export default function V() { return null; }");
        }

        var options = new JsxCoreOptions();
        return (new ViewLocator(options, CompilationLayout.Create(options, root), root), root);
    }

    [Fact]
    public void Find_ControllerAndViewName_ResolvesTheView()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx");

        var view = locator.Find("Index", "Home", null, out _);

        view.ShouldNotBeNull();
        view!.RelativePath.ShouldBe("Home/Index");
        view.ModuleRelativePath.ShouldBe("Home/Index.js");
    }

    [Fact]
    public void Find_ViewIsOnlyInShared_FallsBackToTheSharedFolder()
    {
        var (locator, _) = Setup("Views/Shared/Error.tsx");

        locator.Find("Error", "Home", null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_ViewNameIsAPath_ResolvesIt()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("Home/Index", null, null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_BothTsxAndJsxExist_PrefersTsx()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx", "Views/Home/Index.jsx");

        locator.Find("Index", "Home", null, out _)!.SourcePath.ShouldEndWith(".tsx");
    }

    [Fact]
    public void Find_ViewIsJsx_ResolvesIt()
    {
        var (locator, _) = Setup("Views/Home/Plain.jsx");

        locator.Find("Plain", "Home", null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_PathIsContentRootRelative_ResolvesIt()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("~/Views/Home/Index.tsx", null, null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_ViewNameIsAnAbsolutePath_ResolvesIt()
    {
        // A minimal API can name a view by absolute path. The extension is what says so.
        var (locator, root) = Setup("Views/Home/Index.tsx");
        var absolute = Path.Combine(root, "Views", "Home", "Index.tsx");

        var view = locator.Find(absolute, null, null, out _);

        view.ShouldNotBeNull();
        view!.RelativePath.ShouldBe("Home/Index");
    }

    [Fact]
    public void Find_NameWithALeadingSlashAndNoExtension_IsAViewNotAPath()
    {
        // "/Home/Index" is spelled like an absolute path everywhere except Windows. Having no
        // extension is what makes it a view name, so it goes through the location formats.
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("/Home/Index", null, null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_NameWithALeadingSlashAndNoExtension_StillReachesShared()
    {
        // The point of routing it through the formats rather than treating it as a path: it picks
        // up the Shared fallback, which an explicit path never would.
        var (locator, _) = Setup("Views/Shared/Error.tsx");

        locator.Find("/Error", "Home", null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_RelativeNameWithAnExtension_IsAPath()
    {
        // Resolved against the views directory. Before the extension rule this went through the
        // formats, which appended a second extension and found nothing.
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("Home/Index.tsx", null, null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_ContentRootPathWithoutAnExtension_StillResolves()
    {
        // "~/" says "this is a path" on its own, so it does not need an extension to be one.
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("~/Views/Home/Index", null, null, out _).ShouldNotBeNull();
    }

    [Fact]
    public void Find_AbsolutePathThatIsNotThere_DoesNotFallBackToAViewName()
    {
        // An extension means a file, so a missing one is missing rather than being retried as a
        // name that happens to match something under the views directory.
        var (locator, root) = Setup("Views/Home/Index.tsx");

        locator.Find(Path.Combine(root, "Elsewhere", "Index.tsx"), null, null, out _).ShouldBeNull();
    }

    [Fact]
    public void Find_ViewIsMissing_ReportsEveryLocationSearched()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx");

        locator.Find("Missing", "Home", null, out var searched).ShouldBeNull();

        searched.ShouldNotBeEmpty();
        searched.ShouldContain(path => path.EndsWith(Path.Combine("Home", "Missing.tsx")));
        searched.ShouldContain(path => path.EndsWith(Path.Combine("Shared", "Missing.tsx")));
    }

    [Fact]
    public void Enumerate_ViewTree_ReturnsEveryView()
    {
        var (locator, _) = Setup("Views/Home/Index.tsx", "Views/Shared/Card.tsx", "Views/notes.md");

        locator.EnumerateAll().Select(v => v.RelativePath).OrderBy(p => p)
            .ShouldBe(["Home/Index", "Shared/Card"]);
    }
}
