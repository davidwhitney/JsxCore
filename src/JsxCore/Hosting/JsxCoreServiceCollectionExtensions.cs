using System.Reflection;
using JsxCore.Compilation;
using JsxCore.Mvc;
using JsxCore.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Hosting;

public static class JsxCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the JSX/TSX view engine.
    /// </summary>
    /// <remarks>
    /// Registration only reads, and only locally: it loads the application's own assembly for what
    /// the build stamped onto it, and probes for the node_modules directories to be searched later.
    /// It writes nothing and reaches no network. Packages are restored and the TypeScript toolchain
    /// verified when the host starts, which is still before the first request, so a missing or
    /// unusable toolchain throws <see cref="JsxCoreEnvironmentException"/> and stops the
    /// application rather than surfacing later as a view that will not render.
    /// </remarks>
    /// <param name="environment">The hosting environment, used for the content root and to pick development defaults.</param>
    /// <param name="configure">Optional configuration callback.</param>
    public static IServiceCollection AddJsxCore(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        Action<JsxCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new JsxCoreOptions();
        configure?.Invoke(options);

        // The convention scans the application's own assembly, and it also carries what the build
        // decided, so it is resolved before anything reads either. Taken from the environment
        // rather than Assembly.GetEntryAssembly(), which keeps it correct under test hosts, where
        // the entry assembly is the test runner.
        options.TypeDefinitions.ApplicationAssembly ??= ResolveApplicationAssembly(environment);

        // A Release build compiles the views and publishes them, so the application serves what it
        // produced rather than compiling again on a server that may have no toolchain at all.
        // Nobody has to ask for that; setting the option only overrides it.
        options.PrecompiledOnly = ConfiguredOptimisation.Precompiled(
            options.PrecompiledOnly, options.TypeDefinitions.ApplicationAssembly);

        var precompiled = options.PrecompiledOnly == true;

        // Precompiled applications have no compiler, so nothing can be watched or hot reloaded.
        var isDevelopment = environment.IsDevelopment() && !precompiled;
        options.WatchForChanges ??= isDevelopment;
        options.HotReload ??= isDevelopment;

        if (precompiled)
        {
            options.CompileOnStartup = false;
            options.WatchForChanges = false;
            options.HotReload = false;
        }

        var contentRoot = environment.ContentRootPath;

        // Where a view importing "../wwwroot/logo.svg" is understood to be pointing. Taken from the
        // host rather than assumed, because an application that relocates its web root has already
        // told it where that is, and the import has to mean the directory the static file
        // middleware actually serves.
        if (options.WebRootDirectory == DefaultWebRoot && !string.IsNullOrEmpty(environment.WebRootPath))
        {
            options.WebRootDirectory = environment.WebRootPath;
        }

        // What dotnet:globals describes. Read here rather than during generation because registration
        // is application code, and this is the first point at which all of it has run.
        // Registration has run, so an empty set now means the application has none rather than that
        // nobody has looked yet.
        options.TypeDefinitions.GlobalsAreKnown = true;

        options.TypeDefinitions.GlobalTypes = options.Globals.Registrations.ToDictionary(
            registration => registration.Key,
            registration => registration.Value.ServiceType,
            StringComparer.Ordinal);

        var layout = CompilationLayout.Create(options, contentRoot);
        var nodeModules = NodeModulesLayout.For(contentRoot, options.AdditionalToolchainSearchPaths);

        services.TryAddSingleton(options);
        services.TryAddSingleton(layout);
        services.TryAddSingleton(nodeModules);

        // Restoring packages and verifying the compiler is deferred to the provider, which the
        // startup service runs before anything else. Registration stays free of file system and
        // network work, which is what lets it be logged, cancelled, and awaited.
        services.TryAddSingleton(provider => new JsxToolchainProvider(
            options, environment, contentRoot,
            provider.GetRequiredService<ILogger<JsxToolchainProvider>>()));

        services.TryAddSingleton(provider =>
            provider.GetRequiredService<JsxToolchainProvider>().Value
            ?? throw new JsxCoreEnvironmentException(
                "JsxCore has no TypeScript toolchain, because this application serves precompiled views."));

        services.TryAddSingleton<JsxServerRendererReset>();

        // What the build compiled these views against, read off the application's own assembly.
        // Guessing would mean serving one framework's runtime for another's elements, which renders
        // nothing at all rather than failing.
        var framework = ConfiguredFramework.Read(options.TypeDefinitions.ApplicationAssembly)
                        ?? JsFramework.Preact;

        // React is resolved from node_modules, so its compat aliases must not be in play: with
        // them, a view importing "react" gets preact/compat and hands Preact elements to React's
        // renderer, which rejects them.
        if (framework == JsFramework.React)
        {
            options.EnableReactCompatibility = false;
        }

        // Minification and compression follow the build unless the application overrides them, so
        // that turning either off in the project file is honoured by an application that compiles
        // at startup as well as by one serving what the build produced.
        var assembly = options.TypeDefinitions.ApplicationAssembly;
        var minify = ConfiguredOptimisation.Resolve(
            options.Minify, ConfiguredOptimisation.Minify(assembly), environment.IsDevelopment());

        var compress = ConfiguredOptimisation.Resolve(
            options.CompressAssets, ConfiguredOptimisation.Compress(assembly), environment.IsDevelopment());

        services.TryAddSingleton(new AssetCompressionSettings(compress));
        services.TryAddSingleton(new AssetCompressionCache());

        services.TryAddSingleton(provider => new ReactEntryStager(layout));

        // Preact is staged from the copy JsxCore ships, or from the application's own if it
        // installed one. Either way they are real ES modules with nothing to bundle.
        services.TryAddSingleton(provider => new PreactVendorStager(
            layout,
            nodeModules,
            provider.GetRequiredService<ILogger<PreactVendorStager>>()));

        services.TryAddSingleton(provider => new JsxCompilationService(
            options,
            layout,
            provider.GetRequiredService<JsxToolchainProvider>().Value,
            provider.GetRequiredService<ILogger<JsxCompilationService>>(),
            framework == JsFramework.React ? null : provider.GetRequiredService<PreactVendorStager>(),
            framework == JsFramework.React ? provider.GetRequiredService<ReactEntryStager>() : null,
            framework,
            minify ? LocateMinifier(options, contentRoot, provider) : null,

            // Located whether or not minification is on: the same binary scopes CSS module class
            // names, and a stylesheet has to work in Debug as much as in Release.
            EsbuildToolchainLocator.Locate(
                contentRoot, options.MinifierPath, options.AdditionalToolchainSearchPaths),
            options.AllowNodeModules ? provider.GetRequiredService<NodeModuleResolver>() : null));

        services.TryAddSingleton(provider => framework == JsFramework.React
            ? JsxRuntimeLayout.React(provider.GetRequiredService<ReactEntryStager>())
            : JsxRuntimeLayout.Preact(
                provider.GetRequiredService<PreactVendorStager>(),
                options.EnableReactCompatibility));


        services.TryAddSingleton(provider => new ViewLocator(
            options, layout, contentRoot, provider.GetRequiredService<ILogger<ViewLocator>>()));


        services.TryAddSingleton(provider => new NodeModuleResolver(provider.GetRequiredService<NodeModulesLayout>()));

        // Only registered when packages are allowed, so that switching them off removes the npm
        // asset route and the import map entries along with the server-side resolution.
        if (options.AllowNodeModules)
        {
            services.TryAddSingleton(provider =>
            {
                // Packages are the bulk of what a browser downloads, so they are the bytes
                // minification is really for: the views are usually a rounding error beside them.
                var minifier = minify ? LocateMinifier(options, contentRoot, provider) : null;

                return new NpmClientGraph(
                    provider.GetRequiredService<NodeModuleResolver>(),
                    minifier is null
                        ? null
                        : sources => minifier.MinifySources(
                            sources, Path.Combine(layout.WorkingDirectory, "min", "npm")));
            });
        }

        services.TryAddSingleton(provider =>
        {
            var renderer = new JsxServerRenderer(
                options,
                provider.GetRequiredService<JsxCompilationService>(),
                provider.GetRequiredService<JsxRuntimeLayout>(),
                provider.GetRequiredService<NodeModuleResolver>());
            provider.GetRequiredService<JsxServerRendererReset>().Bind(renderer.Reset);
            return renderer;
        });

        services.TryAddSingleton(provider => new JsxHotReloadService(
            options.HotReload ?? false,
            provider.GetRequiredService<JsxCompilationService>(),
            provider.GetRequiredService<JsxServerRendererReset>(),
            provider.GetRequiredService<ILogger<JsxHotReloadService>>()));

        services.TryAddSingleton<IJsxHotReloadState>(provider => provider.GetRequiredService<JsxHotReloadService>());

        services.TryAddSingleton<JsxViewRenderer>();
        services.TryAddSingleton<JsxViewEngine>();

        // Make `return View()` resolve .tsx/.jsx views. Harmless when MVC is not in use.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<MvcViewOptions>, JsxMvcViewOptionsSetup>());

        services.AddHostedService<JsxCoreStartupService>();

        return services;
    }

    /// <summary>
    /// The minifier, or null with a warning when esbuild is not there.
    /// </summary>
    /// <remarks>
    /// Resolved once and shared, because probing runs the binary. Not finding it is not fatal: the
    /// application serves what it would have served anyway, only larger, and the warning names the
    /// command that fixes it.
    /// </remarks>
    private static JsMinifier? LocateMinifier(
        JsxCoreOptions options, string contentRoot, IServiceProvider provider)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("JsxCore");

        var toolchain = EsbuildToolchainLocator.Locate(
            contentRoot, options.MinifierPath, options.AdditionalToolchainSearchPaths);

        if (toolchain is not null)
        {
            return new JsMinifier(toolchain, logger);
        }

        // A precompiled application is published output: the build minified the views and the
        // publish step minified the packages, so there is nothing left for a runtime minifier to
        // do and esbuild is deliberately absent. Warning there would be noise about a job already
        // done.
        if (options.PrecompiledOnly == true)
        {
            logger.LogDebug("JsxCore: esbuild is not present; assets were minified by the build.");
            return null;
        }

        logger.LogWarning(
            "JsxCore is configured to minify assets but esbuild was not found, so they are served " +
            "unminified. Build the project to install it, or run: dotnet npm add esbuild --dev");

        return null;
    }

    /// <summary>What <see cref="JsxCoreOptions.WebRootDirectory"/> says before anyone sets it.</summary>
    private static readonly string DefaultWebRoot = new JsxCoreOptions().WebRootDirectory;

    private static Assembly? ResolveApplicationAssembly(IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(environment.ApplicationName))
        {
            return null;
        }

        try
        {
            return Assembly.Load(new AssemblyName(environment.ApplicationName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // Nothing to scan by convention; explicit registration still works.
            return null;
        }
    }

    /// <summary>
    /// Registers the JSX/TSX view engine, taking the hosting environment from the builder.
    /// </summary>
    public static WebApplicationBuilder AddJsxCore(
        this WebApplicationBuilder builder,
        Action<JsxCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddJsxCore(builder.Environment, configure);
        return builder;
    }
}

/// <remarks>
/// Everything is resolved inside <see cref="StartAsync"/> rather than injected. The host builds
/// every hosted service before it starts any of them, and constructing the compilation service is
/// what triggers provisioning, so injecting it would put a package restore back before the first
/// line of startup ran.
/// </remarks>
internal sealed class JsxCoreStartupService(
    IServiceProvider services,
    JsxToolchainProvider toolchains,
    JsxCoreOptions options)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // First, and on its own, so that restoring packages and verifying the compiler are the
        // things that fail when they fail, with a logger to say so.
        await toolchains.EnsureAsync(cancellationToken).ConfigureAwait(false);

        var compilation = services.GetRequiredService<JsxCompilationService>();
        var hotReload = services.GetRequiredService<JsxHotReloadService>();
        var locator = services.GetRequiredService<ViewLocator>();

        // A rebuild is when a view name can start or stop resolving, so it is when the locator's
        // answers stop being true.
        compilation.BuildCompleted += _ => locator.Invalidate();

        await compilation.InitialiseAsync(cancellationToken).ConfigureAwait(false);

        if (options.HotReload == true)
        {
            hotReload.Start();
        }

        if (options.WatchForChanges == true)
        {
            compilation.StartWatching();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
