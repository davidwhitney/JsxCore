using System.Collections.Concurrent;
using System.Text.Json;
using Acornima.Ast;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Interop;
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

            // The scope the engine's globals bridge resolves from, for as long as this render holds
            // the engine. Cleared afterwards so an engine waiting in the pool is not holding on to
            // a request whose scope has ended.
            pooled.Globals.Services = services;
            pooled.Deadline.Begin(_options.ServerRendering.Timeout, cancellationToken);
            try
            {
                return Render(pooled.Engine, view, modelJson, contextJson, entryPoint);
            }
            finally
            {
                pooled.Deadline.End();
                pooled.Globals.Services = null;
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
        string entryPoint)
    {
        try
        {
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
        catch (TimeoutException ex)
        {
            // The engine reports an elapsed budget in its own words, which do not say whose budget
            // it was. Which one ran out is the part a host can act on, so the render says it.
            throw new JsxRenderException(
                $"JsxCore failed to server-render '{view.ViewName}'.",
                new TimeoutException("The render exceeded the configured server rendering timeout.", ex));
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

    /// <summary>
    /// The .NET side of the globals bridge for one pooled engine: what the application registered,
    /// and which request's scope the render currently holding the engine resolves it from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registered objects come from the request's service scope, so one of them can be a
    /// database context, a localizer, or anything else a scope builds on demand — and most views
    /// read none of them. Building the bridge is therefore left until a view asks for it: the
    /// engine holds the global as a factory, and a render that never mentions <c>dotnet:globals</c>
    /// resolves no service and builds no wrapper. Importing one is not asking for it either, since
    /// the JavaScript side hands out proxies that reach this only when a member is read.
    /// </para>
    /// <para>
    /// Read at the moment a view asks, rather than captured per render, so a global registered
    /// after startup is still reachable — which is what the catch-all export exists for.
    /// </para>
    /// </remarks>
    private sealed class GlobalsBridge(JsxGlobalRegistry globals)
    {
        /// <summary>The scope of the render holding this engine, or null between renders.</summary>
        public IServiceProvider? Services { get; set; }

        public JsValue Build(Engine engine)
        {
            var registrations = globals.Registrations;
            var services = Services;

            if (services is null || registrations.Count == 0)
            {
                // What an application that registered nothing has always presented, and what the
                // runtime reads as "there is nothing here to reach" when a view asks anyway.
                return JsValue.Undefined;
            }

            var bridge = new JsObject(engine);
            foreach (var (name, registration) in registrations)
            {
                bridge.Set(name, JsValue.FromObject(engine, registration.Factory(services)));
            }

            return bridge;
        }
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
        // Watching means the views can be recompiled while the application runs, and every rebuild
        // throws this compilation's parsed modules away for a new set that one or two engines will
        // read before the next edit does the same. Preparing those the cheaper way is worth more
        // than the bookkeeping the fuller preparation would hand each engine. A server that will
        // not recompile keeps its parses for the life of the pool, which is where the fuller
        // preparation earns back what it costs.
        var staticAnalysis = _options.WatchForChanges != true;

        var loader = new JsxModuleLoader(
            _compilation.Layout,
            _runtime,
            _options.AllowNodeModules ? _npm : null,
            _moduleCache.Get(buildId, () => new ServerModuleCache(staticAnalysis)));

        var settings = _options.ServerRendering;

        // One budget for the whole render rather than one per entry into the engine. The engine
        // resets its constraints at every entry from the host, and a render enters several times,
        // so a plain timeout would give each entry the configured budget over again; this one is
        // armed by the host and declines that reset.
        var deadline = new OperationDeadlineConstraint();

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

        // Declared once for the engine, and before the snapshot below, which is what makes it a
        // per-render bridge without a per-render write: the engine resolves the factory the first
        // time a view reads the global and not at all otherwise, and returning the engine to the
        // pool puts the property back to unresolved, so the next render resolves against the next
        // request's scope. Installed after the snapshot instead, it would be a global the restore
        // has to remove and the next render has to add again.
        var globals = new GlobalsBridge(_options.Globals);
        engine.Advanced.AddLazyGlobal(GlobalsName, globals, static (engine, globals) => globals.Build(engine));

        // Taken last, so that everything above it is part of what the engine is built with: a render
        // returning the engine to the pool restores this surface, and the shims and the flag have to
        // survive that rather than be swept away with the render's own leavings.
        var cleanGlobals = engine.Advanced.CaptureGlobalSnapshot();

        return new PooledEngine(engine, buildId, cleanGlobals, deadline, globals);
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
    /// <param name="Globals">
    /// The engine's bridge to the registered .NET objects, pointed at the scope of the render
    /// currently holding it.
    /// </param>
    private sealed record PooledEngine(
        Engine Engine,
        string BuildId,
        GlobalSnapshot CleanGlobals,
        OperationDeadlineConstraint Deadline,
        GlobalsBridge Globals);
}
