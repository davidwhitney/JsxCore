using System.Diagnostics;
using Jint;

namespace JsxCore.Rendering;

/// <summary>
/// The time budget for one render, and the request it belongs to.
/// </summary>
/// <remarks>
/// The engine re-arms its built-in timeout at every entry from the host, and a render enters the
/// engine several times, so a configured timeout used to bound each entry rather than the render.
/// This holds one deadline for the whole render: armed before the engine goes to work, cleared
/// when it returns to the pool, and deliberately untouched by the engine's own per-entry reset.
/// It also watches the request's abort token, so a client that has gone away stops paying for
/// JavaScript it will never receive.
/// </remarks>
internal sealed class RenderDeadline : Constraint
{
    /// <summary>A budget that never runs out, which is how an unarmed engine sits in the pool.</summary>
    private const long Disarmed = 0;

    private long _deadline;
    private CancellationToken _aborted;

    /// <summary>
    /// Reads a clock and a token without consuming anything, so the engine may check on its
    /// amortised cadence.
    /// </summary>
    public override bool IsAmortizable => true;

    /// <summary>Arms the budget for one render.</summary>
    /// <remarks>
    /// A timeout of zero or less asks for no budget at all, which is what
    /// <see cref="Timeout.InfiniteTimeSpan"/> means and what the engine's own timeout did with one.
    /// Arming it arithmetically instead would put the deadline at or before the render's own start
    /// and end every render the moment it began.
    /// </remarks>
    public void Begin(TimeSpan timeout, CancellationToken aborted)
    {
        _aborted = aborted;

        if (timeout <= TimeSpan.Zero)
        {
            _deadline = Disarmed;
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var budget = timeout.TotalSeconds * Stopwatch.Frequency;

        // A budget too large to add to the clock is one no render was ever going to reach, so it
        // becomes the furthest deadline there is rather than an overflowed one in the past.
        _deadline = budget >= long.MaxValue - start
            ? long.MaxValue
            : Math.Max(start + (long) budget, Disarmed + 1);
    }

    /// <summary>Disarms the budget, so a pooled engine carries no deadline between renders.</summary>
    public void End()
    {
        _deadline = Disarmed;
        _aborted = default;
    }

    /// <inheritdoc />
    public override void Check()
    {
        if (_aborted.IsCancellationRequested)
        {
            throw new OperationCanceledException(_aborted);
        }

        if (_deadline != 0 && Stopwatch.GetTimestamp() >= _deadline)
        {
            throw new TimeoutException("The render exceeded the configured server rendering timeout.");
        }
    }

    /// <summary>
    /// Does nothing, deliberately. The engine resets constraints at every entry from the host, and
    /// this budget belongs to the whole render rather than to any one entry into the engine.
    /// </summary>
    public override void Reset()
    {
    }
}
