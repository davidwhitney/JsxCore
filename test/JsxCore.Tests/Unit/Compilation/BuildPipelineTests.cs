using JsxCore.Compilation;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Pipeline;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Pipeline.Steps;
using JsxCore.Compilation.Pipeline.Steps.Gather;
using JsxCore.Compilation.Pipeline.Steps.Prepare;

namespace JsxCore.Tests.Unit.Compilation;

/// <summary>
/// The pipeline's own behaviour, which used to be the order of statements in a method.
/// </summary>
public class BuildPipelineTests
{
    private sealed class RecordingStep(string name, string? fingerprint, bool applies, List<string> log) : IBuildStep
    {
        public string Name => name;
        public bool AppliesTo(BuildContext context) => applies;

        public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
        {
            log.Add(name);
            return new ValueTask<StepResult>(new StepResult(fingerprint));
        }
    }

    private static BuildContext Context(bool precompiled = false) => new(
        new JsxCoreOptions(),
        CompilationLayout.Create(new JsxCoreOptions(), Path.GetTempPath()),
        NullLogger.Instance,
        precompiled);

    [Fact]
    public async Task Run_SeveralSteps_RunsThemInDeclaredOrder()
    {
        var log = new List<string>();
        var pipeline = new BuildPipeline(
            new RecordingStep("first", null, true, log),
            new RecordingStep("second", null, true, log),
            new RecordingStep("third", null, true, log));

        await pipeline.RunAsync(Context());

        log.ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public async Task Run_StepDoesNotApply_IsSkipped()
    {
        var log = new List<string>();
        var pipeline = new BuildPipeline(
            new RecordingStep("runs", null, true, log),
            new RecordingStep("skipped", null, false, log));

        await pipeline.RunAsync(Context());

        log.ShouldBe(["runs"]);
    }

    [Fact]
    public async Task Run_StepsContributeFingerprints_CombinesThemInOrder()
    {
        var log = new List<string>();
        var pipeline = new BuildPipeline(
            new RecordingStep("a", "aaa", true, log),
            new RecordingStep("b", null, true, log),
            new RecordingStep("c", "ccc", true, log));

        (await pipeline.RunAsync(Context())).ShouldBe("aaaccc");
    }

    [Fact]
    public async Task Run_StepIsSkipped_ContributesNothingToTheFingerprint()
    {
        var log = new List<string>();
        var pipeline = new BuildPipeline(
            new RecordingStep("a", "aaa", true, log),
            new RecordingStep("b", "bbb", false, log));

        (await pipeline.RunAsync(Context())).ShouldBe("aaa");
    }

    [Fact]
    public async Task Run_CancellationIsRequested_StopsBeforeRunningAnyStep()
    {
        var log = new List<string>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var pipeline = new BuildPipeline(new RecordingStep("never", null, true, log));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await pipeline.RunAsync(Context(), cancellation.Token));
        log.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------------------------
    // Which steps apply, which is what a precompiled application depends on being right.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void AppliesTo_ApplicationIsPrecompiled_BuildStepsDoNotRun()
    {
        var context = Context(precompiled: true);

        new ExtractRuntimeAssets().AppliesTo(context).ShouldBeFalse();
        new GenerateModelTypes().AppliesTo(context).ShouldBeFalse();
        new WriteCompilerConfig().AppliesTo(context).ShouldBeFalse();
    }

    [Fact]
    public void AppliesTo_ApplicationIsPrecompiled_PreactIsStillStaged()
    {
        // Preact is served to the browser rather than compiled, so it has to be on disk even
        // where nothing is being built.
        var stager = new PreactVendorStager(
            CompilationLayout.Create(new JsxCoreOptions(), Path.GetTempPath()),
            NodeModulesLayout.For(JsxProjectFixture.RepositoryRoot()),
            NullLogger.Instance);

        new StagePreactRuntime(stager).AppliesTo(Context(precompiled: true)).ShouldBeTrue();
    }

    [Fact]
    public void AppliesTo_BuiltInRuntime_PreactStagingIsSkipped()
    {
        new StagePreactRuntime(null).AppliesTo(Context()).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------------
    // The gather phase, which every later step reads from.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Gather_PipelineRuns_PopulatesTheInputsForLaterSteps()
    {
        var context = Context();
        await new BuildPipeline(new GatherProjectInputs()).RunAsync(context);

        context.Inputs.NodeModules.ShouldNotBeNull();
    }

    [Fact]
    public async Task Gather_ManifestIsPresent_ReadsItIntoTheContext()
    {
        using var project = JsxProjectFixture.Create();
        File.WriteAllText(Path.Combine(project.Root, "package.json"),
            """{"dependencies":{"marked":"^18.0.0"},"devDependencies":{"typescript":"^7.0.2"}}""");

        var context = new BuildContext(
            project.Options, project.Layout, NullLogger.Instance, Precompiled: false);
        await new BuildPipeline(new GatherProjectInputs()).RunAsync(context);

        context.Inputs.Manifest.ShouldNotBeNull().Packages.Count.ShouldBe(2);
        context.Inputs.RuntimeDependencies.ShouldBe(["marked"]);
    }

    [Fact]
    public async Task MissingPackages_PackageIsDeclaredButNotInstalled_IsReported()
    {
        using var project = JsxProjectFixture.Create();
        File.WriteAllText(Path.Combine(project.Root, "package.json"),
            """{"dependencies":{"never-installed":"^1.0.0"}}""");

        var context = new BuildContext(
            project.Options, project.Layout, NullLogger.Instance, Precompiled: false);
        await new BuildPipeline(new GatherProjectInputs()).RunAsync(context);

        context.Inputs.MissingPackages.ShouldBe(["never-installed"]);
    }

    [Fact]
    public async Task Gather_PipelineRuns_ContributesNothingToTheBuildId()
    {
        // The build id describes what was produced, not what was read, so reading changes nothing.
        var fingerprint = await new BuildPipeline(
            new GatherProjectInputs(), new CheckDeclaredPackages()).RunAsync(Context());

        fingerprint.ShouldBeEmpty();
    }

    [Fact]
    public void AppliesTo_ApplicationIsPrecompiled_GatheringStillRuns()
    {
        // A precompiled server still resolves packages when it renders, so it still wants to know.
        ((IBuildStep)new GatherProjectInputs()).AppliesTo(Context(precompiled: true)).ShouldBeTrue();
        ((IBuildStep)new CheckDeclaredPackages()).AppliesTo(Context(precompiled: true)).ShouldBeTrue();
    }

    [Fact]
    public void AppliesTo_TypeGenerationIsDisabled_StepDoesNotRun()
    {
        var context = Context();
        context.Options.TypeDefinitions.Enabled = false;

        new GenerateModelTypes().AppliesTo(context).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------------
    // Everything derived from a build is keyed on the build id, and superseded entries go.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Get_BuildIdChanges_RecomputesTheValue()
    {
        var cache = new BuildScopedCache<object>();
        var built = 0;

        cache.Get("build-1", () => { built++; return new object(); });
        cache.Get("build-1", () => { built++; return new object(); });
        cache.Get("build-2", () => { built++; return new object(); });

        built.ShouldBe(2);
    }

    [Fact]
    public void Get_EarlierBuildIsRequestedAgain_HasBeenDiscarded()
    {
        // The point of the single slot: a development session recompiles on every edit, and
        // keeping each build's derived state would grow the process for the life of the session.
        var cache = new BuildScopedCache<string>();
        cache.Get("build-1", () => "one");
        cache.Get("build-2", () => "two");

        var built = 0;
        cache.Get("build-1", () => { built++; return "one again"; });

        built.ShouldBe(1);
    }
}
