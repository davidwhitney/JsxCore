using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace JsxCore.Hosting;

public static class JsxCoreApplicationBuilderExtensions
{
    /// <summary>
    /// Serves compiled view modules, the JsxCore runtime, and, in development, the hot reload
    /// endpoint. Add this before <c>UseRouting</c> so asset requests short-circuit early.
    /// </summary>
    public static IApplicationBuilder UseJsxCore(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<JsxCoreOptions>()
            ?? throw new InvalidOperationException(
                "UseJsxCore() was called without AddJsxCore(). Register the view engine first, for " +
                "example with builder.AddJsxCore().");

        // Hot reload needs WebSocket support; adding it here keeps setup to a single call. The
        // middleware is a no-op for non-WebSocket requests, so this is safe when already present.
        if (options.HotReload == true)
        {
            app.UseWebSockets();
        }

        return app.UseMiddleware<JsxAssetMiddleware>();
    }
}
