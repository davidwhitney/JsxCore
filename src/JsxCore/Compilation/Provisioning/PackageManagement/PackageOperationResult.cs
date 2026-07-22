namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed record PackageOperationResult(bool Succeeded, string Description, string? Failure = null)
{
    public static PackageOperationResult Ok(string description) => new(true, description);

    public static PackageOperationResult Failed(string description, string failure) =>
        new(false, description, failure);

    public static readonly PackageOperationResult NothingToDo = new(true, "nothing to do");
}
