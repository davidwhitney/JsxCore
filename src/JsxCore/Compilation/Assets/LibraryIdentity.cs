using System.Reflection;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// Identifies the build of JsxCore that produced a set of assets.
/// </summary>
/// <remarks>
/// Asset URLs are cached for a year, so a URL has to change whenever its content could have. The
/// build id is otherwise assembled from the inputs — views, packages, embedded runtime files — and
/// says nothing about the code that transformed them. Upgrading JsxCore can therefore change what a
/// package is wrapped into while leaving its URL identical, and browsers keep serving the old one.
/// The module version id changes whenever this assembly is rebuilt, and only then.
/// </remarks>
public static class LibraryIdentity
{
    public static string Value { get; } =
        typeof(LibraryIdentity).Assembly.ManifestModule.ModuleVersionId.ToString("n")[..12];

    /// <summary>The informational version, for logs and diagnostics rather than for cache keys.</summary>
    public static string Version { get; } =
        typeof(LibraryIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
}
