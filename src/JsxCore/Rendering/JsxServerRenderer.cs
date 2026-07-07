using System.Collections.Concurrent;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using JsxCore.Compilation;
using JsxCore.Compilation.Modules;

namespace JsxCore.Rendering;

/// <summary>
/// Renders views to HTML on the server by executing the compiled modules in an embedded
/// JavaScript engine.
/// </summary>
/// <remarks>
/// <para>
/// Engines are not thread-safe, so they are pooled and rented for the duration of a render. A
/// pooled engine keeps its parsed module graph, which is what makes repeat renders cheap; engines
/// built against a superseded compilation are discarded rather than reused.
/// </para>
/// <para>
/// General CLR access is deliberately never enabled. The only .NET reachable from a view is what
/// was explicitly registered on <see cref="JsxCoreOptions.Globals"/>.
/// </para>
/// </remarks>
public sealed class JsxServerRenderer(
    JsxCoreOptions options,
    JsxCompilationService compilation,
    JsxRuntimeLayout runtime,
    NodeModuleResolver? npm = null)
    : IDisposable
{
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly JsxCompilationService _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly JsxRuntimeLayout _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly NodeModuleResolver? _npm = npm;
    private readonly ConcurrentBag<PooledEngine> _pool = [];
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.ServerRendering.MaxPooledEngines));
    private bool _disposed;

    /// <summary>
    /// Executes a view and returns its markup.
    /// </summary>
    /// <param name="context">Ambient values exposed as the component's <c>context</c> prop.</param>
    /// <param name="services">Scope used to resolve request-scoped globals.</param>
    /// <param name="cancellationToken">Cancels waiting for a free engine.</param>
    public Task<ServerRenderResult> RenderAsync(
        LocatedView view,
        object? model,
        IReadOnlyDictionary<string, object?> context,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(view, model, context, services, "renderView", cancellationToken);

    /// <summary>
    /// Evaluates only a view's <c>head</c> export, without running the component.
    /// </summary>
    /// <remarks>
    /// Client-rendered views never execute on the server, but their head export still has to
    /// populate the document title and meta tags. The module is already parsed and cached in the
    /// pooled engine, so this costs little beyond the first call.
    /// </remarks>
    public Task<ServerRenderResult> ReadHeadAsync(
        LocatedView view,
        object? model,
        IReadOnlyDictionary<string, object?> context,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(view, model, context, services, "readHead", cancellationToken);

    private async Task<ServerRenderResult> ExecuteAsync(
        LocatedView view,
        object? model,
        IReadOnlyDictionary<string, object?> context,
        IServiceProvider services,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (_compilation.ResolveCompiledModule(view) is null)
        {
            throw new JsxRenderException(
                $"The view '{view.ViewName}' has no compiled output at " +
                $"'{Path.Combine(_compilation.Layout.OutputDirectory, view.ModuleRelativePath)}'. " +
                $"Check the compiler diagnostics for errors in this view.",
                new FileNotFoundException(view.ModuleRelativePath));
        }

        var payload = JsonSerializer.Serialize(
            new { model, context },
            _options.JsonSerializerOptions);

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var buildId = _compilation.BuildId;
            var pooled = Rent(buildId);
            try
            {
                return Render(pooled.Engine, view, payload, services, entryPoint);
            }
            finally
            {
                Return(pooled);
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    private ServerRenderResult Render(
        Engine engine,
        LocatedView view,
        string payload,
        IServiceProvider services,
        string entryPoint)
    {
        try
        {
            InstallGlobals(engine, services);

            engine.SetValue("__jsxcore_payload", payload);
            var props = engine.Evaluate("JSON.parse(__jsxcore_payload)").AsObject();
            engine.SetValue("__jsxcore_payload", JsValue.Undefined);

            var server = engine.Modules.Import(_runtime.ServerEntrySpecifier);
            var module = engine.Modules.Import("./" + view.ModuleRelativePath);

            var json = engine.Invoke(server.Get(entryPoint), module, props).AsString();

            return JsonSerializer.Deserialize<ServerRenderResult>(json, SerializerOptions)
                ?? new ServerRenderResult(string.Empty, null);
        }
        catch (JavaScriptException ex)
        {
            throw new JsxRenderException(
                $"JsxCore failed to server-render '{view.ViewName}': {ex.Message}{Environment.NewLine}" +
                $"{ex.JavaScriptStackTrace}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new JsxRenderException($"JsxCore failed to server-render '{view.ViewName}'.", ex);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private void InstallGlobals(Engine engine, IServiceProvider services)
    {
        var registrations = _options.Globals.Registrations;
        if (registrations.Count == 0)
        {
            engine.SetValue("__jsxcore_dotnet", JsValue.Undefined);
            return;
        }

        var globals = engine.Evaluate("({})").AsObject();
        foreach (var (name, registration) in registrations)
        {
            globals.Set(name, JsValue.FromObject(engine, registration.Factory(services)));
        }

        engine.SetValue("__jsxcore_dotnet", globals);
    }

    private PooledEngine Rent(string buildId)
    {
        while (_pool.TryTake(out var candidate))
        {
            if (candidate.BuildId == buildId)
            {
                return candidate;
            }
            // Built against superseded output; let it go.
        }

        return new PooledEngine(CreateEngine(), buildId);
    }

    private void Return(PooledEngine engine)
    {
        if (!_disposed && engine.BuildId == _compilation.BuildId)
        {
            _pool.Add(engine);
        }
    }

    private Engine CreateEngine()
    {
        var loader = new JsxModuleLoader(_compilation.Layout, _runtime, _options.AllowNodeModules ? _npm : null);
        var settings = _options.ServerRendering;

        return new Engine(options =>
        {
            options.EnableModules(loader);
            options.TimeoutInterval(settings.Timeout);
            options.LimitRecursion(settings.MaxRecursionDepth);

            if (settings.ExposeCamelCaseMembers)
            {
                // Expose both the exact .NET name and a camelCase alias, so a C# `Greet` method can
                // be called as either `Greet()` or `greet()` without the caller having to know.
                options.Interop.TypeResolver = new TypeResolver
                {
                    MemberNameCreator = MemberNames
                };
            }
        });
    }

    private static IEnumerable<string> MemberNames(System.Reflection.MemberInfo member)
    {
        var name = member.Name;
        yield return name;

        if (name.Length > 0 && char.IsUpper(name[0]))
        {
            yield return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }

    /// <summary>Discards pooled engines, for example after a recompilation.</summary>
    public void Reset()
    {
        while (_pool.TryTake(out _))
        {
            // Engines hold no unmanaged resources; dropping the reference is enough.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Reset();
        _slots.Dispose();
    }

    private sealed record PooledEngine(Engine Engine, string BuildId);
}
