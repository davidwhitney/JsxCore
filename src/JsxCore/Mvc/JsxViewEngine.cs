using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Options;

namespace JsxCore.Mvc;

/// <summary>
/// An MVC view engine backed by compiled .tsx/.jsx files, so <c>return View()</c> resolves
/// <c>Views/Home/Index.tsx</c> the same way it would resolve a Razor view.
/// </summary>
public sealed class JsxViewEngine(ViewLocator locator, JsxViewRenderer renderer, JsxCoreOptions options) : IViewEngine
{
    /// <summary>ViewData key used to override the render mode for a single view.</summary>
    public const string RenderModeKey = "JsxCore.RenderMode";

    private readonly ViewLocator _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    private readonly JsxViewRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(viewName);

        var controller = RouteValue(context, "controller");
        var area = RouteValue(context, "area");

        var view = _locator.Find(viewName, controller, area, out var searched);

        return view is null
            ? ViewEngineResult.NotFound(viewName, searched)
            : ViewEngineResult.Found(viewName, new JsxView(view, _renderer, _options));
    }

    public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage)
    {
        ArgumentException.ThrowIfNullOrEmpty(viewPath);

        var view = _locator.Find(viewPath, controllerName: null, areaName: null, out var searched);

        return view is null
            ? ViewEngineResult.NotFound(viewPath, searched)
            : ViewEngineResult.Found(viewPath, new JsxView(view, _renderer, _options));
    }

    private static string? RouteValue(ActionContext context, string key) =>
        context.RouteData.Values.TryGetValue(key, out var value) ? value?.ToString() : null;
}

/// <summary>A compiled JSX view bound to MVC's rendering pipeline.</summary>
public sealed class JsxView(LocatedView view, JsxViewRenderer renderer, JsxCoreOptions options) : IView
{
    private readonly LocatedView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly JsxViewRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string Path => _view.SourcePath;
    public LocatedView Located => _view;

    public async Task RenderAsync(ViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var renderMode = context.ViewData.TryGetValue(JsxViewEngine.RenderModeKey, out var value) && value is RenderMode mode
            ? mode
            : _options.DefaultRenderMode;

        var html = await _renderer
            .RenderAsync(new JsxRenderRequest(_view, context.ViewData.Model, renderMode), context.HttpContext)
            .ConfigureAwait(false);

        await context.Writer.WriteAsync(html).ConfigureAwait(false);
    }
}

/// <summary>Inserts the JSX view engine into MVC's view engine list.</summary>
internal sealed class JsxMvcViewOptionsSetup(JsxViewEngine viewEngine, JsxCoreOptions options)
    : IConfigureOptions<MvcViewOptions>
{
    public void Configure(MvcViewOptions options1)
    {
        var index = Math.Clamp(options.ViewEngineOrder, 0, options1.ViewEngines.Count);
        options1.ViewEngines.Insert(index, viewEngine);
    }
}
