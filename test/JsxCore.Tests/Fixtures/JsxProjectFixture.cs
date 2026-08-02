using JsxCore;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Hosting;
using JsxCore.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using JsxCore.Compilation.Provisioning;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;

namespace JsxCore.Tests.Fixtures;

/// <summary>
/// Builds a throwaway project on disk, compiles it with the real TypeScript toolchain and renders
/// it with the real JavaScript engine. These tests deliberately avoid mocking the compiler: the
/// contract with tsc's emit, extension rewriting in particular, is the thing most worth pinning.
/// </summary>
public sealed class JsxProjectFixture : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    private JsxProjectFixture(string root, JsFramework framework)
    {
        Root = root;
        Framework = framework;
        ViewsDirectory = Path.Combine(root, "Views");
        Directory.CreateDirectory(ViewsDirectory);

        Options = new JsxCoreOptions
        {
            CompileOnStartup = false,
            WatchForChanges = false,
            HotReload = false
        };
    }

    /// <summary>Creates a fixture rooted in a fresh temporary directory.</summary>
    /// <param name="framework">
    /// Which framework to compile and render against, as the project file would choose. React is
    /// resolved from node_modules, so a React fixture also points package resolution at the
    /// repository, where react and react-dom are development dependencies of the test suite.
    /// </param>
    public static JsxProjectFixture Create(JsFramework framework = JsFramework.Preact)
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-tests", Guid.NewGuid().ToString("n")[..12]);
        Directory.CreateDirectory(root);

        var fixture = new JsxProjectFixture(root, framework);

        if (framework == JsFramework.React)
        {
            // Without this "react" resolves to preact/compat, and Preact elements reach React's
            // renderer, which rejects them.
            fixture.Options.EnableReactCompatibility = false;
            fixture.Options.AdditionalToolchainSearchPaths.Add(RepositoryRoot());
        }

        return fixture;
    }

    /// <summary>Which framework this fixture compiles and renders against.</summary>
    public JsFramework Framework { get; }

    /// <summary>Content root of the throwaway project.</summary>
    public string Root { get; }

    /// <summary>Directory views are written to.</summary>
    public string ViewsDirectory { get; }

    /// <summary>Options used for compilation and rendering.</summary>
    public JsxCoreOptions Options { get; }

    /// <summary>The compilation service, created on first use.</summary>
    public JsxCompilationService Compilation => _compilation ??= CreateCompilation();
    private JsxCompilationService? _compilation;

    /// <summary>The directory layout, valid once <see cref="Compilation"/> has been created.</summary>
    public CompilationLayout Layout => Compilation.Layout;

    /// <summary>Locates views by name.</summary>
    public ViewLocator Locator => _locator ??= new ViewLocator(Options, Layout, Root);
    private ViewLocator? _locator;

    /// <summary>
    /// The toolchain the tests run against. Located once and shared, since probing spawns a process.
    /// </summary>
    public static TypeScriptToolchain Toolchain => ToolchainLazy.Value;

    private static readonly Lazy<TypeScriptToolchain> ToolchainLazy = new(() =>
        TypeScriptToolchainLocator.Locate(RepositoryRoot())
        ?? RestoreToolchain()
        ?? throw new InvalidOperationException(
            "The test suite needs the TypeScript toolchain and could not restore it. Check network "
            + "access to the npm registry, or run a restore in the repository root by hand."));

    /// <summary>
    /// Installs what the repository's package.json declares, so a clean checkout can run the suite
    /// with nothing installed but the .NET SDK.
    /// </summary>
    /// <remarks>
    /// Uses JsxCore's own package manager, which is the same code the build and the dotnet tool
    /// use. A test suite for something that claims not to need npm should not need npm to start.
    /// </remarks>
    /// <summary>
    /// Ensures the repository's own packages are installed, for tests that read them directly.
    /// </summary>
    /// <remarks>
    /// Restoring happens on first use of <see cref="Toolchain"/>, which most tests reach through
    /// compiling something. A test that instead inspects node_modules would otherwise pass or fail
    /// on whether it ran before or after one of those.
    /// </remarks>
    public static void EnsureRepositoryPackages() => _ = Toolchain;

    private static TypeScriptToolchain? RestoreToolchain()
    {
        var root = RepositoryRoot();
        var packages = new NativePackageManager(report: _ => { });

        // The lock file is committed, so this is normally an exact restore rather than a resolve.
        if (packages.RestoreFromLockFile(root) == PackageOperationResult.NothingToDo)
        {
            packages.InstallDeclared(root);
        }

        return TypeScriptToolchainLocator.Locate(root);
    }

    /// <summary>Walks up from the test assembly to the repository root, which holds node_modules.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "node_modules"))
                || File.Exists(Path.Combine(directory.FullName, "JsxCore.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private JsxCompilationService CreateCompilation()
    {
        var layout = CompilationLayout.Create(Options, Root);

        // The framework is staged as part of compiling, exactly as it is in a hosted application:
        // server rendering resolves its modules through what the staging step wrote.
        _stager = new PreactVendorStager(
            layout,
            NodeModulesLayout.For(Root, Options.AdditionalToolchainSearchPaths),
            NullLogger<PreactVendorStager>.Instance);

        _reactStager = new ReactEntryStager(layout);

        // Minification is opt-in here as it is in an application: a test asks for it by setting
        // the option, and gets nothing if esbuild is not on the machine.
        var minifier = Options.Minify == true
                       && EsbuildToolchainLocator.Locate(Root, Options.MinifierPath) is { } esbuild
            ? new JsMinifier(esbuild, NullLogger.Instance)
            : null;

        var service = new JsxCompilationService(
            Options,
            layout,
            Toolchain,
            NullLogger<JsxCompilationService>.Instance,
            Framework == JsFramework.React ? null : _stager,
            Framework == JsFramework.React ? _reactStager : null,
            Framework,
            minifier);
        _disposables.Add(service);
        return service;
    }

    /// <summary>Writes a view or component into the views directory.</summary>
    public JsxProjectFixture AddView(string relativePath, string contents)
    {
        var path = Path.Combine(ViewsDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return this;
    }

    /// <summary>Writes a file anywhere in the throwaway project, such as into its wwwroot.</summary>
    public JsxProjectFixture AddFile(string relativePath, string contents)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return this;
    }

    /// <summary>Prepares the working directory and compiles everything.</summary>
    public async Task<BuildState> CompileAsync()
    {
        await Compilation.InitialiseAsync();
        return await Compilation.CompileAsync();
    }

    /// <summary>Finds a view, failing the test if it is missing.</summary>
    public LocatedView Locate(string viewName) =>
        Locator.Find(viewName, controllerName: null, areaName: null, out var searched)
        ?? throw new InvalidOperationException($"View '{viewName}' not found. Searched:{Environment.NewLine}"
                                               + string.Join(Environment.NewLine, searched));

    private PreactVendorStager? _stager;
    private ReactEntryStager? _reactStager;

    /// <summary>
    /// The framework layout for this fixture, staged as the compilation pipeline stages it.
    /// </summary>
    public JsxRuntimeLayout RuntimeLayout
    {
        get
        {
            // Touching Compilation is what creates the stagers, and staging is what fills in the
            // module list the layout reports.
            _ = Compilation;

            if (Framework == JsFramework.React)
            {
                _reactStager!.Stage();
                return JsxRuntimeLayout.React(_reactStager);
            }

            _stager!.Stage();
            return JsxRuntimeLayout.Preact(_stager, Options.EnableReactCompatibility);
        }
    }

    /// <summary>Creates a server renderer over this fixture's compiled output.</summary>
    public JsxServerRenderer CreateServerRenderer()
    {
        // React is resolved out of node_modules rather than staged, so the renderer needs the
        // package resolver to find it at all.
        var renderer = Framework == JsFramework.React
            ? new JsxServerRenderer(Options, Compilation, RuntimeLayout, new NodeModuleResolver(RepositoryRoot()))
            : new JsxServerRenderer(Options, Compilation, RuntimeLayout);
        _disposables.Add(renderer);
        return renderer;
    }

    /// <summary>Renders a view on the server and returns the result.</summary>
    public async Task<ServerRenderResult> RenderAsync(
        string viewName,
        object? model = null,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        var provider = services ?? new ServiceCollection().BuildServiceProvider();
        return await CreateServerRenderer().RenderAsync(
            Locate(viewName),
            model,
            context ?? new Dictionary<string, object?>(),
            provider);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test run is not worth failing over.
        }
    }
}
