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
/// <para>
/// A pool fills under a burst of requests, and the engines it builds walk the same modules in the
/// same order — so the natural race is every engine parsing every module and all but one result
/// being thrown away. The <see cref="Lazy{T}"/> holds that door: whoever asks first parses, and
/// the rest wait for that result instead of duplicating it.
/// </para>
/// <para>
/// <paramref name="staticAnalysis"/> chooses which half of the engine's preparation trade this
/// compilation wants. Preparing with the analysis pass costs about twice a plain parse and hands
/// every engine a tree with the interpreter's own bookkeeping already on it; preparing without it
/// leaves each engine to work that out as it reaches a node. Whichever is cheaper depends on how
/// many engines end up reading a parse, and a set of modules thrown away on the next edit is read
/// by very few.
/// </para>
/// </remarks>
internal sealed class ServerModuleCache(bool staticAnalysis)
{
    /// <summary>
    /// Shared, because it is a description of how to prepare rather than state: one instance for
    /// every cache that wants a parse without the analysis pass.
    /// </summary>
    private static readonly ModulePreparationOptions ParseOnly = new() { StaticAnalysis = false };

    private readonly ModulePreparationOptions? _preparation = staticAnalysis ? null : ParseOnly;

    private readonly ConcurrentDictionary<string, Lazy<Prepared<AstModule>>> _modules = new(StringComparer.Ordinal);

    public Prepared<AstModule> GetOrParse(string location, Func<string> readSource) =>
        _modules.GetOrAdd(
            location,
            l => new Lazy<Prepared<AstModule>>(() => Engine.PrepareModule(readSource(), l, _preparation))).Value;
}
