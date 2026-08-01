using Microsoft.Extensions.DependencyInjection;

namespace JsxCore.Interop;

/// <summary>
/// The set of .NET objects exposed to server-rendered views, each importable by name from
/// <c>dotnet:globals</c>.
/// </summary>
public sealed class JsxGlobalRegistry
{
    private readonly Dictionary<string, GlobalRegistration> _globals = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, GlobalRegistration> Registrations => _globals;

    /// <summary>
    /// Exposes a singleton instance to server-rendered views. The same instance is shared by
    /// every render, so it must be thread-safe.
    /// </summary>
    public JsxGlobalRegistry Register(string name, object instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(instance);

        _globals[name] = new GlobalRegistration(name, _ => instance, instance.GetType());
        return this;
    }

    /// <summary>
    /// Exposes an object resolved per render from the request's service scope. Use this for
    /// anything that depends on scoped services such as a DbContext or the current user.
    /// </summary>
    public JsxGlobalRegistry Register(string name, Func<IServiceProvider, object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        // No declared type: a factory returns object, so nothing here says what it produces and
        // the generated declaration falls back to any. Register<TService>() is the typed form.
        _globals[name] = new GlobalRegistration(name, factory, ServiceType: null);
        return this;
    }

    /// <summary>
    /// Exposes the service <typeparamref name="TService"/>, resolved from the request scope.
    /// </summary>
    public JsxGlobalRegistry Register<TService>(string? name = null) where TService : notnull
    {
        var resolvedName = name ?? typeof(TService).Name;

        _globals[resolvedName] = new GlobalRegistration(
            resolvedName, services => services.GetRequiredService<TService>(), typeof(TService));

        return this;
    }

    public bool Remove(string name) => _globals.Remove(name);
}

/// <param name="ServiceType">
/// What the global is, when that is knowable. Used to describe it in TypeScript; null means the
/// registration was a factory, and a view sees the global as <c>any</c>.
/// </param>
public sealed record GlobalRegistration(
    string Name, Func<IServiceProvider, object> Factory, Type? ServiceType = null);
