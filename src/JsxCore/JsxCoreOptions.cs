using System.Text.Json;
using JsxCore.Interop;
using JsxCore.Rendering;
using JsxCore.TypeScript;

namespace JsxCore;

/// <summary>How strictly TypeScript type errors are treated.</summary>
public enum TypeCheckingMode
{
    /// <summary>Skip type checking entirely and only transpile. The fastest option.</summary>
    Off,

    /// <summary>Report type errors as log warnings (and in the dev error overlay) but keep serving.</summary>
    Warn,

    /// <summary>Treat type errors as fatal: compilation throws and requests fail.</summary>
    Error
}

/// <summary>
/// Which JavaScript framework views are compiled and rendered against.
/// </summary>
/// <remarks>
/// Chosen in the project file with <c>&lt;JsxCoreFramework&gt;</c>, because the build acts on it:
/// it decides which packages are installed and which JSX runtime views compile against, long
/// before any of the application's own code runs.
/// </remarks>
public enum JsFramework
{
    /// <summary>
    /// Preact, which ships inside JsxCore. A full component model: context, error boundaries,
    /// refs and the complete hook set, plus true DOM-preserving hydration, and via preact/compat
    /// most of the React ecosystem. Nothing to install and nothing extra in the publish output.
    /// </summary>
    Preact,

    /// <summary>
    /// React itself, installed from npm. Chosen when a dependency needs the real thing rather than
    /// preact/compat; react and react-dom are added to package.json and restored by the build.
    /// </summary>
    React
}

/// <summary>When JsxCore may install missing npm packages on the developer's behalf.</summary>
public enum DependencyInstallMode
{
    /// <summary>Never run npm. A missing dependency fails startup with instructions.</summary>
    Never,

    /// <summary>Install in the Development environment only. The default.</summary>
    Development,

    /// <summary>Install in any environment. Rarely what you want on a server.</summary>
    Always
}

/// <summary>Configuration for the JsxCore view engine.</summary>
public sealed class JsxCoreOptions
{
    /// <summary>
    /// Directory containing views, relative to the content root (or absolute). Defaults to "Views".
    /// </summary>
    public string ViewsDirectory { get; set; } = "Views";

    /// <summary>
    /// Working directory for compiler output, relative to the content root (or absolute).
    /// </summary>
    /// <remarks>
    /// Defaults to "obj/JsxCore", the standard .NET intermediate output location: it is already
    /// covered by the default .NET .gitignore and is hidden from IDE project trees, so compiled
    /// views do not clutter the consuming project.
    /// </remarks>
    public string WorkingDirectory { get; set; } = Path.Combine("obj", "JsxCore");

    /// <summary>
    /// Base path that compiled modules are served from. Defaults to "/_jsx".
    /// </summary>
    public string RequestPath { get; set; } = "/_jsx";

    /// <summary>File extensions treated as views, in probe order.</summary>
    public IList<string> Extensions { get; } = new List<string> { ".tsx", ".jsx" };

    /// <summary>
    /// Format strings used to locate a view, where <c>{0}</c> is the view name, <c>{1}</c> the
    /// controller name and <c>{2}</c> the area name. Paths are relative to the content root.
    /// </summary>
    public IList<string> ViewLocationFormats { get; } = new List<string>
    {
        "{ViewsDirectory}/{1}/{0}",
        "{ViewsDirectory}/Shared/{0}",
        "{ViewsDirectory}/{0}"
    };

    /// <summary>Area-aware view location formats, where <c>{2}</c> is the area name.</summary>
    public IList<string> AreaViewLocationFormats { get; } = new List<string>
    {
        "Areas/{2}/{ViewsDirectory}/{1}/{0}",
        "Areas/{2}/{ViewsDirectory}/Shared/{0}",
        "{ViewsDirectory}/Shared/{0}"
    };

    /// <summary>Render mode used when a view does not specify one. Defaults to <see cref="RenderMode.Client"/>.</summary>
    public RenderMode DefaultRenderMode { get; set; } = RenderMode.Client;

    /// <summary>
    /// Map the bare specifiers <c>react</c> and <c>react-dom</c> onto <c>preact/compat</c>, so
    /// components written against React work unchanged. Defaults to true and only applies when the
    /// framework is Preact.
    /// </summary>
    public bool EnableReactCompatibility { get; set; } = true;

    /// <summary>How TypeScript type errors are handled. Defaults to <see cref="TypeCheckingMode.Warn"/>.</summary>
    public TypeCheckingMode TypeChecking { get; set; } = TypeCheckingMode.Warn;

