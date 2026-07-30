using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace JsxCore.Compilation.Provisioning;

/// <summary>
/// What runtime an application's own code asks for, read from the assembly the build produced.
/// </summary>
/// <remarks>
/// <para>
/// The build learns the runtime from <c>JsxCoreRuntime</c>, and the application sets it again in
/// code. When the two disagree the build installs and compiles against one runtime while the
/// application renders with the other, and the only sign is a startup failure. In development that
/// hides: startup compiles views itself, with the right runtime, and everything works. It is a
/// build server serving precompiled output that finds out, which is the worst place to.
/// </para>
/// <para>
/// Only metadata is read, never executed and never loaded, so an assembly built for another
/// platform or against a framework this process does not have is still readable.
/// </para>
/// </remarks>
public static class ConfiguredRuntime
{
    private const string PreactMethod = "UsePreact";
    private const string OptionsType = "JsxCoreOptions";

    /// <summary>
    /// Whether the assembly calls <c>options.UsePreact()</c> anywhere.
    /// </summary>
    /// <remarks>
    /// A reference to the method is enough. Whether the call runs is a question about configuration
    /// this cannot answer, and an application carrying the call but not making it is better served
    /// by having Preact installed than by a build that ignores it.
    /// </remarks>
    public static bool CallsUsePreact(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var reader = new PEReader(stream);

            if (!reader.HasMetadata)
            {
                return false;
            }

            var metadata = reader.GetMetadataReader();

            foreach (var handle in metadata.MemberReferences)
            {
                var member = metadata.GetMemberReference(handle);

                if (!metadata.GetString(member.Name).Equals(PreactMethod, StringComparison.Ordinal))
                {
                    continue;
                }

                if (member.Parent.Kind == HandleKind.TypeReference
                    && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name)
                        .Equals(OptionsType, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
        {
            // Unreadable is not the same as "uses Preact"; the build carries on as configured.
            return false;
        }
    }
}
