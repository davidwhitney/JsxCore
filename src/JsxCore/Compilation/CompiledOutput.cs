using JsxCore.Compilation.Assets;

namespace JsxCore.Compilation;

/// <summary>
/// Removes compiled modules whose source is gone.
/// </summary>
/// <remarks>
/// <para>
/// The compiler emits into a directory it does not own and never revisits, so deleting or renaming
/// a view leaves its JavaScript behind. That is not merely untidy. Compiled views are carried into
/// the publish output wholesale, so a deleted view keeps being published, and a precompiled
/// application still serves it by name: the view engine looks for a compiled module, and there one
/// is. A view removed from the source tree goes on answering requests.
/// </para>
/// <para>
/// Only files that mirror the views tree are considered. Generated output has no source and is left
/// alone, which is why the manifest and the asset modules are excluded by name rather than by
/// guessing from an extension.
/// </para>
/// </remarks>
public static class CompiledOutput
{
    /// <summary>
    /// Generated output beneath the compiled views, which no source produced. Named from the
    /// constant the generator writes to, so the two cannot drift apart.
    /// </summary>
    private static readonly string[] GeneratedDirectories = [ViewAssets.DistDirectory];

    /// <summary>
    /// Deletes emitted files under <paramref name="layout"/>'s output directory that no source
    /// under its views directory could have produced, and reports how many went.
    /// </summary>
    /// <remarks>
    /// Does nothing when the views directory is absent. That is a published application, where the
    /// sources were never deployed and every compiled module would look orphaned.
    /// </remarks>
    public static int PruneOrphans(CompilationLayout layout, IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(extensions);

        if (!Directory.Exists(layout.OutputDirectory) || !Directory.Exists(layout.ViewsDirectory))
        {
            return 0;
        }

        // ".ts" and ".js" because a view may import a plain module beside it, and the compiler
        // emits those too.
        var sourceExtensions = extensions
            .Concat([".ts", ".js"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(layout.OutputDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(layout.OutputDirectory, file);

            if (IsGenerated(relative) || HasSource(layout, relative, sourceExtensions))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // Something else is holding it. It will be reconsidered on the next build, and a
                // stale module is not worth failing a compilation over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        RemoveEmptyDirectories(layout.OutputDirectory);
        return removed;
    }

    private static bool IsGenerated(string relative)
    {
        // Directly in the output root, so it is the manifest or something like it rather than a
        // compiled view, all of which sit in the directory their source did.
        if (!relative.Contains(Path.DirectorySeparatorChar) && !relative.Contains('/'))
        {
            return true;
        }

        var head = relative.Replace('\\', '/').Split('/')[0];
        return GeneratedDirectories.Contains(head, StringComparer.Ordinal);
    }

    private static bool HasSource(CompilationLayout layout, string relative, string[] sourceExtensions)
    {
        // A map belongs to the module it describes, so it lives or dies with it.
        var stem = relative.EndsWith(".js.map", StringComparison.Ordinal)
            ? relative[..^".map".Length]
            : relative;

        if (!stem.EndsWith(".js", StringComparison.Ordinal))
        {
            // A stylesheet keeps its own name through compilation, so it is its own source name.
            return File.Exists(Path.Combine(layout.ViewsDirectory, stem));
        }

        var withoutExtension = stem[..^".js".Length];

        return sourceExtensions.Any(extension =>
            File.Exists(Path.Combine(layout.ViewsDirectory, withoutExtension + extension)));
    }

    /// <summary>
    /// Removes directories left empty by the deletions, so a deleted folder of views does not
    /// survive as an empty one. The output root itself stays: the compiler expects to find it.
    /// </summary>
    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
