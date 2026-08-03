using System.Text.RegularExpressions;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// The stylesheets a view can import: from the web root, from beside the component, and from an
/// npm package, plus CSS modules with scoped class names.
/// </summary>
/// <remarks>
/// Compiled with the real toolchain and processed with the real esbuild, because the scoping is
/// esbuild's and a stub would prove nothing about it.
/// </remarks>
public class StylesheetTests
{
    private static IReadOnlyList<string> LinksIn(string html) =>
        Regex.Matches(html, @"<link rel=""stylesheet"" href=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

    [Fact]
    public async Task Stylesheet_BesideAComponent_IsServedAndLinked()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/page.css", ".page { color: rebeccapurple; }");
        project.AddView("Home/Index.tsx", """
            import "./page.css";
            export default function Index() { return <p>styled</p>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        var href = LinksIn(html).ShouldHaveSingleItem();
        href.ShouldContain("/_dist/css/Home/page.css");

        var response = await host.Client.GetAsync(href);
        (await response.Content.ReadAsStringAsync()).ShouldContain("rebeccapurple");
    }

    [Fact]
    public async Task CssModule_ScopesItsClassNames()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Card.module.css", ".card { padding: 24px; }");
        project.AddView("Home/Index.tsx", """
            import styles from "./Card.module.css";
            export default function Index() { return <div class={styles.card}>scoped</div>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        // The rendered class is the scoped one, not the name as written.
        var rendered = Regex.Match(html, @"<div class=""(?<name>[^""]+)"">scoped</div>").Groups["name"].Value;
        rendered.ShouldNotBe("card");
        rendered.ShouldContain("card");

        // And the stylesheet agrees with it.
        var css = await host.Client.GetStringAsync(LinksIn(html).ShouldHaveSingleItem());
        css.ShouldContain("." + rendered);
    }

    [Fact]
    public async Task CssModules_WithTheSameFileName_DoNotCollide()
    {
        // esbuild disambiguates within one invocation, which is why every stylesheet goes through
        // a single pass rather than one call each.
        using var project = JsxProjectFixture.Create();
        project.AddView("A/Card.module.css", ".card { color: red; }");
        project.AddView("B/Card.module.css", ".card { color: blue; }");
        project.AddView("Shared/Two.tsx", """
            import a from "../A/Card.module.css";
            import b from "../B/Card.module.css";
            export function Two() { return <><i class={a.card} /><b class={b.card} /></>; }
            """);
        project.AddView("Home/Index.tsx", """
            import { Two } from "../Shared/Two.tsx";
            export default function Index() { return <Two />; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        var first = Regex.Match(html, @"<i class=""(?<n>[^""]+)""").Groups["n"].Value;
        var second = Regex.Match(html, @"<b class=""(?<n>[^""]+)""").Groups["n"].Value;

        first.ShouldNotBeNullOrEmpty();
        first.ShouldNotBe(second);
    }

    [Fact]
    public async Task Stylesheet_FromTheWebRoot_IsNotCopied()
    {
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/css/site.css", "body { margin: 0; }");
        project.AddView("Home/Index.tsx", """
            import "/css/site.css";
            export default function Index() { return <p>styled</p>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        // Its own URL, unversioned: the application already serves it and one file keeps one URL.
        LinksIn(html).ShouldHaveSingleItem().ShouldBe("/css/site.css");
    }

    [Fact]
    public async Task Stylesheets_AreLinkedDependenciesFirst()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/card.css", ".card { color: red; }");
        project.AddView("Home/page.css", ".page { color: blue; }");
        project.AddView("Shared/Card.tsx", """
            import "./card.css";
            export function Card() { return <div class="card" />; }
            """);
        project.AddView("Home/Index.tsx", """
            import "./page.css";
            import { Card } from "../Shared/Card.tsx";
            export default function Index() { return <Card />; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var links = LinksIn(await host.GetStringAsync("/server/Index"));

        links.Count.ShouldBe(2);
        links[0].ShouldContain("Shared/card.css");
        links[1].ShouldContain("Home/page.css");
    }

    [Fact]
    public async Task Stylesheet_FromAnNpmPackage_IsServedAndLinked()
    {
        // A third-party component's own styles, which is the case that used to fail outright:
        // the specifier was reported as misplaced and the page rendered unstyled.
        JsxProjectFixture.EnsureRepositoryPackages();

        using var project = JsxProjectFixture.Create();
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        project.AddFile("node_modules/fake-widget/package.json", """{ "name": "fake-widget" }""");
        project.AddFile("node_modules/fake-widget/styles.css", ".widget { border: 1px solid; }");
        project.AddView("Home/Index.tsx", """
            import "fake-widget/styles.css";
            export default function Index() { return <p>widget</p>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        var href = LinksIn(html).ShouldHaveSingleItem();
        (await host.Client.GetStringAsync(href)).ShouldContain("1px solid");
    }

    [Fact]
    public async Task Stylesheet_ThatIsNotThere_IsReported()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            import "./missing.css";
            export default function Index() { return <p>x</p>; }
            """);

        await project.CompileAsync();
        var linked = JsxCore.Compilation.Assets.ViewAssetLinker.Link(project.Layout);

        linked.Misplaced.ShouldContain("./missing.css");
    }

    [Fact]
    public async Task Stylesheet_NoLongerImported_IsRemovedFromTheOutput()
    {
        // Everything generated lives under one root so that one sweep cleans it. Before that, only
        // the module directory was pruned, and an orphaned stylesheet stayed on disk and stayed
        // served for as long as the build id it was published under remained current.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/page.css", ".page { color: rebeccapurple; }");
        project.AddView("Home/Index.tsx", """
            import "./page.css";
            export default function Index() { return <p>styled</p>; }
            """);

        await project.CompileAsync();

        var stylesheet = JsxCore.Compilation.Assets.ViewAssets.PathUnder(
            project.Layout.OutputDirectory,
            JsxCore.Compilation.Assets.ViewAssets.StyleDirectory + "/Home/page.css");

        File.Exists(stylesheet).ShouldBeTrue();

        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>plain</p>; }
            """);

        await project.CompileAsync();

        File.Exists(stylesheet).ShouldBeFalse();
    }

    [Fact]
    public async Task Stylesheet_ThatChanged_MovesTheBuildId()
    {
        // Asset URLs carry the build id and are served immutable for a year, so anything the
        // browser fetches has to be part of the hash. Stylesheets were not, and a release that
        // changed only CSS reused a URL that browsers had been told to keep.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/page.css", ".page { color: red; }");
        project.AddView("Home/Index.tsx", """
            import "./page.css";
            export default function Index() { return <p>styled</p>; }
            """);

        var before = await project.CompileAsync();

        project.AddView("Home/page.css", ".page { color: blue; }");
        var after = await project.CompileAsync();

        after.BuildId.ShouldNotBe(before.BuildId);
    }
}
