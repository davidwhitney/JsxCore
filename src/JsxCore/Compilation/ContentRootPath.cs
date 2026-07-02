namespace JsxCore.Compilation;

public static class ContentRootPath
{
    public static string Resolve(string path, string contentRoot) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(contentRoot, path));
}
