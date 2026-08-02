using JsxCore.Compilation.Assets;

namespace JsxCore.TypeScript;

/// <summary>
/// Where generated declarations live, and what the compiler is told when they are not there yet.
/// </summary>
/// <remarks>
/// A fresh clone compiles views before anything has generated declarations. Pointing the compiler
/// at a file with no exports fails every named import with TS2305, so it gets an ambient
/// declaration instead: imports resolve as <c>any</c> rather than as errors about types that are
/// going to exist.
/// </remarks>
public static class ModelTypeDeclarations
{
    /// <summary>The generated declaration file, which every .NET type is declared in.</summary>
    public static string FileName => TypeDefinitionOptions.TypesFileName;

    /// <summary>The ambient stand-in. Pruned by <see cref="GeneratedTypeScript.WriteTo"/> once real declarations arrive.</summary>
    public const string PendingFileName = "pending.d.ts";

    /// <summary>
    /// Whether anything has been generated yet. Any declaration file will do: the compiler is
    /// pointed at the directory with a wildcard rather than at one file.
    /// </summary>
    public static bool Exist(string directory) =>
        Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*.d.ts")
            .Any(path => !string.Equals(Path.GetFileName(path), PendingFileName, StringComparison.Ordinal));

    /// <summary>
    /// Writes the ambient stand-in for whatever has not been generated yet, and returns its path.
    /// </summary>
    /// <remarks>
    /// Declares only what is missing. A wildcard covering the assembly modules would otherwise
    /// swallow a mistyped namespace, turning an error a developer wants into a silent <c>any</c>.
    /// </remarks>
    public static string WritePending(string directory, bool models = true, bool globals = true)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, PendingFileName);
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("// Written by JsxCore. These stand in until the declarations are generated;");
        builder.AppendLine("// imports resolve as any until then, rather than failing to resolve.");
        builder.AppendLine("//");
        builder.AppendLine("// Run the application once and they are generated from your .NET types.");
        builder.AppendLine();

        if (models)
        {
            builder.Append("declare module \"").Append(TypeDefinitionOptions.Scheme).AppendLine("*\";");
        }

        if (globals)
        {
            builder.Append("declare module \"").Append(TypeDefinitionOptions.GlobalsSpecifier).AppendLine("\";");
        }

        AssetStage.WriteFileIfChanged(path, builder.ToString());

        return path;
    }
}
