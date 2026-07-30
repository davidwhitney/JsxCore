using System.Reflection;

namespace JsxCore.Compilation.Provisioning;

/// <summary>
/// Which framework the build compiled an application against.
/// </summary>
/// <remarks>
/// <para>
/// The choice is made in the project file, because the build acts on it long before any of the
/// application's code runs. The application still has to know: it serves the framework's runtime
/// files and picks the entry points that render a view, and doing that for the wrong one produces
/// pages that are empty rather than broken.
/// </para>
/// <para>
/// So the build stamps it onto the application's own assembly, and this reads it back. An attribute
/// travels wherever the assembly does: through publishing, through precompiled output, and through
/// a content root that has nothing to do with where the project was built.
/// </para>
/// </remarks>
public static class ConfiguredFramework
{
    public const string MetadataKey = "JsxCoreFramework";

    /// <summary>
    /// What <paramref name="assembly"/> was built against, or null when it says nothing.
    /// </summary>
    /// <remarks>
    /// Silence is normal rather than an error: a project that suppresses generated assembly info
    /// carries no attribute, and an application assembly that cannot be resolved at all is not
    /// worth failing startup over. Both fall back to the default.
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
