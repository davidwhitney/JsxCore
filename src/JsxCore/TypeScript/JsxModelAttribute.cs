using System.Reflection;

namespace JsxCore.TypeScript;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum,
    Inherited = false)]
public sealed class JsxModelAttribute : Attribute
{
    public string? Name { get; set; }
}

public sealed class TypeDefinitionOptions
{
    private readonly List<Type> _types = [];

    /// <summary>Generate declarations. Defaults to true; nothing is written when no types are registered.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The scheme views import .NET types through. The name after it is the assembly's, so an
    /// import says which .dll the types came from: <c>import type MyApp from "dotnet:MyApp"</c>.
    /// <para>
    /// Spelled for the platform rather than for the runtime that implements it: "CLR" is precise
    /// and is also jargon a good many .NET developers have never needed.
    /// </para>
    /// </summary>
    public const string Scheme = "dotnet:";

    /// <summary>
    /// Names the scheme keeps for itself, which an assembly therefore cannot use.
    /// </summary>
    /// <remarks>
    /// Matched exactly. <c>Rendering</c> is a perfectly ordinary assembly name and resolves as one;
    /// only the lowercase spellings are taken.
    /// </remarks>
    public static readonly IReadOnlyList<string> ReservedNames = ["globals", "rendering"];

    /// <summary>The module that names every registered .NET global.</summary>
    public const string GlobalsSpecifier = "dotnet:globals";

    /// <summary>The file backing <see cref="GlobalsSpecifier"/>, beside the assembly modules.</summary>
    public const string GlobalsFileName = "globals.d.ts";

    /// <summary>
    /// The .NET objects exposed to views, by the name they were registered under. A null value is
    /// a global whose type is not knowable, which a view sees as <c>any</c>.
    /// </summary>
    /// <remarks>
    /// Populated from the registry at startup. A build cannot see it, because registration happens
    /// in application code the build never runs; until then a view importing one gets <c>any</c>.
    /// </remarks>
    public IReadOnlyDictionary<string, Type?> GlobalTypes { get; set; } =
        new Dictionary<string, Type?>(StringComparer.Ordinal);

    /// <summary>What views import to reach the types generated from an assembly.</summary>
    public static string SpecifierFor(string assemblyName) => Scheme + assemblyName;

    /// <summary>
    /// The assembly the generated module is named after, which is the application's own.
    /// </summary>
    /// <remarks>
    /// Falls back to a fixed name when nothing identifies the assembly, which happens in tests and
    /// in hosts that suppress assembly info. A view still has something to import; it is only the
    /// name in the specifier that is less informative.
    /// </remarks>
    /// <param name="declaring">
    /// The assemblies the generated types actually came from, used when nothing else identifies
    /// the application: types registered with <c>Add&lt;T&gt;()</c> alone still know their own
    /// assembly, and naming the module after it beats naming it after nothing.
    /// </param>
    public string AssemblyName(IEnumerable<Assembly>? declaring = null)
    {
        if (ApplicationAssembly?.GetName().Name is { Length: > 0 } configured)
        {
            return configured;
        }

        var fromTypes = declaring?
            .GroupBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return fromTypes?.Key ?? "Application";
    }

    /// <summary>
    /// Directory the declaration files are written to, relative to the content root (or absolute).
    /// </summary>
    /// <remarks>
    /// Defaults to the intermediate output directory so generated code does not appear in the
    /// project tree. Point this somewhere committed if you want the declarations available on a
    /// fresh clone before the application has been run.
    /// </remarks>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Emit one module per .NET namespace, mirroring the application's own structure.
    /// </summary>
    /// <remarks>
    /// With this on, which is the default, <c>SampleApp.Models.Product</c> is imported from
    /// their .NET namespace, so two types sharing a simple name in different
    /// namespaces stay distinct. Turn it off to put everything in a single flat module, which is
    /// simpler but cannot express that difference.
    /// </remarks>
    public bool MirrorNamespaces { get; set; } = true;

