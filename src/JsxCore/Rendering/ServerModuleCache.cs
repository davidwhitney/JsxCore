using System.Collections.Concurrent;
using Jint;
using AstModule = Acornima.Ast.Module;

namespace JsxCore.Rendering;

/// <summary>
/// Modules parsed once and shared by every pooled engine rendering one compilation. A pooled
/// engine keeps its own module graph; this keeps the parsing from being repeated when the pool
/// grows, or refills after a rebuild.
/// </summary>
/// <remarks>
/// A pool fills under a burst of requests, and the engines it builds walk the same modules in the
/// same order — so the natural race is every engine parsing every module and all but one result
/// being thrown away. The <see cref="Lazy{T}"/> holds that door: whoever asks first parses, and
/// the rest wait for that result instead of duplicating it.
/// </remarks>
internal sealed class ServerModuleCache
{
    private readonly ConcurrentDictionary<string, Lazy<Prepared<AstModule>>> _modules = new(StringComparer.Ordinal);

    public Prepared<AstModule> GetOrParse(string location, Func<string> readSource) =>
        _modules.GetOrAdd(
            location,
            l => new Lazy<Prepared<AstModule>>(() => Engine.PrepareModule(readSource(), l))).Value;
}
