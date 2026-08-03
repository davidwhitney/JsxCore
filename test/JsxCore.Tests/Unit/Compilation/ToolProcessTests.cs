using JsxCore.Compilation;
using Shouldly;

namespace JsxCore.Tests.Unit.Compilation;

/// <summary>
/// Running a tool: what comes back, and what never escapes.
/// </summary>
/// <remarks>
/// Every caller degrades rather than failing a build, which only works if this reports trouble in
/// the result instead of raising. These are the paths where it would be tempting not to.
/// </remarks>
public class ToolProcessTests
{
    private static readonly string Shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    private static string[] Script(string command) =>
        OperatingSystem.IsWindows() ? ["/c", command] : ["-c", command];

    [Fact]
    public void Run_ToolSucceeds_ReportsWhatItSaid()
    {
        var result = ToolProcess.Run(Shell, Script("echo hello"));

        result.Succeeded.ShouldBeTrue();
        result.Outcome.ShouldBe(ToolOutcome.Exited);
        result.StandardOutput.Trim().ShouldBe("hello");
    }

    [Fact]
    public void Run_ToolFails_CarriesTheExitCodeAndTheError()
    {
        var result = ToolProcess.Run(Shell, Script("echo trouble 1>&2; exit 3"));

        result.Succeeded.ShouldBeFalse();
        result.Outcome.ShouldBe(ToolOutcome.Exited);
        result.ExitCode.ShouldBe(3);
        result.StandardError.ShouldContain("trouble");
    }

    [Fact]
    public void Run_ToolIsNotThere_IsReportedRatherThanThrown()
    {
        var result = ToolProcess.Run("/definitely/not/a/binary", ["--version"]);

        result.Outcome.ShouldBe(ToolOutcome.CouldNotStart);
        result.Succeeded.ShouldBeFalse();
        result.StandardError.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Run_ToolOutlivesItsTimeout_IsKilledAndReported()
    {
        var result = ToolProcess.Run(Shell, Script("sleep 30"), timeout: TimeSpan.FromMilliseconds(300));

        result.Outcome.ShouldBe(ToolOutcome.TimedOut);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Run_ToolWritesMoreThanAPipeHolds_DoesNotDeadlock()
    {
        // The reason there is one of these rather than a ProcessStartInfo at each call site: a tool
        // that fills one pipe while the other goes unread blocks forever. A pipe buffer is 64KB on
        // most systems, so this is comfortably past it on both streams at once.
        var result = ToolProcess.Run(
            Shell,
            Script("i=0; while [ $i -lt 400 ]; do echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; " +
                   "echo bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 1>&2; i=$((i+1)); done"),
            timeout: TimeSpan.FromSeconds(30));

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.Length.ShouldBeGreaterThan(16_000);
        result.StandardError.Length.ShouldBeGreaterThan(16_000);
    }

    [Fact]
    public async Task RunAsync_Cancelled_Throws()
    {
        // The one case that does raise: the caller asked for it, and a request or a watch rebuild
        // being abandoned should not be reported as a tool that misbehaved.
        using var cancellation = new CancellationTokenSource();
        var running = ToolProcess.RunAsync(
            Shell, Script("sleep 30"), cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await running);
    }
}
