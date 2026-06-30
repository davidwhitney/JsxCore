using JsxCore.Compilation;

namespace JsxCore;

public class JsxCoreException : Exception
{
    public JsxCoreException(string message) : base(message) { }
    public JsxCoreException(string message, Exception innerException) : base(message, innerException) { }
}

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
