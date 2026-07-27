using System.Reflection;

namespace JsxCore.TypeScript;

public sealed record ModelTypeGenerationResult(int TypeCount, string? Failure)
{
    public bool Generated => Failure is null && TypeCount > 0;
}

/// <summary>
/// Generates TypeScript declarations from an application assembly at build time, so that views
/// importing model types compile on a fresh clone rather than only after the application has run.
/// </summary>
/// <remarks>
/// <para>
/// This runs the same generator the application runs, over types loaded from the built assembly,
/// so the two cannot describe the same model differently.
/// </para>
/// <para>
/// What it cannot see is configuration: <c>AutoExport</c>, a custom naming policy on
/// <c>JsonSerializerOptions</c>, and the rest of <see cref="TypeDefinitionOptions"/> are set in
/// application code that has not run. Defaults are used, which is right for an application that
/// has not customised them and approximate for one that has. Running the application regenerates
/// the declarations from its real configuration, so the run time answer always wins in the end.
/// </para>
/// </remarks>
public static class BuildTimeModelTypes
{
    public static ModelTypeGenerationResult Generate(string assemblyPath, string outputDirectory)
    {
        var assembly = ApplicationAssembly.TryLoad(assemblyPath);

        if (assembly is null)
        {
            return new ModelTypeGenerationResult(0, $"'{Path.GetFileName(assemblyPath)}' could not be loaded");
        }

        return Generate(assembly, outputDirectory);
    }

    public static ModelTypeGenerationResult Generate(Assembly assembly, string outputDirectory)
    {
        var options = new JsxCoreOptions();
        options.TypeDefinitions.ApplicationAssembly = assembly;

        try
        {
            var types = options.TypeDefinitions.ResolveTypes();

            // Writing an empty module would make every named import from it an error. Leaving it
            // absent means the ambient stand-in applies instead, which types them as any.
            if (types.Count == 0)
            {
                return new ModelTypeGenerationResult(0, null);
            }

            new TypeScriptDefinitionGenerator(options.TypeDefinitions, options.JsonSerializerOptions)
                .Generate()
                .WriteTo(outputDirectory);

            return new ModelTypeGenerationResult(types.Count, null);
        }
        catch (Exception ex) when (ex is not JsxCoreException)
        {
            // A model that cannot be described at build time is one the application will describe
            // when it runs. Views fall back to the ambient declarations until then.
            return new ModelTypeGenerationResult(0, ex.Message);
        }
    }
}
