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
    /// The environment is verified here, synchronously, and a missing or unusable TypeScript
    /// toolchain throws <see cref="JsxCoreEnvironmentException"/> immediately. Failing at
    /// registration is deliberate: the alternative is an application that starts cleanly and then
    /// fails on the first request for a view, with far less context about what is wrong.
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
        // decided, so it is resolved before anything reads either.
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

        // Already resolved above; kept for callers that construct options themselves.
        // The convention scans the application's own assembly. Resolving it from the environment
        // rather than Assembly.GetEntryAssembly() keeps it correct under test hosts, where the
        // entry assembly is the test runner.
        options.TypeDefinitions.ApplicationAssembly ??= ResolveApplicationAssembly(environment);

        // What dotnet:globals describes. Read here rather than during generation because registration
        // is application code, and this is the first point at which all of it has run.
        options.TypeDefinitions.GlobalTypes = options.Globals.Registrations.ToDictionary(
            registration => registration.Key,
            registration => registration.Value.ServiceType,
            StringComparer.Ordinal);

        // Install anything missing before checking, so a first run does not fail on a package the
        // developer had no way to know they needed.
        var bootstrapFailure = TryInstallDependencies(options, environment, contentRoot);

        // Fail fast, with a message that names the missing dependency and how to install it.
        var toolchain = EnvironmentVerifier.Verify(options, contentRoot, bootstrapFailure);
        var layout = CompilationLayout.Create(options, contentRoot);
        var nodeModules = NodeModulesLayout.For(contentRoot, options.AdditionalToolchainSearchPaths);

        services.TryAddSingleton(options);
        services.TryAddSingleton(layout);
        services.TryAddSingleton(nodeModules);
        if (toolchain is not null)
        {
            services.TryAddSingleton(toolchain);
        }

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
            toolchain,
            provider.GetRequiredService<ILogger<JsxCompilationService>>(),
            framework == JsFramework.React ? null : provider.GetRequiredService<PreactVendorStager>(),
            framework == JsFramework.React ? provider.GetRequiredService<ReactEntryStager>() : null,
            framework,
            minify ? LocateMinifier(options, contentRoot, provider) : null));

        services.TryAddSingleton(provider => framework == JsFramework.React
            ? JsxRuntimeLayout.React(provider.GetRequiredService<ReactEntryStager>())
            : JsxRuntimeLayout.Preact(
                provider.GetRequiredService<PreactVendorStager>(),
                options.EnableReactCompatibility));


        services.TryAddSingleton(provider => new ViewLocator(options, layout, contentRoot));


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
    /// Runs the npm bootstrapper if configuration allows it, returning why it failed or null.
    /// </summary>
    /// <remarks>
    /// Never fatal by itself: whatever happens here, the verifier runs next and produces an
    /// actionable message. Skipped entirely for precompiled applications, which have no toolchain
    /// to install and no business writing to the file system on a server.
    /// </remarks>
    private static string? TryInstallDependencies(
        JsxCoreOptions options,
        IWebHostEnvironment environment,
        string contentRoot)
    {
        var allowed = options.AutoInstallDependencies switch
        {
            DependencyInstallMode.Always => true,
            DependencyInstallMode.Development => environment.IsDevelopment(),
            _ => false
        };

        if (!allowed || options.PrecompiledOnly == true)
        {
            return null;
        }

        // Registration happens before logging is configured, so progress goes to the console.
        var report = options.OnBootstrapMessage ?? Console.WriteLine;

        try
        {
            var bootstrapper = new NpmBootstrapper(report, options.DependencyInstallTimeout, options.NpmPath);
            return bootstrapper.EnsureDependencies(options, contentRoot).Failure;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
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

internal sealed class JsxCoreStartupService(
    JsxCompilationService compilation,
    JsxHotReloadService hotReload,
    ViewLocator locator,
    JsxCoreOptions options)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
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
