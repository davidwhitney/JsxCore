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
    public const string FileName = "index.d.ts";

    /// <summary>The ambient stand-in. Pruned by <see cref="GeneratedTypeScript.WriteTo"/> once real declarations arrive.</summary>
    public const string PendingFileName = "pending.d.ts";

    public static bool Exist(string directory) => File.Exists(Path.Combine(directory, FileName));

    /// <summary>Writes the ambient declaration and returns its path.</summary>
    public static string WritePending(string directory, string moduleSpecifier)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, PendingFileName);

        // A shorthand ambient module declaration: the one form that resolves any named import
        // without knowing what the module exports.
        AssetStage.WriteFileIfChanged(path,
            $"""
             // Written by JsxCore. Declarations for your .NET types have not been generated yet, so
             // imports from "{moduleSpecifier}" are typed as any for now.
             //
             // Run the application once and they are generated from your models, replacing this.
             // To have them present on a fresh clone, point TypeDefinitions.OutputPath at a
             // directory you commit.

             declare module "{moduleSpecifier}";

             """);

        return path;
    }
}
