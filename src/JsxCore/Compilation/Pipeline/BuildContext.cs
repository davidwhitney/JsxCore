using Microsoft.Extensions.Logging;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Pipeline;

public sealed record BuildContext(
    JsxCoreOptions Options,
    CompilationLayout Layout,
    ILogger Logger,
    bool Precompiled)
{
    // The accumulator of a fold. Steps may read it, which is how the compiler learns the build
    // id to stamp on its output, but only the pipeline writes to it.
    public string Fingerprint { get; private set; } = string.Empty;

    public ProjectInputs Inputs { get; private set; } =
        new(NodeModulesLayout.For(Layout.ContentRoot), Manifest: null);

    /// <summary>
    /// What the compiler made of the views, on an emit run. Null while preparing, because nothing
    /// has compiled yet.
    /// </summary>
    public CompilationResult? Compilation { get; private set; }

    /// <summary>What linking recorded about each compiled module, once it has run.</summary>
    public ViewManifest? Views { get; private set; }

    internal void Contribute(string value) => Fingerprint += value;

    internal void Gathered(ProjectInputs inputs) => Inputs = inputs;

    internal void Compiled(CompilationResult result) => Compilation = result;

    internal void Linked(ViewManifest views) => Views = views;
}
