using System.Text;
using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Assets;

namespace JsxCore.Hosting;

/// <summary>
/// Serves compiled view modules and the JsxCore runtime.
/// </summary>
/// <remarks>
/// URLs carry the build id as a path segment, as in <c>/_jsx/v{buildId}/views/Home/Index.js</c>. That
/// makes every asset immutable and safely cacheable forever, and it means a relative import inside
/// a module resolves within the same versioned prefix, so a rebuild swaps the entire graph at once.
/// Requests carrying a stale build id are still served if the file exists, so pages loaded just
/// before a rebuild do not break.
/// </remarks>
public sealed class JsxAssetMiddleware(
    RequestDelegate next,
    JsxCoreOptions options,
    JsxCompilationService compilation,
    JsxHotReloadService hotReload,
    JsxRuntimeLayout runtime,
    ILogger<JsxAssetMiddleware> logger,
    IHostEnvironment environment,
    NpmClientGraph? npmGraph = null)
{
    private const string ViewsSegment = "views";
    private const string RuntimeSegment = "runtime";
    private const string NpmSegment = "npm";

    // Development only: it describes the build rather than the request, so a public server has no
    // reason to volunteer it.
    private const string FrameworkHeader = "X-JsxCore-Framework";

    private readonly NpmClientGraph? _npmGraph = npmGraph;
    private readonly bool _development = environment?.IsDevelopment() ?? false;

    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly JsxCompilationService _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly JsxHotReloadService _hotReload = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
    private readonly JsxRuntimeLayout _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly ILogger<JsxAssetMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InvokeAsync(HttpContext context)
    {
        // On every response, not just on assets: the question this answers is "what is this
        // application rendering with", and the request people ask it of is the page.
        if (_development)
        {
            context.Response.Headers[FrameworkHeader] = _runtime.Name;
        }

        if (!context.Request.Path.StartsWithSegments(_options.RequestPath, out var remainder))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (remainder.StartsWithSegments("/hmr"))
        {
            await HandleHotReloadAsync(context).ConfigureAwait(false);
            return;
        }

        var asset = Resolve(remainder.Value);
        if (asset is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        await ServeAsync(context, asset).ConfigureAwait(false);
    }

    private async Task HandleHotReloadAsync(HttpContext context)
    {
        if (!_hotReload.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("The JsxCore hot reload endpoint requires a WebSocket request.")
                .ConfigureAwait(false);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await _hotReload.AcceptAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>An asset to serve: either a file on disk or an embedded runtime resource.</summary>
    internal sealed record ResolvedAsset(string Name, string? FilePath, byte[]? Content);

    /// <summary>
    /// Maps "/v{buildId}/views/..." onto compiled files, and "/v{buildId}/runtime/..." onto the
    /// runtime embedded in this assembly. Returns null when the path is not one of ours, escapes
    /// its root, or does not exist.
    /// </summary>
    internal ResolvedAsset? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || segments[0].Length < 2 || segments[0][0] != 'v')
        {
            return null;
        }

        var relative = string.Join('/', segments.Skip(2));

        // The built-in runtime never touches disk: it is served out of the assembly manifest.
        if (segments[1] == RuntimeSegment)
        {
            var content = RuntimeAssets.TryGetContent(relative);
            return content is null ? null : new ResolvedAsset(relative, null, content);
        }

        // npm packages are served only where a view actually reaches them, and in the form the
        // browser needs, so this goes through the prepared manifest rather than to disk.
        if (segments[1] == NpmSegment)
        {
            var assetBase = $"{_options.RequestPath}/v{_compilation.BuildId}";
            var manifest = _npmGraph?.ForBuild(
                _compilation.BuildId,
                _compilation.Layout.OutputDirectory,
                assetBase,
                _runtime.BuildImportMap(assetBase).Keys.ToList(),
                _runtime.ClientDependencies);

            return manifest is not null && manifest.Assets.TryGetValue(relative, out var package)
                ? new ResolvedAsset(relative, null, Encoding.UTF8.GetBytes(package.Content))
                : null;
        }

        // A staged runtime (Preact copied out of node_modules) is served from its directory.
        if (_runtime.Directory is { } runtimeDirectory && segments[1] == _runtime.AssetSegment)
        {
            return ResolveUnder(runtimeDirectory, relative);
        }

        if (segments[1] != ViewsSegment)
        {
            return null;
        }

        return ResolveUnder(_compilation.Layout.OutputDirectory, relative);
    }

    private ResolvedAsset? ResolveUnder(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        // Path traversal guard: the combined path must stay inside the directory it resolved against.
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("JsxCore rejected an asset path that escaped its root: {Path}", relative);
            return null;
        }

        return File.Exists(full) ? new ResolvedAsset(relative, full, null) : null;
    }

    private async Task ServeAsync(HttpContext context, ResolvedAsset asset)
    {
        context.Response.ContentType = ContentTypeFor(asset.Name);

        // Build-id-scoped URLs never change contents, so they can be cached indefinitely. In
        // development the id changes on every edit, which is what makes that safe.
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(365),
            NoTransform = true
        };

        if (asset.Content is { } content)
        {
            context.Response.ContentLength = content.Length;
            await context.Response.Body.WriteAsync(content, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await context.Response.SendFileAsync(asset.FilePath!, context.RequestAborted).ConfigureAwait(false);
    }

    private static string ContentTypeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".js" or ".mjs" or ".cjs" => "text/javascript; charset=utf-8",
        ".map" => "application/json; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".ts" or ".tsx" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}
