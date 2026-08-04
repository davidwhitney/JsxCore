namespace JsxCore;

/// <summary>Base type for everything JsxCore throws deliberately.</summary>
/// <remarks>
/// Lives here rather than beside the exceptions that derive from it because the npm client throws
/// it and this assembly is the lower of the two. The derived types, which describe failures only
/// the view engine can have, are in JsxCore/JsxCoreException.cs.
/// </remarks>
public class JsxCoreException : Exception
{
    public JsxCoreException(string message) : base(message) { }
    public JsxCoreException(string message, Exception innerException) : base(message, innerException) { }
}