    /// <summary>
    /// Explicit path to a TypeScript compiler executable. When null, JsxCore locates the native
    /// <c>tsc</c> binary shipped by the <c>typescript</c> npm package.
    /// </summary>
    public string? TypeScriptCompilerPath { get; set; }

    /// <summary>
    /// Additional directories to search for a <c>node_modules</c> folder, used when locating the
    /// TypeScript compiler and the Preact packages. The content root and its ancestors are always
    /// searched, so this is only needed when the content root sits outside the npm project,
    /// which is common under test hosts that relocate it.
    /// </summary>
    public IList<string> AdditionalToolchainSearchPaths { get; } = new List<string>();

    /// <summary>
    /// Minimum acceptable TypeScript major version. JsxCore relies on the native (Go) compiler and
    /// on <c>rewriteRelativeImportExtensions</c>, both of which require version 7 or later.
    /// </summary>
    public int MinimumTypeScriptMajorVersion { get; set; } = 7;

    /// <summary>
    /// Whether JsxCore installs missing npm packages itself. Defaults to
    /// <see cref="DependencyInstallMode.Development"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a first run this saves having to remember <c>npm install --save-dev typescript@^7</c>,
    /// which is otherwise the most common way to meet JsxCore for the first time and have it fail.
    /// If there is no <c>package.json</c> yet, one is created with <c>npm init -y</c>.
    /// </para>
    /// <para>
    /// It writes to your project (a manifest, a lock file and node_modules), so it is restricted to
    /// Development, skipped entirely when <see cref="PrecompiledOnly"/> is set, and every step is
    /// reported through <see cref="OnBootstrapMessage"/> as it happens. Set this to
    /// <see cref="DependencyInstallMode.Never"/> to keep package management entirely in your hands.
    /// </para>
    /// </remarks>
    public DependencyInstallMode AutoInstallDependencies { get; set; } = DependencyInstallMode.Development;

    /// <summary>Explicit path to npm, used only by <see cref="AutoInstallDependencies"/>.</summary>
    public string? NpmPath { get; set; }

    /// <summary>
    /// Which package manager restores packages. Null picks the first that can run, which is
    /// <c>native</c>.
    /// </summary>
    /// <remarks>
    /// <c>native</c> talks to the registry directly and needs nothing installed. <c>npm</c> runs
    /// the npm on the machine. Naming one insists on it, and JsxCore reports that it cannot restore
    /// rather than quietly using the other.
    /// </remarks>
    public string? PackageManager { get; set; }

    /// <summary>How long a single npm command may run for. Defaults to five minutes.</summary>
    public TimeSpan DependencyInstallTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Receives progress messages while dependencies are installed. Defaults to the console,
    /// because this happens during service registration, before logging is available.
    /// </summary>
    public Action<string>? OnBootstrapMessage { get; set; }

    /// <summary>Compile all views during startup. Defaults to true.</summary>
    public bool CompileOnStartup { get; set; } = true;

    /// <summary>
    /// Write a tsconfig.json beside the views so editors can resolve <c>@jsxcore/runtime</c>.
    /// Defaults to true.
    /// </summary>
    /// <remarks>
    /// Editors pick the nearest tsconfig.json to the file being edited, and the compiler's own
    /// config lives in the intermediate output directory where they will not find it. Without this
    /// file, Rider and VS Code mark every <c>@jsxcore/runtime</c> import as unresolved even though
    /// compilation succeeds. A file that no longer carries JsxCore's marker comment is treated as
    /// hand-written and is never overwritten.
    /// </remarks>
    public bool GenerateEditorTsConfig { get; set; } = true;

    /// <summary>
    /// Serve views that were already compiled, without a TypeScript toolchain.
    /// </summary>
    /// <remarks>
    /// Set this on a published application whose views were compiled at build time by the JsxCore
    /// MSBuild target. Startup then verifies that compiled output exists instead of verifying the
    /// toolchain, so the production machine needs no npm packages. Compilation, watching and hot
    /// reload are all disabled.
    /// </remarks>
    public bool PrecompiledOnly { get; set; }

    /// <summary>
    /// Watch the views directory and recompile on change. Defaults to true in the Development
    /// environment and false elsewhere.
    /// </summary>
    public bool? WatchForChanges { get; set; }

    /// <summary>
    /// Serve the hot-reload client and WebSocket endpoint. Defaults to true in the Development
    /// environment and false elsewhere.
    /// </summary>
    public bool? HotReload { get; set; }

