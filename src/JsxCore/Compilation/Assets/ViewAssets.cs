namespace JsxCore.Compilation.Assets;

/// <summary>
/// What a view means when it imports a static asset, and what it gets back.
/// </summary>
/// <remarks>
/// <para>
/// Static assets live where they always have in an ASP.NET Core application: in <c>wwwroot</c>,
/// served by <c>UseStaticFiles</c>. JsxCore copies nothing out of it. What it adds is the half a
/// frontend developer expects and the framework has no answer for: that
/// <c>import logo from "/images/logo.svg"</c> resolves, type checks, and hands back the URL the
/// file is served from.
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
    /// How a view names a file in the application's web root: <c>/images/logo.svg</c>.
    /// </summary>
    /// <remarks>
    /// The URL the file is served from, written exactly as it would be in an <c>src</c> attribute.
    /// That is the whole appeal: the import and the hand-written URL are the same string, and it is
    /// the spelling every Vite project already uses for its public directory.
    /// <para>
    /// A leading slash is a legal ESM specifier that no browser could do anything useful with here,
    /// since it would fetch an image and refuse it as a module, so nothing that worked before means
    /// something different now.
    /// </para>
    /// </remarks>
    public const string RootPrefix = "/";

    /// <summary>The ambient declarations that make an asset import type check.</summary>
    public const string DeclarationFileName = "jsxcore-assets.d.ts";

    /// <summary>What the linker recorded for the renderer, written into the compiled output.</summary>
    public const string ManifestFileName = "jsxcore-views.json";

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
        if (!specifier.StartsWith(RootPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var path = specifier[RootPrefix.Length..];

        // Query strings and fragments are not part of this yet, and treating "logo.svg?url" as an
        // ordinary asset would produce a module nothing points at.
        return path.Length > 0 && !path.Contains('?') && !path.Contains('#') ? path : null;
    }

    /// <summary>
    /// Whether a specifier names an asset but not one this can serve, which is how a relative
    /// import of an image is told apart from a rooted one.
    /// </summary>
    /// <remarks>
    /// The ambient declarations have to be plain <c>*.svg</c> wildcards, because TypeScript rejects
    /// a pattern beginning with a slash as a relative module name. So an import of
    /// <c>./logo.svg</c> type checks as readily as a rooted one, and would otherwise fail as a
    /// module the browser cannot load, with nothing said about why.
    /// </remarks>
    public static bool IsMisplacedAsset(string specifier) =>
        PathFor(specifier) is null && IsAssetSpecifier(specifier);

    private static bool IsAssetSpecifier(string specifier) =>
        specifier.Length > 0
        && !specifier.Contains('?')
        && !specifier.Contains('#')
        && IsAsset(specifier);

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
    /// The module a stylesheet import resolves to.
    /// </summary>
    /// <remarks>
    /// A CSS module exports the scoped class names, which is what <c>styles.card</c> reads. An
    /// ordinary stylesheet binds nothing, so its module exports the URL instead, which costs
    /// nothing and gives anyone who wants the href a way to get it.
    /// </remarks>
    public static string StyleModuleSource(string url, string? names) =>
        names is null
            ? $"""
               // Written by JsxCore for a stylesheet a view imports. The document links it; this
               // exists so the import has something to resolve to.
               const url = {Quote(url)};
               export default url;

               """
            : $"""
               // Written by JsxCore for a CSS module. The class names are esbuild's, scoped so two
               // components can use the same one without colliding.
               const styles = {names};
               export default styles;

               """;

    /// <summary>
    /// Ambient declarations so <c>import logo from "/images/logo.svg"</c> type checks.
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
        builder.AppendLine("//     import logo from \"/images/logo.svg\";");
        builder.AppendLine("//     <img src={logo} />");
        builder.AppendLine("//");
        builder.AppendLine("// The path is the URL, so it starts at your web root. A relative import of an image");
        builder.AppendLine("// type checks against these too, but nothing serves it, and the build says so.");
        builder.AppendLine();

        builder.AppendLine("declare module \"*.module.css\" {");
        builder.AppendLine("    const classes: { readonly [name: string]: string };");
        builder.AppendLine("    export default classes;");
        builder.AppendLine("}");
        builder.AppendLine();

        foreach (var extension in ContentTypes.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            // A pattern beginning with a slash is rejected as a relative module name, so these
            // cannot be narrowed to rooted specifiers. The linker reports the difference instead.
            builder.AppendLine($"declare module \"*{extension}\" {{");
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
