using JsxCore;
using JsxCore.Compilation;
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

    private JsxProjectFixture(string root)
    {
        Root = root;
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
    public static JsxProjectFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-tests", Guid.NewGuid().ToString("n")[..12]);
        Directory.CreateDirectory(root);
        return new JsxProjectFixture(root);
    }

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
        var service = new JsxCompilationService(Options, layout, Toolchain, NullLogger<JsxCompilationService>.Instance);
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

    /// <summary>Creates a server renderer over this fixture's compiled output.</summary>
    public JsxServerRenderer CreateServerRenderer()
    {
        var renderer = new JsxServerRenderer(Options, Compilation, JsxRuntimeLayout.Builtin());
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
