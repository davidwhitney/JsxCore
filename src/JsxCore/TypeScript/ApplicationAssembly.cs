using System.Reflection;
using System.Runtime.Loader;

namespace JsxCore.TypeScript;

/// <summary>
/// Loads a built application assembly for inspection, without running any of it.
/// </summary>
/// <remarks>
/// Only metadata is read: the types in the assembly and the shape of their members. The
/// application's entry point is never invoked, so nothing it does at startup, connecting to a
/// database or reading configuration, happens here.
/// </remarks>
public static class ApplicationAssembly
{
    /// <summary>
    /// Loads the assembly at <paramref name="path"/>, or returns null if it cannot be loaded.
    /// </summary>
    /// <remarks>
    /// Dependencies are resolved from the application's own deps file, falling back to the
    /// directory it was built into. Failure is a normal outcome rather than an error: an assembly
    /// built for a platform this process cannot load is a reason to skip generating declarations,
    /// not a reason to fail a build.
    /// </remarks>
    public static Assembly? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var resolver = new AssemblyDependencyResolver(path);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var context = new AssemblyLoadContext("JsxCore.ModelTypes");

            context.Resolving += (loadContext, name) =>
            {
                if (resolver.ResolveAssemblyToPath(name) is { } resolved && File.Exists(resolved))
                {
                    return loadContext.LoadFromAssemblyPath(resolved);
                }

                // Anything the deps file does not describe, such as a reference copied in by a
                // build step, is still worth looking for beside the assembly itself.
                var beside = Path.Combine(directory, name.Name + ".dll");

                return File.Exists(beside) ? loadContext.LoadFromAssemblyPath(beside) : null;
            };

            return context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException
                                       or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