    /// <summary>Extra compiler options merged into the generated tsconfig.json.</summary>
    public IDictionary<string, object> CompilerOptions { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>
    /// Extra import map entries added to the rendered document, for bare module specifiers that
    /// JsxCore does not resolve itself.
    /// </summary>
    public IDictionary<string, string> ImportMap { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Let views import packages from node_modules, on the server and in the browser.
    /// Defaults to true.
    /// </summary>
    /// <remarks>
    /// Resolution follows a package's own <c>exports</c> map, preferring its ES module build, and
    /// CommonJS packages are wrapped so they can be imported as well. Only packages a view actually
    /// reaches are loaded or served, and nothing else in node_modules is reachable.
    /// </remarks>
    public bool AllowNodeModules { get; set; } = true;

    /// <summary>.NET objects exposed to server-rendered views.</summary>
    public JsxGlobalRegistry Globals { get; } = new();

    /// <summary>Values added to the <c>context</c> prop that every view receives.</summary>
    public IDictionary<string, object?> ContextValues { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Options used to serialise the model into the page and into the renderer.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>Customises the HTML document that wraps a view.</summary>
    public DocumentOptions Document { get; } = new();

    /// <summary>Limits applied to the server-side JavaScript engine.</summary>
    public ServerRenderOptions ServerRendering { get; } = new();

    /// <summary>
    /// TypeScript declarations generated from .NET types, so view models do not have to be
    /// described twice.
    /// </summary>
    public TypeDefinitionOptions TypeDefinitions { get; } = new();

    /// <summary>
    /// Which .NET types get TypeScript declarations. Shorthand for
    /// <see cref="TypeDefinitionOptions.AutoExport"/>.
    /// </summary>
    /// <remarks>
    /// Leaving this alone uses the convention: everything in a <c>Models</c> or
    /// <c>ViewModels</c> namespace, plus anything marked <c>[JsxModel]</c>. That is why a
    /// conventional MVC application needs no configuration here at all.
    /// <code>
    /// options.AutoExport = TypesFrom.NamespaceContaining&lt;OrderModel&gt;(includeChildNamespaces: true);
    /// </code>
    /// </remarks>
    public TypeSource? AutoExport
    {
        get => TypeDefinitions.AutoExport;
        set => TypeDefinitions.AutoExport = value;
    }

    /// <summary>
    /// Position at which the JSX view engine is inserted into MVC's view engine list. 0 means JSX
    /// views take precedence over Razor. Defaults to 0.
    /// </summary>
    public int ViewEngineOrder { get; set; }
}

/// <summary>Controls the HTML shell JsxCore generates around a view.</summary>
public sealed class DocumentOptions
{
    /// <summary>Element id of the container the view renders into. Defaults to "jsxcore-root".</summary>
    public string ContainerId { get; set; } = "jsxcore-root";

    public string ModelElementId { get; set; } = "jsxcore-model";

    public string Language { get; set; } = "en";

    /// <summary>Title used when a view does not export one.</summary>
    public string DefaultTitle { get; set; } = "";

    /// <summary>Raw markup appended to the document head, after any view-supplied head content.</summary>
    public string HeadContent { get; set; } = "";

    public string BodyContent { get; set; } = "";

    public IDictionary<string, string> BodyAttributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Replaces the built-in document writer entirely. When set, all other options on this type
    /// are ignored and the supplied delegate is responsible for the whole response body.
    /// </summary>
    public Func<DocumentContext, string>? Template { get; set; }

    /// <summary>
    /// Produces an independent copy, used as the starting point when a single result overrides
    /// document settings for one response.
    /// </summary>
    public DocumentOptions Clone()
    {
        var clone = new DocumentOptions
        {
            ContainerId = ContainerId,
            ModelElementId = ModelElementId,
            Language = Language,
            DefaultTitle = DefaultTitle,
            HeadContent = HeadContent,
            BodyContent = BodyContent,
            Template = Template
        };

        foreach (var (name, value) in BodyAttributes)
        {
            clone.BodyAttributes[name] = value;
        }

        return clone;
    }
}

/// <summary>Limits applied to the embedded JavaScript engine used for server rendering.</summary>
public sealed class ServerRenderOptions
{
    /// <summary>Maximum wall-clock time a single render may take. Defaults to 5 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum recursion depth. Defaults to 256.</summary>
    public int MaxRecursionDepth { get; set; } = 256;

    /// <summary>
    /// Maximum number of pooled JavaScript engines. Engines are not thread-safe, so this bounds
    /// how many server renders can run concurrently before they queue. Defaults to the processor count.
    /// </summary>
    public int MaxPooledEngines { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Expose members of registered globals under their camelCase names in addition to their
    /// exact .NET names. Defaults to true, so both <c>Greeter.Greet()</c> and <c>Greeter.greet()</c> work.
    /// </summary>
    public bool ExposeCamelCaseMembers { get; set; } = true;

}
