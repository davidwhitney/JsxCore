using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Rendering;

public class HtmlDocumentWriterTests
{
    private static DocumentContext Context(RenderMode mode, JsxCoreOptions? options = null) => new()
    {
        ViewName = "Home/Index",
        RenderMode = mode,
        ServerHtml = mode == RenderMode.Client ? null : "<p>server</p>",
        ModelJson = """{"a":1}""",
        ContextJson = """{"path":"/"}""",
        ModuleUrl = "/_jsx/vabc/views/Home/Index.js",
        ImportMap = new Dictionary<string, string> { ["dotnet:rendering"] = "/_jsx/vabc/runtime/index.js" },
        Options = options ??= new JsxCoreOptions(),
        Document = options.Document,
        ClientSpecifier = "@jsxcore/preact/client"
    };

    [Fact]
    public void Write_ClientMode_EmitsTheModelAndMountScriptButNoMarkup()
    {
        var html = HtmlDocumentWriter.Write(Context(RenderMode.Client));

        html.ShouldContain("""<div id="jsxcore-root"></div>""");
        html.ShouldContain("""<script type="application/json" id="jsxcore-model">""");
        html.ShouldContain("mountView(Component");
    }

    [Fact]
    public void Write_ServerMode_EmitsMarkupAndNoMountScript()
    {
        var html = HtmlDocumentWriter.Write(Context(RenderMode.Server));

        html.ShouldContain("<p>server</p>");
        html.ShouldNotContain("mountView");
        html.ShouldNotContain("jsxcore-model");
    }

    [Fact]
    public void Write_HybridMode_EmitsMarkupAndAMountScript()
    {
        var html = HtmlDocumentWriter.Write(Context(RenderMode.ServerAndClient));

        html.ShouldContain("<p>server</p>");
        html.ShouldContain("mountView");
    }

    [Fact]
    public void Write_HeadDescriptors_AreRenderedIntoTheDocument()
    {
        var context = Context(RenderMode.Server);
        var withHead = new DocumentContext
        {
            ViewName = context.ViewName,
            RenderMode = context.RenderMode,
            ServerHtml = context.ServerHtml,
            ModelJson = context.ModelJson,
            ContextJson = context.ContextJson,
            ModuleUrl = context.ModuleUrl,
            ImportMap = context.ImportMap,
            Options = context.Options,
            Document = context.Document,
            ClientSpecifier = context.ClientSpecifier,
            Head = new HeadDescriptor
            {
                Title = "A & B",
                Meta = [new Dictionary<string, string> { ["name"] = "description", ["content"] = "hi" }],
                Links = [new Dictionary<string, string> { ["rel"] = "icon", ["href"] = "/f.ico" }]
            }
        };

        var html = HtmlDocumentWriter.Write(withHead);

        html.ShouldContain("<title>A &amp; B</title>");
        html.ShouldContain("""<meta name="description" content="hi">""");
        html.ShouldContain("""<link rel="icon" href="/f.ico">""");
    }

    [Fact]
    public void Write_ModelContainsAClosingScriptTag_EscapesItSafely()
    {
        HtmlDocumentWriter.EscapeForScript("""{"x":"</script><script>alert(1)</script>"}""")
            .ShouldNotContain("</script>");
    }

    [Fact]
    public void Write_CustomTemplate_ReplacesTheEntireDocument()
    {
        var options = new JsxCoreOptions();
        options.Document.Template = context => $"custom:{context.ViewName}";

        HtmlDocumentWriter.Write(Context(RenderMode.Client, options)).ShouldBe("custom:Home/Index");
    }
}
