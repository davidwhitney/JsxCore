using JsxCore.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JsxCore.Mvc;

public static class JsxControllerExtensions
{
    /// <summary>Renders a JSX view using the configured default render mode.</summary>
    public static JsxViewResult Jsx(this ControllerBase controller, string viewName, object? model = null) =>
        new(viewName, model);

    /// <summary>Renders a JSX view with an explicit render mode.</summary>
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

    /// <summary>Renders a JSX view on the server, like a traditional view engine.</summary>
    public static JsxViewResult JsxServerRendered(this ControllerBase controller, string viewName, object? model = null) =>
        new(viewName, model, RenderMode.Server);

    /// <summary>Renders a JSX view on the server and mounts it again on the client for interactivity.</summary>
    public static JsxViewResult JsxServerAndClient(this ControllerBase controller, string viewName, object? model = null) =>
        new(viewName, model, RenderMode.ServerAndClient);

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
    /// <summary>Renders a JSX view using the configured default render mode.</summary>
    public static IResult Jsx(string viewName, object? model = null, RenderMode? renderMode = null) =>
        new JsxViewResult(viewName, model, renderMode);

    /// <summary>Renders a JSX view on the server.</summary>
    public static IResult JsxServerRendered(string viewName, object? model = null) =>
        new JsxViewResult(viewName, model, RenderMode.Server);
}

/// <summary>
/// Extends <see cref="Results.Extensions"/> so minimal API endpoints can write
/// <c>Results.Extensions.Jsx("Home/Index", model)</c>.
/// </summary>
public static class JsxResultExtensions
{
    /// <summary>Renders a JSX view using the configured default render mode.</summary>
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

    /// <summary>Renders a JSX view on the server, like a traditional view engine.</summary>
    public static IResult JsxServerRendered(this IResultExtensions _, string viewName, object? model = null) =>
        new JsxViewResult(viewName, model, RenderMode.Server);

    /// <summary>Renders a JSX view on the server and mounts it again on the client.</summary>
    public static IResult JsxServerAndClient(this IResultExtensions _, string viewName, object? model = null) =>
        new JsxViewResult(viewName, model, RenderMode.ServerAndClient);
}
