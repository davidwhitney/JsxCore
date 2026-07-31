using System.Reflection;

namespace JsxCore.Compilation.Provisioning;

/// <summary>
/// Whether the build asked for minified and compressed assets, stamped onto the assembly.
/// </summary>
/// <remarks>
/// The same channel the framework choice travels on, for the same reason: the project file decides,
/// the build acts on it long before any application code runs, and an attribute is the only thing
/// that survives publishing into an application started from prebuilt output. Without it,
/// <c>&lt;JsxCoreMinify&gt;false&lt;/JsxCoreMinify&gt;</c> would turn off build-time minification
/// and be silently ignored by an application that compiles at startup.
/// </remarks>
public static class ConfiguredOptimisation
{
    public const string MinifyKey = "JsxCoreMinify";
    public const string CompressKey = "JsxCoreCompressAssets";

    /// <summary>What the build said about minification, or null when it said nothing.</summary>
    public static bool? Minify(Assembly? assembly) => Read(assembly, MinifyKey);

    /// <summary>What the build said about compression, or null when it said nothing.</summary>
    public static bool? Compress(Assembly? assembly) => Read(assembly, CompressKey);

    /// <summary>
    /// Resolves a setting: what was asked for explicitly, then what the build stamped, then the
    /// environment, where anything that is not Development is treated as production.
    /// </summary>
    public static bool Resolve(bool? configured, bool? fromBuild, bool isDevelopment) =>
        configured ?? fromBuild ?? !isDevelopment;

    private static bool? Read(Assembly? assembly, string key)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(metadata.Key, key, StringComparison.Ordinal)
                && bool.TryParse(metadata.Value?.Trim(), out var value))
            {
                return value;
            }
        }

        return null;
    }
}
