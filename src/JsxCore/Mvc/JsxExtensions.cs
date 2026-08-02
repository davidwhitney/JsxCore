using JsxCore.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JsxCore.Mvc;

public static class JsxControllerExtensions
{
    /// <summary>
    /// Renders a JSX view, in the mode its <c>"use client"</c> or <c>"use server"</c> directive
    /// asks for, or the configured default when it declares neither.
    /// </summary>
    public static JsxViewResult Jsx(this ControllerBase controller, string viewName, object? model = null) =>
        new(viewName, model);

    /// <summary>Renders a JSX view in a named mode, whatever its directive says.</summary>
    public static JsxViewResult Jsx(this ControllerBase controller, string viewName, object? model, RenderMode renderMode) =>
        new(viewName, model, renderMode);

    /// <summary>
    /// Renders a JSX view, configuring the result (status code, title, head content and the
    /// other document settings) before it is returned.
    /// </summary>
    public static JsxViewResult Jsx(
        this ControllerBase controller,
        string viewName,
        object? model,
        Action<JsxViewResult> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var result = new JsxViewResult(viewName, model);
        configure(result);
        return result;
    }


    /// <summary>
    /// Adds a value to the <c>context</c> prop that views receive, for this request only.
    /// </summary>
    public static void AddJsxContext(this HttpContext httpContext, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (httpContext.Items.TryGetValue(JsxContextItems.Key, out var existing)
            && existing is IDictionary<string, object?> values)
        {
            values[key] = value;
            return;
        }

        httpContext.Items[JsxContextItems.Key] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [key] = value
        };
    }
}

/// <summary>Minimal API helpers for returning JSX/TSX views.</summary>
public static class JsxResults
{
    /// <summary>
    /// Renders a JSX view. A null <paramref name="renderMode"/> takes the mode from the view's
    /// <c>"use client"</c> or <c>"use server"</c> directive, and then from the configured default.
    /// </summary>
    public static IResult Jsx(string viewName, object? model = null, RenderMode? renderMode = null) =>
        new JsxViewResult(viewName, model, renderMode);

}

/// <summary>
/// Extends <see cref="Results.Extensions"/> so minimal API endpoints can write
/// <c>Results.Extensions.Jsx("Home/Index", model)</c>.
/// </summary>
public static class JsxResultExtensions
{
    /// <summary>
    /// Renders a JSX view. A null <paramref name="renderMode"/> takes the mode from the view's
    /// <c>"use client"</c> or <c>"use server"</c> directive, and then from the configured default.
    /// </summary>
    public static IResult Jsx(this IResultExtensions _, string viewName, object? model = null, RenderMode? renderMode = null) =>
        new JsxViewResult(viewName, model, renderMode);

    /// <summary>
    /// Renders a JSX view, configuring the result (status code, title, head content and the
    /// other document settings) before it is returned.
    /// </summary>
    public static IResult Jsx(
        this IResultExtensions _,
        string viewName,
        object? model,
        Action<JsxViewResult> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var result = new JsxViewResult(viewName, model);
        configure(result);
        return result;
    }

}
