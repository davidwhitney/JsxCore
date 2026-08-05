using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;

namespace JsxCore.Mvc;

/// <summary>A compiled JSX view bound to MVC's rendering pipeline.</summary>
/// <remarks>
/// Holds no options of its own. Where a view renders is resolved by
/// <see cref="JsxViewRenderer.ResolveRenderMode"/>, so that a controller, a minimal API endpoint
/// and a view's own directive are all read in one place.
/// </remarks>
public sealed class JsxView(string path, JsxViewRenderer renderer) : IView
{
    private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));
    private readonly JsxViewRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly LocatedView? _view;

    [Obsolete("This constructor is deprecated. Use the constructor that takes a path instead.")]
    public JsxView(LocatedView view, JsxViewRenderer renderer)
        : this(view?.SourcePath ?? throw new ArgumentNullException(nameof(view)), renderer)
    {
        _view = view;
    }

    public string Path => _path;

    public LocatedView Located => _view;

    public async Task RenderAsync(ViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Null when the action set nothing, which leaves the choice to the view's directive and
        // then to the configured default.
        var renderMode = context.ViewData.TryGetValue(JsxViewEngine.RenderModeKey, out var value) && value is RenderMode mode
            ? mode
            : (RenderMode?)null;

        var html = await _renderer
            .RenderAsync(new JsxRenderRequest(_view, context.ViewData.Model, renderMode), context.HttpContext)
            .ConfigureAwait(false);

        await context.Writer.WriteAsync(html).ConfigureAwait(false);
    }
}
