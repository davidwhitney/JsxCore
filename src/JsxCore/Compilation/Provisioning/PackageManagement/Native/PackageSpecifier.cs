namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

// What a dependency entry actually asks for. Usually the name and the range, but an alias splits
// the two: "react-is-18": "npm:react-is@^18.3.1" installs react-is under a different folder name,
// which is how a package depends on two versions of the same thing at once.
public sealed record PackageSpecifier(string InstallName, string RegistryName, VersionRange Range)
{
    public bool IsAlias => !string.Equals(InstallName, RegistryName, StringComparison.Ordinal);

    public static PackageSpecifier Parse(string name, string? range)
    {
        var text = (range ?? string.Empty).Trim();

        if (!text.StartsWith("npm:", StringComparison.Ordinal))
        {
            return new PackageSpecifier(name, name, VersionRange.Parse(text));
        }

        var target = text[4..];

        // A scoped target starts with @, so the separating @ is the one after the scope.
        var separator = target.LastIndexOf('@');
        if (separator <= 0)
        {
            return new PackageSpecifier(name, target, VersionRange.Parse(string.Empty));
        }

        return new PackageSpecifier(name, target[..separator], VersionRange.Parse(target[(separator + 1)..]));
    }
}
