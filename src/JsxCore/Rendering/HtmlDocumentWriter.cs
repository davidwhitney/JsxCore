using System.Net;
using System.Text;
using System.Text.Json;

namespace JsxCore.Rendering;

public static class HtmlDocumentWriter
{
    public static string Write(DocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Document.Template is { } template)
        {
            return template(context);
        }

        var document = context.Document;
        var builder = new StringBuilder(4096);

        builder.Append("<!DOCTYPE html>\n<html");
        if (!string.IsNullOrEmpty(document.Language))
        {
            builder.Append(" lang=\"").Append(Escape(document.Language)).Append('"');
        }
        builder.Append(">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\">\n");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");

        WriteTitle(builder, context, document);

        // Before the view's own head tags, so a link a view declares for itself comes later in the
        // cascade and wins.
        WriteStyleSheets(builder, context);
        WriteHeadTags(builder, context.Head);
        WriteImportMap(builder, context);

        if (!string.IsNullOrEmpty(document.HeadContent))
        {
            builder.Append(document.HeadContent).Append('\n');
        }

        builder.Append("</head>\n<body");
        foreach (var (name, value) in document.BodyAttributes)
        {
            builder.Append(' ').Append(Escape(name)).Append("=\"").Append(Escape(value)).Append('"');
        }
        builder.Append(">\n");

        builder.Append("<div id=\"").Append(Escape(document.ContainerId)).Append("\">");
        if (!string.IsNullOrEmpty(context.ServerHtml))
        {
            builder.Append(context.ServerHtml);
        }
        builder.Append("</div>\n");

        if (RunsOnClient(context.RenderMode))
        {
            WriteModelScript(builder, context, document);
            WriteMountScript(builder, context, document);
        }

        if (context.HotReloadEnabled)
        {
            WriteHotReloadScript(builder, context);
        }

        if (!string.IsNullOrEmpty(document.BodyContent))
        {
            builder.Append(document.BodyContent).Append('\n');
        }

        builder.Append("</body>\n</html>");
        return builder.ToString();
    }

    private static bool RunsOnClient(RenderMode mode) =>
        mode is RenderMode.Client or RenderMode.ServerAndClient;

    private static void WriteTitle(StringBuilder builder, DocumentContext context, DocumentOptions document)
    {
        var title = context.TitleOverride ?? context.Head?.Title ?? document.DefaultTitle;
        if (!string.IsNullOrEmpty(title))
        {
            builder.Append("<title>").Append(Escape(title)).Append("</title>\n");
        }
    }

    private static void WriteHeadTags(StringBuilder builder, HeadDescriptor? head)
    {
        if (head is null)
        {
            return;
        }

        WriteTagList(builder, "meta", head.Meta, selfClosing: true);
        WriteTagList(builder, "link", head.Links, selfClosing: true);
        WriteTagList(builder, "script", head.Scripts, selfClosing: false);
    }

    private static void WriteTagList(
        StringBuilder builder,
        string tag,
        List<Dictionary<string, string>>? entries,
        bool selfClosing)
    {
        foreach (var attributes in entries ?? [])
        {
            builder.Append('<').Append(tag);
            foreach (var (name, value) in attributes)
            {
                builder.Append(' ').Append(Escape(name)).Append("=\"").Append(Escape(value)).Append('"');
            }
            builder.Append(selfClosing ? ">\n" : "></" + tag + ">\n");
        }
    }

    /// <summary>
    /// Emits a link element for every stylesheet the view's imports reach.
    /// </summary>
    /// <remarks>
    /// <c>import "dotnet:wwwroot/app.css"</c> binds nothing, so unlike an image it cannot be
    /// answered by a module that returns a URL: the document is what has to carry it. Written for
    /// every render mode, including server-only, where no JavaScript runs in the browser at all.
    /// </remarks>
    private static void WriteStyleSheets(StringBuilder builder, DocumentContext context)
    {
        foreach (var href in context.StyleSheets)
        {
            builder.Append("<link rel=\"stylesheet\" href=\"").Append(Escape(href)).Append("\">\n");
        }
    }

    private static void WriteImportMap(StringBuilder builder, DocumentContext context)
    {
        var imports = context.ImportMap;

        if (imports.Count == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new { imports });
        builder.Append("<script type=\"importmap\"").Append(NonceAttribute(context)).Append('>')
            .Append(EscapeForScript(json)).Append("</script>\n");
    }

    /// <summary>
    /// The nonce attribute for an inline script, or nothing when the application supplied none.
    /// </summary>
    /// <remarks>
    /// Every inline script JsxCore writes carries it. Missing one is not a partial failure: a
    /// Content-Security-Policy blocks that script, and a page whose import map or model is blocked
    /// does not render at all.
    /// </remarks>
    private static string NonceAttribute(DocumentContext context) =>
        string.IsNullOrEmpty(context.Nonce) ? string.Empty : $" nonce=\"{Escape(context.Nonce)}\"";

    private static void WriteModelScript(StringBuilder builder, DocumentContext context, DocumentOptions document)
    {
        builder.Append("<script type=\"application/json\" id=\"").Append(Escape(document.ModelElementId))
            .Append('"').Append(NonceAttribute(context)).Append('>')
            .Append(EscapeForScript(context.ModelJson))
            .Append("</script>\n");
    }

    private static void WriteMountScript(StringBuilder builder, DocumentContext context, DocumentOptions document)
    {
        builder.Append("<script type=\"module\"").Append(NonceAttribute(context)).AppendLine(">")
            .Append("import Component from ").Append(JsonSerializer.Serialize(context.ModuleUrl)).Append(";\n")
            .Append("import { mountView } from ").Append(JsonSerializer.Serialize(context.ClientSpecifier)).Append(";\n")
            .Append("window.__jsxcore_context = ").Append(EscapeForScript(context.ContextJson)).Append(";\n")
            .Append("mountView(Component, ")
            .Append(JsonSerializer.Serialize(new
            {
                containerId = document.ContainerId,
                modelId = document.ModelElementId,
                // Server markup is already in the container, so attach to it rather than replacing it.
                hydrate = context.RenderMode == RenderMode.ServerAndClient
            }))
            .Append(");\n</script>\n");
    }

    private static void WriteHotReloadScript(StringBuilder builder, DocumentContext context)
    {
        var config = JsonSerializer.Serialize(new
        {
            endpoint = context.HotReloadEndpoint,
            entry = context.ModuleUrl
        });

        builder.Append("<script").Append(NonceAttribute(context)).Append('>').Append("window.__jsxcore_hmr = ").Append(EscapeForScript(config)).Append(";</script>\n")
            .Append("<script type=\"module\"").Append(NonceAttribute(context)).Append(" src=")
            .Append(JsonSerializer.Serialize(context.HotReloadClientUrl))
            .Append("></script>\n");
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Makes JSON safe to embed inside a script element.
    /// </summary>
    /// <remarks>
    /// A literal "&lt;/script&gt;" or "&lt;!--" in the data would otherwise terminate the element
    /// early, and U+2028/U+2029 are line terminators in JavaScript source. Escaping them as \uXXXX
    /// keeps the text valid JSON while making it inert to both the HTML and JS parsers.
    /// System.Text.Json already escapes '&lt;' by default, so for JSON produced here this is a
    /// second line of defence rather than the only one.
    /// </remarks>
    internal static string EscapeForScript(string json) =>
        json.Replace("<", "\\u003C", StringComparison.Ordinal)
            .Replace("\u2028", "\\u2028", StringComparison.Ordinal)
            .Replace("\u2029", "\\u2029", StringComparison.Ordinal);
}
