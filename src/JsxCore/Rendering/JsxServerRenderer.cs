using System.Collections.Concurrent;
using System.Text.Json;
using Acornima.Ast;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsonParser = Jint.Native.Json.JsonParser;

namespace JsxCore.Rendering;

/// <summary>
/// Renders views to HTML on the server by executing the compiled modules in an embedded
/// JavaScript engine.
/// </summary>
/// <remarks>
/// <para>
/// Engines are not thread-safe, so they are pooled and rented for the duration of a render. A
/// pooled engine keeps its module graph, which is what makes repeat renders cheap, and the parsed
/// modules behind it are shared by the whole pool, so growing the pool costs no extra parsing.
/// Engines built against a superseded compilation are discarded rather than reused.
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
    private readonly BuildScopedCache<ServerModuleCache> _moduleCache = new();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.ServerRendering.MaxPooledEngines));
    private bool _disposed;

    /// <summary>
    /// Executes a view and returns its markup.
    /// </summary>
    /// <param name="context">Ambient values exposed as the component's <c>context</c> prop.</param>
    /// <param name="services">Scope used to resolve request-scoped globals.</param>
    /// <param name="cancellationToken">
    /// Abandons the render: while it waits for a free engine, and afterwards while its JavaScript
    /// runs. A request whose client has disconnected stops paying for markup nobody will read.
    /// </param>
    public Task<ServerRenderResult> RenderAsync(
        LocatedView view,
        object? model,
        IReadOnlyDictionary<string, object?> context,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(view, Serialize(model), Serialize(context), services, "renderView", cancellationToken);

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
        ExecuteAsync(view, Serialize(model), Serialize(context), services, "readHead", cancellationToken);

    /// <summary>
    /// Executes a view whose model and context have already been serialised.
    /// </summary>
    /// <remarks>
    /// The document around a view carries the same model and context, so a response that writes
    /// both would otherwise serialise each of them twice. The renderer that builds the document
    /// does it once and hands the results here.
    /// </remarks>
    internal Task<ServerRenderResult> RenderSerializedAsync(
        LocatedView view,
        string modelJson,
        string contextJson,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(view, modelJson, contextJson, services, "renderView", cancellationToken);

    /// <summary>
    /// Reads a view's <c>head</c> export from an already serialised model and context.
    /// </summary>
    internal Task<ServerRenderResult> ReadHeadSerializedAsync(
        LocatedView view,
        string modelJson,
        string contextJson,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(view, modelJson, contextJson, services, "readHead", cancellationToken);

    private string Serialize(object? value) =>
        JsonSerializer.Serialize(value, _options.JsonSerializerOptions);

    private async Task<ServerRenderResult> ExecuteAsync(
        LocatedView view,
        string modelJson,
        string contextJson,
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

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var buildId = _compilation.BuildId;
            var pooled = Rent(buildId);
            pooled.Deadline.Begin(_options.ServerRendering.Timeout, cancellationToken);
            try
            {
                return Render(pooled.Engine, view, modelJson, contextJson, services, entryPoint);
            }
            finally
            {
                pooled.Deadline.End();
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
        string modelJson,
        string contextJson,
        IServiceProvider services,
        string entryPoint)
    {
        try
        {
            InstallGlobals(engine, services);

            var parser = new JsonParser(engine);
            var props = new JsObject(engine);
            props.Set("model", parser.Parse(modelJson));
            props.Set("context", parser.Parse(contextJson));

            var server = engine.Modules.Import(_runtime.ServerEntrySpecifier);
            var module = engine.Modules.Import("./" + view.ModuleRelativePath);

            var result = engine.Invoke(server.Get(entryPoint), module, props).AsObject();

            // The markup is taken as the string it already is. Only the head descriptor, which is a
            // handful of tags rather than a whole page, is worth reading back out of JSON.
            var html = result.Get("html").AsString();
            var headValue = result.Get("head");
            var head = headValue.IsString()
                ? JsonSerializer.Deserialize<HeadDescriptor>(headValue.AsString(), SerializerOptions)
                : null;

            return new ServerRenderResult(html, head);
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

    /// <summary>
    /// The global a view reads through <c>isServerRender()</c>.
    /// </summary>
    /// <remarks>
    /// Separate from the globals bridge because they answer different questions. This one is true
    /// for every server render; the bridge holds whatever the application registered, which is
    /// often nothing.
    /// </remarks>
    internal const string ServerFlag = "__jsxcore_server";

    /// <summary>The object the registered .NET globals are installed on, for one render.</summary>
    private const string GlobalsName = "__jsxcore_dotnet";

    private void InstallGlobals(Engine engine, IServiceProvider services)
    {
        var registrations = _options.Globals.Registrations;
        if (registrations.Count == 0)
        {
            engine.SetValue(GlobalsName, JsValue.Undefined);
            return;
        }

        var globals = new JsObject(engine);
        foreach (var (name, registration) in registrations)
        {
            globals.Set(name, JsValue.FromObject(engine, registration.Factory(services)));
        }

        engine.SetValue(GlobalsName, globals);
    }

    private PooledEngine Rent(string buildId)
    {
        while (_pool.TryTake(out var candidate))
        {
            if (candidate.BuildId == buildId)
            {
                return candidate;
            }

            // Built against superseded output.
            Discard(candidate);
        }

        return CreateEngine(buildId);
    }

    private void Return(PooledEngine engine)
    {
        if (_disposed || engine.BuildId != _compilation.BuildId)
        {
            Discard(engine);
            return;
        }

        // The engine goes back with the globals it was built with: the request's .NET bridge, which
        // was resolved from a scope that is about to end, and anything a view left on globalThis are
        // both gone, while the modules it has already parsed stay. Left in place, either would
        // remain for as long as the engine sits in the pool, which for a quiet application is
        // indefinitely.
        try
        {
            engine.Engine.Advanced.RestoreGlobalSnapshot(engine.CleanGlobals);
        }
        catch (InvalidOperationException)
        {
            // An engine whose global surface cannot be restored is not worth pooling.
            Discard(engine);
            return;
        }

        _pool.Add(engine);
    }

    /// <summary>
    /// Lets an engine go. <see cref="Engine"/> is disposable, so this is not the same as dropping
    /// the reference, whatever it happens to hold today.
    /// </summary>
    private static void Discard(PooledEngine engine)
    {
        try
        {
            engine.Engine.Dispose();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // Already gone, which is the outcome this wanted.
        }
    }

    /// <summary>
    /// The host shims, parsed once for the process rather than once per engine.
    /// </summary>
    /// <remarks>
    /// Every engine runs exactly this text, so parsing it again for each of them bought nothing.
    /// The text itself is decoded out of the assembly on each read, which this saves as well.
    /// </remarks>
    private static readonly Lazy<Prepared<Script>> HostShims =
        new(static () => Engine.PrepareScript(RuntimeAssets.HostShims, source: "host-shims.js"));

    /// <summary>
    /// Exposes both the exact .NET name and a camelCase alias, so a C# <c>Greet</c> method can be
    /// called as either <c>Greet()</c> or <c>greet()</c> without the caller having to know.
    /// </summary>
    /// <remarks>
    /// One resolver for all of them: it remembers the members it has already looked up, and every
    /// engine built here is configured the same way, so there is nothing to keep apart.
    /// </remarks>
    private static readonly TypeResolver CamelCaseResolver = new()
    {
        MemberNameCreator = MemberNames
    };

    private PooledEngine CreateEngine(string buildId)
    {
        var loader = new JsxModuleLoader(
            _compilation.Layout,
            _runtime,
            _options.AllowNodeModules ? _npm : null,
            _moduleCache.Get(buildId, static () => new ServerModuleCache()));

        var settings = _options.ServerRendering;
        var deadline = new RenderDeadline();

        var engine = new Engine(options =>
        {
            options.EnableModules(loader);
            options.Constraint(deadline);
            options.LimitRecursion(settings.MaxRecursionDepth);

            if (settings.ExposeCamelCaseMembers)
            {
                options.Interop.TypeResolver = CamelCaseResolver;
            }

            if (settings.ImmutableCrossingTypes.Count > 0)
            {
                options.AddImmutableCrossing([.. settings.ImmutableCrossingTypes]);
            }
        });

        // Before anything is imported: a package that expects a browser or Node global reads it
        // while its own module body runs, so there is no later point at which this would work.
        engine.Execute(HostShims.Value);

        // Which pass is running, stated on its own rather than inferred from whether any .NET
        // objects were registered. An application that registers none is still server rendering,
        // and isServerRender() used to answer that wrongly.
        engine.SetValue(ServerFlag, true);

        // Taken last, so that everything above it is part of what the engine is built with: a render
        // returning the engine to the pool restores this surface, and the shims and the flag have to
        // survive that rather than be swept away with the render's own leavings.
        var cleanGlobals = engine.Advanced.CaptureGlobalSnapshot();

        return new PooledEngine(engine, buildId, cleanGlobals, deadline);
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
        while (_pool.TryTake(out var engine))
        {
            Discard(engine);
        }

        _moduleCache.Clear();
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

    /// <summary>
    /// A pooled engine, together with the global surface it was built with.
    /// </summary>
    /// <param name="CleanGlobals">
    /// The engine's globals as construction left them, restored every time it returns to the pool.
    /// </param>
    /// <param name="Deadline">
    /// The engine's own time budget, armed for the render currently holding it.
    /// </param>
    private sealed record PooledEngine(
        Engine Engine,
        string BuildId,
        GlobalSnapshot CleanGlobals,
        RenderDeadline Deadline);
}
