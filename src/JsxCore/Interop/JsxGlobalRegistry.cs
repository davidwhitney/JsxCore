using Microsoft.Extensions.DependencyInjection;

namespace JsxCore.Interop;

/// <summary>
/// The set of .NET objects exposed to server-rendered views via
/// <c>import { dotnet } from "@jsxcore/runtime/dotnet"</c>.
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

        _globals[name] = new GlobalRegistration(name, _ => instance);
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

        _globals[name] = new GlobalRegistration(name, factory);
        return this;
    }

    /// <summary>
    /// Exposes the service <typeparamref name="TService"/>, resolved from the request scope.
    /// </summary>
    public JsxGlobalRegistry Register<TService>(string? name = null) where TService : notnull
    {
        var resolvedName = name ?? typeof(TService).Name;
        return Register(resolvedName, services => services.GetRequiredService<TService>());
    }

    public bool Remove(string name) => _globals.Remove(name);
}

public sealed record GlobalRegistration(string Name, Func<IServiceProvider, object> Factory);
