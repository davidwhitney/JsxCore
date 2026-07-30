using System.Reflection;

namespace JsxCore.Compilation.Provisioning;

/// <summary>
/// Which framework the build compiled an application against, stamped onto its assembly.
/// </summary>
/// <remarks>
/// The project file decides, because the build acts on it before any application code runs, and an
/// attribute is the only channel that travels with the assembly through publishing and precompiled
/// output. Serving the wrong framework's runtime produces empty pages rather than an error.
/// </remarks>
public static class ConfiguredFramework
{
    public const string MetadataKey = "JsxCoreFramework";

    /// <summary>
    /// What <paramref name="assembly"/> was built against, or null when it says nothing.
    /// </summary>
    /// <remarks>
    /// Silence is normal: a project suppressing generated assembly info carries no attribute, and
    /// falling back to the default beats failing startup over it.
    /// </remarks>
    public static JsFramework? Read(Assembly? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(metadata.Key, MetadataKey, StringComparison.Ordinal))
            {
                return Parse(metadata.Value);
            }
        }

        return null;
    }

    public static JsFramework? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "preact" => JsFramework.Preact,
        "react" => JsFramework.React,
        _ => null
    };

    public static string NameOf(JsFramework framework) =>
        framework == JsFramework.React ? "react" : "preact";
}
