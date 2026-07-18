using System.Reflection;
using System.Runtime.CompilerServices;

namespace JsxCore.TypeScript;

/// <summary>
/// A declarative description of which .NET types get TypeScript declarations.
/// </summary>
/// <remarks>
/// Sources are lazy and composable: <c>a + b</c> unions them, and <see cref="Where"/> and
/// <see cref="Except(Type[])"/> narrow them. Nothing is enumerated until the generator asks, so a
/// source can be configured before the assemblies it names are fully loaded.
/// </remarks>
public sealed class TypeSource
{
    private readonly Func<IEnumerable<Type>> _resolve;

    internal TypeSource(Func<IEnumerable<Type>> resolve) => _resolve = resolve;

    public IReadOnlyList<Type> Resolve() =>
        _resolve()
            .Where(type => type is not null)
            .Distinct()
            .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToList();

    public TypeSource Where(Func<Type, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new TypeSource(() => _resolve().Where(predicate));
    }

    public TypeSource Except(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return Where(type => !types.Contains(type));
    }

    public TypeSource Except<T>() => Except(typeof(T));

    public static TypeSource operator +(TypeSource left, TypeSource right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new TypeSource(() => left._resolve().Concat(right._resolve()));
    }

    public static TypeSource operator |(TypeSource left, TypeSource right) => left + right;

    public static TypeSource Union(TypeSource left, TypeSource right) => left + right;
}

/// <summary>
/// Builds <see cref="TypeSource"/> values for <c>options.AutoExport</c>.
/// </summary>
/// <remarks>
/// <example>
/// <code>
/// options.AutoExport = TypesFrom.NamespaceContaining&lt;OrderModel&gt;()
///                    + TypesFrom.NamespaceContaining&lt;AccountModel&gt;(includeChildNamespaces: false);
/// </code>
/// </example>
/// </remarks>
public static class TypesFrom
{
    /// <summary>A source that yields nothing, useful as a starting point for conditional composition.</summary>
    public static TypeSource Nothing { get; } = new(Array.Empty<Type>);

    /// <summary>
    /// Every model type in the namespace containing <typeparamref name="TMarker"/>, scanned from
    /// that type's assembly.
    /// </summary>
    /// <param name="includeChildNamespaces">
    /// Include nested namespaces too. Defaults to true, so
    /// <c>NamespaceContaining&lt;OrderModel&gt;()</c> on <c>MyApp.Models</c> also picks up
    /// <c>MyApp.Models.Catalogue</c>.
    /// </param>
    public static TypeSource NamespaceContaining<TMarker>(bool includeChildNamespaces = true) =>
        NamespaceContaining(typeof(TMarker), includeChildNamespaces);

