using System.Text.RegularExpressions;

namespace JsxCore.Compilation.Modules;

public enum ModuleReference
{
    Import,

    Require
}

public sealed record RewrittenSpecifier(string Replacement, string ResolvedPath);

public interface ISpecifierRewriter
{
    RewrittenSpecifier? Rewrite(string specifier, string importerPath, ModuleReference reference);
}

public sealed record ShapedModule(string Source, IReadOnlyList<string> Dependencies);

public static partial class ModuleTransform
{
    public static ShapedModule Apply(
        string path,
        NodeModuleKind kind,
        string source,
        ISpecifierRewriter rewriter)
    {
        ArgumentNullException.ThrowIfNull(rewriter);

        var dependencies = new List<string>();

        switch (kind)
        {
            // Re-expressed as a module because a bare .json cannot be imported without an attribute the
            // compiled views do not emit. The engine has its own JSON module type and does not come here.
            case NodeModuleKind.Json:
                // Re-exported from a JavaScript module rather than imported as JSON.
                return new ShapedModule("export default " + source + ";", dependencies);

            case NodeModuleKind.CommonJs:
            {
                var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
                var required = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var specifier in CommonJsInterop.FindRequires(source))
                {
                    if (rewriter.Rewrite(specifier, path, ModuleReference.Require) is not { } target)
                    {
                        continue;
                    }

                    resolved[specifier] = target.Replacement;
                    required[specifier] = target.ResolvedPath;
                    dependencies.Add(target.ResolvedPath);
                }

                // An entry that is only a re-export names nothing itself, so the names have to come
                // from what it points at. Reading it is why the resolved path is kept alongside the
                // replacement the wrapper imports from.
                return new ShapedModule(
                    CommonJsInterop.Wrap(source, resolved, specifier =>
                        required.TryGetValue(specifier, out var file) && File.Exists(file)
                            ? File.ReadAllText(file)
                            : null),
                    dependencies);
            }

            default:
            {
                var rewritten = SpecifierPattern().Replace(source, match =>
                {
                    var specifier = match.Groups["id"].Value;
                    if (rewriter.Rewrite(specifier, path, ModuleReference.Import) is not { } target)
                    {
                        return match.Value;
                    }

                    dependencies.Add(target.ResolvedPath);
                    var quote = match.Groups["q"].Value;
                    return match.Groups["prefix"].Value + quote + target.Replacement + quote;
                });

                return new ShapedModule(rewritten, dependencies);
            }
        }
    }

    public static IEnumerable<string> FindSpecifiers(string source) =>
        SpecifierPattern().Matches(source).Select(match => match.Groups["id"].Value).Where(id => id.Length > 0);

    // Static imports, re-exports and dynamic imports. A literal that does not resolve is left
    // untouched, so a false match inside a string or comment is harmless.
    [GeneratedRegex(@"(?<prefix>\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)(?<q>['""])(?<id>[^'""\n]*)\k<q>")]
    private static partial Regex SpecifierPattern();
}
