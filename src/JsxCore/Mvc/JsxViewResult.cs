using System.Net.Mime;
using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace JsxCore.Mvc;

/// <summary>
/// Renders a JSX/TSX view. Works both as an MVC <see cref="ActionResult"/> and as a minimal API
/// <see cref="IResult"/>, so the same type covers controllers and endpoint routing.
/// </summary>
public sealed class JsxViewResult(string viewName, object? model = null, RenderMode? renderMode = null)
    : ActionResult, IResult, IStatusCodeActionResult
{
    public string ViewName { get; } = viewName ?? throw new ArgumentNullException(nameof(viewName));
    public object? Model { get; } = model;
    public RenderMode? RenderMode { get; } = renderMode;
    public int? StatusCode { get; set; }
    public string ContentType { get; set; } = MediaTypeNames.Text.Html;

    // ---------------------------------------------------------------------------------------
    // Per-response overrides of JsxCoreOptions.Document. Anything left null keeps the value
    // configured at startup, so a result only has to state what it wants to change.
    // ---------------------------------------------------------------------------------------
    public string? Title { get; set; }
    public string? ContainerId { get; set; }
    public string? ModelElementId { get; set; }
    public string? Language { get; set; }
    public string? HeadContent { get; set; }
    public string? BodyContent { get; set; }

    /// <summary>Attributes rendered on the body element, merged over the configured ones.</summary>
    public IDictionary<string, string> BodyAttributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Replaces the built-in document writer for this response only.</summary>
    public Func<DocumentContext, string>? DocumentTemplate { get; set; }

    /// <summary>True when this result changes any document setting.</summary>
    private bool HasDocumentOverrides =>
        Title is not null || ContainerId is not null || ModelElementId is not null
        || Language is not null || HeadContent is not null || BodyContent is not null
        || DocumentTemplate is not null || BodyAttributes.Count > 0;

    /// <summary>Applies this result's overrides on top of the configured document options.</summary>
    internal DocumentOptions? BuildDocumentOptions(DocumentOptions configured)
    {
        if (!HasDocumentOverrides)
        {
            return null;
        }

        var document = configured.Clone();

        document.ContainerId = ContainerId ?? document.ContainerId;
        document.ModelElementId = ModelElementId ?? document.ModelElementId;
        document.Language = Language ?? document.Language;
        document.HeadContent = HeadContent ?? document.HeadContent;
        document.BodyContent = BodyContent ?? document.BodyContent;
        document.Template = DocumentTemplate ?? document.Template;

        foreach (var (name, value) in BodyAttributes)
        {
            document.BodyAttributes[name] = value;
        }

        return document;
    }

    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var controller = context.RouteData.Values.TryGetValue("controller", out var value) ? value?.ToString() : null;
        var area = context.RouteData.Values.TryGetValue("area", out var areaValue) ? areaValue?.ToString() : null;

        return WriteAsync(context.HttpContext, controller, area);
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return WriteAsync(httpContext, controllerName: null, areaName: null);
    }

    private async Task WriteAsync(HttpContext httpContext, string? controllerName, string? areaName)
    {
        var services = httpContext.RequestServices;
        var locator = services.GetRequiredService<ViewLocator>();
        var renderer = services.GetRequiredService<JsxViewRenderer>();
        var options = services.GetRequiredService<JsxCoreOptions>();

        var view = locator.Find(ViewName, controllerName, areaName, out var searched)
            ?? throw new JsxViewNotFoundException(ViewName, searched);

        var request = new JsxRenderRequest(view, Model, RenderMode ?? options.DefaultRenderMode)
        {
            Document = BuildDocumentOptions(options.Document),
            Title = Title
        };

        var html = await renderer.RenderAsync(request, httpContext).ConfigureAwait(false);

        httpContext.Response.StatusCode = StatusCode ?? StatusCodes.Status200OK;
        httpContext.Response.ContentType = ContentType;
        await httpContext.Response.WriteAsync(html, httpContext.RequestAborted).ConfigureAwait(false);
    }
}
