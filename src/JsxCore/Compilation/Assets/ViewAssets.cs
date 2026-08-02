namespace JsxCore.Compilation.Assets;

/// <summary>
/// What a view means when it imports a static asset, and what it gets back.
/// </summary>
/// <remarks>
/// <para>
/// Static assets live where they always have in an ASP.NET Core application: in <c>wwwroot</c>,
/// served by <c>UseStaticFiles</c>. JsxCore copies nothing out of it. What it adds is the half a
/// frontend developer expects and the framework has no answer for: that
/// <c>import logo from "dotnet:wwwroot/images/logo.svg"</c> resolves, type checks, and hands back
/// the URL the file is served from.
/// </para>
/// <para>
/// That needs doing because an svg is not JavaScript. TypeScript leaves a specifier it has no
/// opinion about exactly as written, so the browser would fetch an image where it asked for a
/// module and refuse it. A one-line module is generated for each imported asset instead, exporting
/// its URL, and the emitted import is pointed at that.
/// </para>
/// </remarks>
public static class ViewAssets
{
    /// <summary>
    /// How a view names a file in the application's web root: <c>dotnet:wwwroot/images/logo.svg</c>.
    /// </summary>
    /// <remarks>
    /// The same scheme as the assemblies and the other reserved names, under the same rule:
    /// <c>dotnet:</c> is the .NET side of the application. wwwroot is exactly that.
    /// <para>
    /// A scheme rather than a relative path, because the two ask different questions. A relative
    /// path says where a file sits on this machine; this names one of the application's own served
    /// files, which is what a URL is an answer about. It also cannot be mistaken for an import of
    /// something beside the view, and it does not change meaning when a view moves.
    /// </para>
    /// </remarks>
    public const string Scheme = "dotnet:wwwroot/";

    /// <summary>The ambient declarations that make an asset import type check.</summary>
    public const string DeclarationFileName = "jsxcore-assets.d.ts";

    /// <summary>The module graph recorded for the renderer, written into the compiled output.</summary>
    public const string ManifestFileName = "jsxcore-assets.json";

    /// <summary>
    /// Where the generated modules live, within the compiled output.
    /// </summary>
    /// <remarks>
    /// Gathered in one directory rather than written beside each asset, because the asset is in
    /// wwwroot and JsxCore does not write there: wwwroot is the application's, and a build that
    /// leaves generated files in it is a build that leaves files in source control.
    /// </remarks>
    public const string ModuleDirectory = "_static";

    /// <summary>
    /// Extensions that can be imported, mapped to the content type they are served with.
    /// </summary>
    /// <remarks>
    /// Deliberately the web's asset formats and nothing else. <c>.json</c> is absent: a browser
    /// needs an import attribute to load JSON as a module, so an import of one is a different
    /// feature rather than another entry here.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".css"] = "text/css; charset=utf-8",

            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".avif"] = "image/avif",
            [".ico"] = "image/x-icon",
            [".bmp"] = "image/bmp",

            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".otf"] = "font/otf",
            [".eot"] = "application/vnd.ms-fontobject",

            [".mp3"] = "audio/mpeg",
            [".ogg"] = "audio/ogg",
            [".wav"] = "audio/wav",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",

            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain; charset=utf-8",
            [".csv"] = "text/csv; charset=utf-8",
            [".xml"] = "application/xml"
        };

    /// <summary>Whether a file is one a view can import.</summary>
    public static bool IsAsset(string path) => ContentTypes.ContainsKey(Path.GetExtension(path));

    /// <summary>
    /// Whether a response of this content type is worth compressing on the way out.
    /// </summary>
    /// <remarks>
    /// Text compresses. A PNG or a woff2 is already compressed, and running one through Brotli
    /// again spends CPU to make it very slightly larger.
    /// </remarks>
    public static bool IsCompressible(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The path within the web root that a specifier names, or null when it is not one of ours.
    /// </summary>
    public static string? PathFor(string specifier)
    {
        if (!specifier.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return null;
        }

        var path = specifier[Scheme.Length..];

        // Query strings and fragments are not part of this yet, and treating "logo.svg?url" as an
        // ordinary asset would produce a module nothing points at.
        return path.Length > 0 && !path.Contains('?') && !path.Contains('#') ? path : null;
    }

    /// <summary>The module generated for an imported asset, exporting the URL it is served from.</summary>
    /// <remarks>
    /// A constant, because that URL does not move with a build: it is whatever the static file
    /// middleware has always served the file from. One file, one URL, whether it is reached from a
    /// view, a Razor page or a stylesheet's own <c>url()</c>.
    /// </remarks>
    public static string ModuleSource(string url) =>
        $"""
         // Written by JsxCore for an asset imported by a view. The file itself is served by
         // ASP.NET Core's static file middleware, from wwwroot, exactly as it always was.
         const url = {Quote(url)};
         export default url;

         """;

    /// <summary>
    /// Ambient declarations so <c>import logo from "dotnet:wwwroot/images/logo.svg"</c> type checks.
    /// </summary>
    /// <remarks>
    /// A wildcard module per extension rather than one covering everything: a mistyped specifier
    /// should still be an error a developer can see, not a silent <c>any</c>.
    /// </remarks>
    public static string DeclarationSource()
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("// Written by JsxCore. Importing a file from your web root gives you the URL it is");
        builder.AppendLine("// served from, so it can go straight into an src or href:");
        builder.AppendLine("//");
        builder.AppendLine("//     import logo from \"dotnet:wwwroot/images/logo.svg\";");
        builder.AppendLine("//     <img src={logo} />");
        builder.AppendLine("//");
        builder.AppendLine("// The file is one of your own, under wwwroot, served by UseStaticFiles.");
        builder.AppendLine();

        foreach (var extension in ContentTypes.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            // One wildcard is all a pattern module may have, and it matches path separators, so a
            // single declaration per extension covers every depth of directory under the web root.
            builder.AppendLine($"declare module \"{Scheme}*{extension}\" {{");
            builder.AppendLine("    const url: string;");
            builder.AppendLine("    export default url;");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
