namespace JsxCore.Compilation.Modules;

public sealed class BrowserSpecifierRewriter(
    NodeModuleResolver resolver,
    Func<string, string> urlFor) : ISpecifierRewriter
{
    private readonly NodeModuleResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public RewrittenSpecifier? Rewrite(string specifier, string importerPath, ModuleReference reference)
    {
        // Node lets a package import ./util and find ./util/index.js; a browser will not probe, so the
        // resolution happens here. A nested copy also gets its own URL, which removes any need for scopes.
        var resolved = _resolver.ResolveFrom(specifier, importerPath);
        return resolved is null ? null : new RewrittenSpecifier(urlFor(resolved.Path), resolved.Path);
    }
}
