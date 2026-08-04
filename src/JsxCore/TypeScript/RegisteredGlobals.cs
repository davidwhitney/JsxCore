using System.Reflection;

namespace JsxCore.TypeScript;

/// <summary>
/// The globals an application registers, read off its own assembly.
/// </summary>
/// <remarks>
/// <para>
/// Registration happens in application code, which a build machine never runs, so the build used to
/// have nothing to say about <c>dotnet:globals</c> and wrote an untyped stand-in. That is not just
/// vaguer: under <c>noImplicitAny</c>, a callback over a result of type <c>any</c> is an error, so
/// an application using globals could not build with type checking set to error until someone had
/// run it. It passed on the machine that had and failed on the agent that had not.
/// </para>
/// <para>
/// JsxCore's source generator writes the calls it can read onto the assembly, and this reads them
/// back, so the build describes the same globals the application will expose. Both sides agree that
/// silence means "not known", which leaves the stand-in exactly where it was.
/// </para>
/// </remarks>
public static class RegisteredGlobals
{
    /// <summary>Metadata key holding the registrations, as <c>name=type</c> pairs.</summary>
    public const string MetadataKey = "JsxCoreGlobals";

    /// <summary>Metadata key saying whether that list is all of them.</summary>
    public const string CompleteMetadataKey = "JsxCoreGlobalsComplete";

    /// <summary>
    /// Applies what <paramref name="assembly"/> records to <paramref name="options"/>, and reports
    /// whether it said anything usable.
    /// </summary>
    /// <remarks>
    /// A partial list is refused. Naming three globals when the application exposes four turns the
    /// fourth from <c>any</c> into "has no exported member", which breaks a build that worked.
    /// </remarks>
    public static bool Apply(TypeDefinitionOptions options, Assembly? assembly)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (assembly is null)
        {
            return false;
        }

        string? pairs = null;
        var complete = false;

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(metadata.Key, MetadataKey, StringComparison.Ordinal))
            {
                pairs = metadata.Value;
            }
            else if (string.Equals(metadata.Key, CompleteMetadataKey, StringComparison.Ordinal))
            {
                complete = string.Equals(metadata.Value, "true", StringComparison.Ordinal);
            }
        }

        if (!complete || string.IsNullOrWhiteSpace(pairs))
        {
            return false;
        }

        var globals = new Dictionary<string, Type?>(StringComparer.Ordinal);

        foreach (var entry in pairs!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = entry[..separator];
            var typeName = entry[(separator + 1)..];

            // An empty type is a registration that says nothing about what it produces, which is
            // the factory overload. Recorded so the name exists and is typed as any, which is what
            // the running application does with it too.
            globals[name] = typeName.Length == 0 ? null : assembly.GetType(typeName, throwOnError: false);
        }

        if (globals.Count == 0)
        {
            return false;
        }

        options.GlobalTypes = globals;
        options.GlobalsAreKnown = true;
        return true;
    }
}
