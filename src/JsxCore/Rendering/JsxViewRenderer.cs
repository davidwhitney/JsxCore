using System.Collections.Concurrent;
using System.Text.Json;
using JsxCore.Compilation;
using JsxCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using JsxCore.Compilation.Modules;

namespace JsxCore.Rendering;

/// <summary>
/// A view to render. <paramref name="RenderMode"/> is what the call site asked for; null leaves the
/// choice to the view's directive, and then to the configured default.
/// </summary>
public sealed record JsxRenderRequest(LocatedView View, object? Model, RenderMode? RenderMode = null)
{
    public DocumentOptions? Document { get; init; }
    public string? Title { get; init; }
}

public sealed class JsxViewRenderer(
    JsxCoreOptions options,
    JsxCompilationService compilation,
    JsxServerRenderer serverRenderer,
    IJsxHotReloadState hotReload,
    JsxRuntimeLayout runtime,
    ILogger<JsxViewRenderer> logger,
    NpmClientGraph? npmGraph = null)
{
    private readonly NpmClientGraph? _npmGraph = npmGraph;
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly JsxCompilationService _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly JsxServerRenderer _serverRenderer = serverRenderer ?? throw new ArgumentNullException(nameof(serverRenderer));
    private readonly IJsxHotReloadState _hotReload = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
    private readonly JsxRuntimeLayout _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly ILogger<JsxViewRenderer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly BuildScopedCache<IReadOnlyDictionary<string, string>> _importMaps = new();

    public async Task<string> RenderAsync(JsxRenderRequest request, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);

        var buildId = _compilation.BuildId;
        var context = BuildContextValues(httpContext);
        var renderMode = ResolveRenderMode(request);

        ServerRenderResult? serverResult;
        if (renderMode is RenderMode.Server or RenderMode.ServerAndClient)
        {
            serverResult = await _serverRenderer.RenderAsync(
                request.View,
                request.Model,
                context,
                httpContext.RequestServices,
                httpContext.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            serverResult = await TryReadHeadAsync(request, context, httpContext).ConfigureAwait(false);
        }

        var assetBase = $"{_options.RequestPath}/v{buildId}";

        var documentContext = new DocumentContext
        {
            ViewName = request.View.ViewName,
            RenderMode = renderMode,
            ServerHtml = serverResult?.Html,
            Head = serverResult?.Head,
            ModelJson = JsonSerializer.Serialize(request.Model, _options.JsonSerializerOptions),
            ContextJson = JsonSerializer.Serialize(context, _options.JsonSerializerOptions),
            ModuleUrl = $"{assetBase}/views/{request.View.ModuleRelativePath}",
            StyleSheets = StyleSheetsFor(request.View, assetBase, buildId),
            Document = request.Document ?? _options.Document,
            TitleOverride = request.Title,
            ImportMap = ImportMapFor(assetBase, buildId),
            ClientSpecifier = _runtime.ClientSpecifier,
            HotReloadEnabled = _hotReload.Enabled,
            HotReloadClientUrl = $"{assetBase}/runtime/hmr-client.js",
            HotReloadEndpoint = $"{_options.RequestPath}/hmr",

            // Asked per request, because a nonce that is reused is not a nonce.
            Nonce = (request.Document ?? _options.Document).Nonce?.Invoke(httpContext),
            Options = _options
        };

        return HtmlDocumentWriter.Write(documentContext);
    }

    /// <summary>
    /// Where this response renders: what the call site asked for, else what the view's directive
    /// prologue asks for, else the configured default.
    /// </summary>
    /// <remarks>
    /// In that order because each is more specific than the next. An endpoint naming a mode is
    /// deciding for one response and knows why; a <c>"use client"</c> at the top of a view is the
    /// view stating where it expects to run, which is right until an endpoint says otherwise.
    /// </remarks>
    public RenderMode ResolveRenderMode(JsxRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RenderMode
               ?? _compilation.Views.ModeFor(request.View.ModuleRelativePath)
               ?? _options.DefaultRenderMode;
    }

    /// <summary>
    /// Reads a client-rendered view's head export so the document still gets its title and meta
    /// tags. A view that cannot be evaluated on the server, because it imports a
    /// browser-only package say, is still perfectly renderable on the client, so this must not fail the page.
    /// </summary>
    private async Task<ServerRenderResult?> TryReadHeadAsync(
        JsxRenderRequest request,
        IReadOnlyDictionary<string, object?> context,
        HttpContext httpContext)
    {
        try
        {
            return await _serverRenderer.ReadHeadAsync(
                request.View,
                request.Model,
                context,
                httpContext.RequestServices,
                httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "JsxCore could not read the head export of the client-rendered view {View}; " +
                "rendering the document without it.", request.View.ViewName);
            return null;
        }
    }

    private Dictionary<string, object?> BuildContextValues(HttpContext httpContext)
    {
        var context = new Dictionary<string, object?>(_options.ContextValues, StringComparer.Ordinal)
        {
            ["path"] = httpContext.Request.Path.Value
        };

        if (httpContext.Items.TryGetValue(JsxContextItems.Key, out var perRequest)
            && perRequest is IDictionary<string, object?> additions)
        {
            foreach (var (key, value) in additions)
            {
                context[key] = value;
            }
        }

        return context;
    }

    private readonly BuildScopedCache<Dictionary<string, IReadOnlyList<string>>> _styleSheets = new();

    /// <summary>
    /// The stylesheets a view brings with it, as URLs, worked out once per view per build.
    /// </summary>
    /// <remarks>
    /// The walk is over a graph that only changes when something recompiles, and the answer is the
    /// same for every request for that view, so it is cached beside the import map rather than
    /// recomputed per response.
    /// </remarks>
    private IReadOnlyList<string> StyleSheetsFor(LocatedView view, string assetBase, string buildId)
    {
        var cache = _styleSheets.Get(buildId, () => new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        lock (cache)
        {
            if (cache.TryGetValue(view.ModuleRelativePath, out var cached))
            {
                return cached;
            }

            // A stylesheet the application already serves keeps its own URL. One JsxCore processed
            // is a build output, served from this build's prefix like any compiled module.
            var styles = _compilation.Views.StylesFor(view.ModuleRelativePath)
                .Select(style => style.StartsWith('/') ? style : $"{assetBase}/views/{style}")
                .ToList();

            cache[view.ModuleRelativePath] = styles;
            return styles;
        }
    }

    private IReadOnlyDictionary<string, string> ImportMapFor(string assetBase, string buildId) =>
        _importMaps.Get(buildId, () =>
        {
            var map = new Dictionary<string, string>(_runtime.BuildImportMap(assetBase), StringComparer.Ordinal);

            // Packages the views import, served from this app. Added without displacing the
            // runtime, which owns its own specifiers and stages its own files.
            if (_npmGraph is not null)
            {
                var manifest = _npmGraph.ForBuild(
                    buildId, _compilation.Layout.OutputDirectory, assetBase, map.Keys.ToList(),
                    _runtime.ClientDependencies);

                foreach (var (specifier, url) in manifest.ImportMap)
                {
                    map.TryAdd(specifier, url);
                }

                foreach (var package in _npmGraph.NotExported)
                {
                    _logger.LogWarning(
                        "JsxCore did not send '{Package}' to the browser: it is a devDependency, which is " +
                        "not published, so a client-rendered view importing it would fail in production. " +
                        "Move it into dependencies if a view needs it.", package);
                }
            }

            foreach (var (specifier, url) in _options.ImportMap)
            {
                map[specifier] = url;
            }

            return map;
        });
}

public static class JsxContextItems
{
    public const string Key = "JsxCore.ViewContext";
}
