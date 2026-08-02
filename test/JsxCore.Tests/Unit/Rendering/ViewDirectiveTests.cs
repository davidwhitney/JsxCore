using JsxCore.Compilation.Assets;
using Shouldly;

namespace JsxCore.Tests.Unit.Rendering;

/// <summary>
/// Reading the <c>"use client"</c> / <c>"use server"</c> prologue a view opens with.
/// </summary>
public class ViewDirectiveTests
{
    [Theory]
    [InlineData("\"use client\";\nexport default function P() {}", RenderMode.Client)]
    [InlineData("\"use server\";\nexport default function P() {}", RenderMode.Server)]

    // tsc emits whichever quote the source used, so both have to be read.
    [InlineData("'use client';\nexport default function P() {}", RenderMode.Client)]
    [InlineData("'use server';\nexport default function P() {}", RenderMode.Server)]

    // A licence header above the directive leaves it a prologue as far as the language is concerned.
    [InlineData("// header\n\"use server\";\nexport default function P() {}", RenderMode.Server)]
    [InlineData("/* header\n   over lines */\n\"use client\";\n", RenderMode.Client)]

    // Other directives are skipped rather than ending the prologue.
    [InlineData("\"use strict\";\n\"use server\";\n", RenderMode.Server)]

    [InlineData("export default function P() {}", null)]
    [InlineData("", null)]
    [InlineData("// only a comment", null)]
    public void Parse_ReadsThePrologue(string source, RenderMode? expected) =>
        ViewDirectives.Parse(source).ShouldBe(expected);

    [Fact]
    public void Parse_DirectiveAfterCode_IsNotAPrologue()
    {
        // A string in the middle of a module is an expression, not a directive, and treating it as
        // one would let any component's text change how a page renders.
        var source = """
            import { Card } from "./Card.js";
            "use server";
            export default function P() { return null; }
            """;

        ViewDirectives.Parse(source).ShouldBeNull();
    }

    [Fact]
    public void Parse_UnknownDirectiveOnly_IsNoDirective() =>
        ViewDirectives.Parse("\"use strict\";\nexport default function P() {}").ShouldBeNull();

    [Fact]
    public void Parse_TheFirstRecognisedDirectiveWins() =>
        ViewDirectives.Parse("\"use server\";\n\"use client\";\n").ShouldBe(RenderMode.Server);

    [Fact]
    public void ModeFor_ReadsOnlyTheModuleAsked()
    {
        // A directive on an imported component says nothing about the response.
        var manifest = new ViewManifest();
        manifest.Modules["Home/Index.js"] = new ViewModule(["Shared/Card.js"], []);
        manifest.Modules["Shared/Card.js"] = new ViewModule([], [], RenderMode.Server);

        manifest.ModeFor("Home/Index.js").ShouldBeNull();
        manifest.ModeFor("Shared/Card.js").ShouldBe(RenderMode.Server);
    }

    [Fact]
    public void Manifest_RoundTripsTheMode()
    {
        var manifest = new ViewManifest();
        manifest.Modules["Home/Index.js"] = new ViewModule([], [], RenderMode.ServerAndClient);

        var json = manifest.ToJson();

        // Written by name, so a reordered enum cannot change what an existing manifest means.
        json.ShouldContain("\"ServerAndClient\"");
        ViewManifest.Parse(json).ModeFor("Home/Index.js").ShouldBe(RenderMode.ServerAndClient);
    }
}
