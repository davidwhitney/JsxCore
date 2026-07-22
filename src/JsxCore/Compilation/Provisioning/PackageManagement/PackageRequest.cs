namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed record PackageRequest(string Name, string VersionRange = "", bool Development = false)
{
    public string Specifier => VersionRange.Length == 0 ? Name : $"{Name}@{VersionRange}";
}
