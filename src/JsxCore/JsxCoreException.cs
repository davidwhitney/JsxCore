using JsxCore.Compilation;

namespace JsxCore;

// The base type, JsxCoreException, is in JsxCore.PackageManagement/Shared. The npm client throws it
// and cannot reference this assembly, so it sits at the bottom of the two. Same namespace either
// way, so nothing catching it can tell the difference.

public sealed class JsxCoreEnvironmentException : JsxCoreException
{
    public JsxCoreEnvironmentException(string message) : base(message) { }

    public JsxCoreEnvironmentException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class JsxCompilationException(string message, IReadOnlyList<CompilerDiagnostic> diagnostics)
    : JsxCoreException(message)
{
    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed class JsxViewNotFoundException(string viewName, IReadOnlyList<string> searchedLocations)
    : JsxCoreException($"The JSX view '{viewName}' was not found. Locations searched:{Environment.NewLine}" +
                       string.Join(Environment.NewLine, searchedLocations.Select(l => "  " + l)))
{
    public string ViewName { get; } = viewName;

    public IReadOnlyList<string> SearchedLocations { get; } = searchedLocations;
}

public sealed class JsxRenderException(string message, Exception innerException)
    : JsxCoreException(message, innerException);
