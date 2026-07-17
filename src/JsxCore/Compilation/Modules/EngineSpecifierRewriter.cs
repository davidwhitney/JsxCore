namespace JsxCore.Compilation.Modules;

public sealed class EngineSpecifierRewriter(NodeModuleResolver resolver) : ISpecifierRewriter
{
    private readonly NodeModuleResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public RewrittenSpecifier? Rewrite(string specifier, string importerPath, ModuleReference reference)
    {
        // Imports are left alone because the engine resolves those itself. A require that resolves to
        // nothing is too, so the wrapper's own require throws for it, which is right for a Node built-in.
        if (reference != ModuleReference.Require)
        {
            return null;
        }

        var resolved = _resolver.ResolveFrom(specifier, importerPath);
        return resolved is null
            ? null
            : new RewrittenSpecifier(resolved.Path.Replace(Path.DirectorySeparatorChar, '/'), resolved.Path);
    }
}