    /// <summary>
    /// Namespace prefix stripped from generated module paths, so that
    /// <c>MyApp.Web.ViewModels</c> can be reached as <c>ViewModels</c> rather
    /// than repeating the application's root namespace in every import.
    /// </summary>
    public string? TrimNamespacePrefix { get; set; }

    /// <summary>Emit enums as unions of string literals rather than numbers.</summary>
    /// <remarks>
    /// Null follows the configured <c>JsonSerializerOptions</c>: string literals when a
    /// <c>JsonStringEnumConverter</c> is registered, numbers otherwise.
    /// </remarks>
    public bool? EnumsAsStrings { get; set; }

    /// <summary>Include public fields as well as properties, matching System.Text.Json's option.</summary>
    public bool IncludeFields { get; set; }

    /// <summary>
    /// Which types are exported. Null, the default, means the built-in convention: everything in
    /// a namespace named in <see cref="ConventionalNamespaceNames"/>, plus anything marked with
    /// <see cref="JsxModelAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Assign a <see cref="TypeSource"/> to take control:
    /// <code>
    /// options.AutoExport = TypesFrom.NamespaceContaining&lt;OrderModel&gt;(includeChildNamespaces: true);
    /// </code>
    /// Use <see cref="TypesFrom.Nothing"/> to export nothing but the types added explicitly.
    /// </remarks>
    public TypeSource? AutoExport { get; set; }

    /// <summary>
    /// Namespace segment names treated as holding view models. Defaults to "Models" and
    /// "ViewModels", which is what makes a conventional MVC application work with no configuration.
    /// </summary>
    public IList<string> ConventionalNamespaceNames { get; } = new List<string> { "Models", "ViewModels" };

    /// <summary>
    /// The application assembly scanned by the default convention. Set automatically from the
    /// hosting environment's application name.
    /// </summary>
    public Assembly? ApplicationAssembly { get; set; }

    /// <summary>Types added explicitly, on top of whatever <see cref="AutoExport"/> yields.</summary>
    public IReadOnlyList<Type> Types => _types;

    /// <summary>
    /// The full set of types to describe: those added explicitly, plus the configured source, or
    /// the built-in convention when no source was configured.
    /// </summary>
    public IReadOnlyList<Type> ResolveTypes()
    {
        var source = AutoExport;

        if (source is null && ApplicationAssembly is not null)
        {
            source = TypesFrom.Conventional(ApplicationAssembly, ConventionalNamespaceNames.ToArray());
        }

        var resolved = source?.Resolve() ?? [];

        return
        [
            .. _types
                .Concat(resolved)
                .Distinct()
                .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
        ];
    }

    public TypeDefinitionOptions Add<T>() => Add(typeof(T));

    public TypeDefinitionOptions Add(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_types.Contains(type))
        {
            _types.Add(type);
        }
        return this;
    }

    public TypeDefinitionOptions Add(params Type[] types)
    {
        foreach (var type in types)
        {
            Add(type);
        }
        return this;
    }

    public TypeDefinitionOptions AddMarkedTypesFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return AddFrom(assembly, type => type.GetCustomAttribute<JsxModelAttribute>() is not null);
    }

    public TypeDefinitionOptions AddFrom(Assembly assembly, Func<Type, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(predicate);

        // A type that fails to load is one JsxCore cannot describe anyway; skipping beats failing
        // startup over an unrelated dependency.
        Type?[] candidates;
        try
        {
            candidates = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            candidates = ex.Types;
        }

        foreach (var type in candidates)
        {
            if (type is not null && type.IsPublic && predicate(type))
            {
                Add(type);
            }
        }

        return this;
    }

    public TypeDefinitionOptions AddNamespace(Assembly assembly, string @namespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        return AddFrom(assembly, type =>
            type.Namespace is not null
            && (type.Namespace == @namespace || type.Namespace.StartsWith(@namespace + ".", StringComparison.Ordinal)));
    }
}
