using JsxCore.Compilation.Assets;

namespace JsxCore.TypeScript;

/// <summary>
/// Where generated declarations live, and what the compiler is told when they are not there yet.
/// </summary>
/// <remarks>
/// <para>
/// Declarations are produced by reflecting over the running application, because which types are
/// exported and how their properties are named both depend on configuration that only exists at
/// run time: <c>AutoExport</c>, the naming policy in <c>JsonSerializerOptions</c>, and the rest of
/// <see cref="TypeDefinitionOptions"/>. A build cannot know any of it.
/// </para>
/// <para>
/// So on a fresh clone the build compiles views before anything has generated declarations. Rather
/// than point the compiler at a file with no exports, which fails every named import with TS2305,
/// it is given an ambient declaration for the module. Imports from it resolve as <c>any</c>: no
/// model type safety until the application has run once, but no errors about types that are going
/// to exist either.
/// </para>
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

        // A shorthand ambient module declaration, which is the one form that makes every named
        // import from a specifier resolve without knowing what it exports.
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