    public static TypeSource NamespaceContaining(Type marker, bool includeChildNamespaces = true)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return Namespace(
            marker.Namespace ?? string.Empty,
            marker.Assembly,
            includeChildNamespaces);
    }

    public static TypeSource Namespace(string @namespace, Assembly assembly, bool includeChildNamespaces = true)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(assembly);

        return new TypeSource(() => ModelTypesIn(assembly).Where(type =>
        {
            var candidate = type.Namespace ?? string.Empty;

            return candidate == @namespace
                   || (includeChildNamespaces
                       && @namespace.Length > 0
                       && candidate.StartsWith(@namespace + ".", StringComparison.Ordinal));
        }));
    }

    public static TypeSource AssemblyContaining<TMarker>() => InAssembly(typeof(TMarker).Assembly);

    public static TypeSource InAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new TypeSource(() => ModelTypesIn(assembly));
    }

    public static TypeSource Matching(Assembly assembly, Func<Type, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(predicate);
        return new TypeSource(() => LoadTypes(assembly).Where(predicate));
    }

    /// <summary>
    /// Types in namespaces named after one of <paramref name="namespaceNames"/>, or nested inside
    /// one, so "Models" picks up <c>MyApp.Models</c> and <c>MyApp.Models.Catalogue</c>, but not
    /// <c>MyApp.ModelBinding</c>.
    /// </summary>
    public static TypeSource ConventionalNamespaces(Assembly assembly, params string[] namespaceNames)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(namespaceNames);

        var names = namespaceNames.ToHashSet(StringComparer.Ordinal);

        return new TypeSource(() => ModelTypesIn(assembly).Where(type =>
            (type.Namespace ?? string.Empty)
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Any(names.Contains)));
    }

    /// <summary>
    /// What JsxCore exports when nothing has been configured: everything in a conventional models
    /// namespace, plus anything marked with <see cref="JsxModelAttribute"/> wherever it lives.
    /// </summary>
    /// <remarks>
    /// The convention is the point: an MVC application that keeps its view models in a
    /// <c>Models</c> namespace never has to configure this at all. The attribute exists for the
    /// types that sit somewhere else.
    /// </remarks>
    public static TypeSource Conventional(Assembly assembly, params string[] namespaceNames) =>
        ConventionalNamespaces(assembly, namespaceNames) + MarkedTypes(assembly);

    public static TypeSource MarkedTypes(Assembly assembly) =>
        Matching(assembly, type => type.GetCustomAttribute<JsxModelAttribute>() is not null);

    public static TypeSource MarkedTypesIn<TMarker>() => MarkedTypes(typeof(TMarker).Assembly);

    /// <summary>
    /// Every model type in every assembly that looks like the application's own code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blunt option: no namespaces to name, no attributes to add. Useful when models are
    /// scattered, or for a small application where generating everything is simply easier than
    /// deciding.
    /// </para>
    /// <para>
    /// "User code" is a heuristic: loaded assemblies that are neither dynamic, nor part of the
    /// shared framework, nor published by Microsoft or JsxCore itself. It will generate more than
    /// you strictly need on a large solution. Narrow it with <see cref="TypeSource.Where"/> or
    /// <see cref="TypeSource.Except(Type[])"/> if that becomes a problem.
    /// </para>
    /// </remarks>
    public static TypeSource AllUserCode { get; } =
        new(() => UserAssemblies().SelectMany(ModelTypesIn));

    public static TypeSource UserCode(Func<Assembly, bool> includeAssembly)
    {
        ArgumentNullException.ThrowIfNull(includeAssembly);
        return new TypeSource(() => UserAssemblies().Where(includeAssembly).SelectMany(ModelTypesIn));
    }

    /// <summary>Assembly name prefixes treated as framework or infrastructure rather than user code.</summary>
    private static readonly string[] NonUserPrefixes =
    [
        "System", "Microsoft", "netstandard", "mscorlib", "WindowsBase", "PresentationCore",
        "Jint", "Acornima", "Newtonsoft", "xunit", "Shouldly", "coverlet", "testhost", "NuGet"
    ];

    private static IEnumerable<Assembly> UserAssemblies()
    {
        var frameworkDirectory = SafeDirectory(typeof(object).Assembly);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            // Excluded by identity rather than by name: an application assembly is perfectly
            // entitled to be called JsxCore.Something.
            if (assembly == typeof(TypeSource).Assembly)
            {
                continue;
            }

            var name = assembly.GetName().Name ?? string.Empty;
            if (NonUserPrefixes.Any(prefix =>
                    name.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Anything sitting alongside the runtime is part of it, whatever it is called.
            if (frameworkDirectory is not null
                && SafeDirectory(assembly) is { } directory
                && string.Equals(directory, frameworkDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return assembly;
        }
    }

    private static string? SafeDirectory(Assembly assembly)
    {
        try
        {
            return string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(assembly.Location);
        }
        catch (NotSupportedException)
        {
            // Single-file or in-memory assemblies have no location.
            return null;
        }
    }

    public static TypeSource Types(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var snapshot = types.ToArray();
        return new TypeSource(() => snapshot);
    }

    public static TypeSource Type<T>() => Types(typeof(T));

    /// <summary>
    /// Public types in an assembly that plausibly describe data.
    /// </summary>
    /// <remarks>
    /// Scanning a namespace wholesale would otherwise sweep up delegates, attributes, exceptions
    /// and static helper classes, none of which can be described as a view model, and each of
    /// which would produce a confusing empty interface.
    /// </remarks>
    private static IEnumerable<Type> ModelTypesIn(Assembly assembly) =>
        LoadTypes(assembly).Where(IsModelLike);

    internal static bool IsModelLike(Type type)
    {
        if (type is { IsPublic: false, IsNestedPublic: false })
        {
            return false;
        }

        if (type.IsGenericTypeDefinition || type.IsPointer || type.IsByRef || type.IsArray)
        {
            return false;
        }

        if (type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
        {
            return false;
        }

        if (typeof(Delegate).IsAssignableFrom(type)
            || typeof(Attribute).IsAssignableFrom(type)
            || typeof(Exception).IsAssignableFrom(type))
        {
            return false;
        }

        // Static classes carry no data.
        if (type is { IsAbstract: true, IsSealed: true })
        {
            return false;
        }

        return type.IsEnum || type.IsClass || type.IsInterface || type is { IsValueType: true, IsPrimitive: false };
    }

    private static IEnumerable<Type> LoadTypes(Assembly assembly)
    {
        // A type that fails to load is one JsxCore could not describe anyway; skipping beats
        // failing startup over an unrelated dependency.
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
