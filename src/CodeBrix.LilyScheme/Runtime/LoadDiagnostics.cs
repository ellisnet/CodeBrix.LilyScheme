// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Diagnostics;
using System.Threading;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// Wall-clock accumulators for the source-loading pipeline, answering where load time
/// goes: reading source into forms, macro-expanding forms through psyntax, or the plain
/// (pre-expansion) evaluator used while bootstrapping psyntax itself.
/// <para>
/// Loads nest — evaluating one file's top-level form can load another file — so the
/// expansion accumulator counts OUTERMOST expansion spans only, and evaluation cost is
/// deliberately not accumulated here: nested loads would land inside the enclosing
/// form's evaluation span and double-count. Compute evaluation as the caller-measured
/// wall-clock total minus <see cref="ReadTime"/> and <see cref="ExpandTime"/>.
/// </para>
/// <para>
/// Accumulation is cheap (two timestamps per top-level form) and always on. Counters
/// are process-wide; call <see cref="Reset"/> before the measured region.
/// </para>
/// </summary>
public static class LoadDiagnostics
{
    [ThreadStatic]
    private static int _expandDepth;

    [ThreadStatic]
    private static long _expandedFormCountOnThread;

    private static long _readTicks;
    private static long _expandTicks;
    private static long _plainEvalTicks;
    private static long _expandedFormCount;

    /// <summary>Gets the accumulated time spent reading source text into forms.</summary>
    public static TimeSpan ReadTime => ToTimeSpan(Interlocked.Read(ref _readTicks));

    /// <summary>
    /// Gets the accumulated time spent inside <c>macroexpand</c>, counting outermost
    /// expansion spans only.
    /// </summary>
    public static TimeSpan ExpandTime => ToTimeSpan(Interlocked.Read(ref _expandTicks));

    /// <summary>
    /// Gets the accumulated time spent evaluating forms through the plain evaluator in
    /// <see cref="SchemeBootstrap.LoadSource"/> (the pre-expansion psyntax bootstrap path).
    /// </summary>
    public static TimeSpan PlainEvalTime => ToTimeSpan(Interlocked.Read(ref _plainEvalTicks));

    /// <summary>Gets the number of outermost expansion spans accumulated so far.</summary>
    public static long ExpandedFormCount => Interlocked.Read(ref _expandedFormCount);

    /// <summary>
    /// Gets the number of outermost expansion spans THIS THREAD has accumulated since it
    /// last called <see cref="Reset"/>.
    /// <para>
    /// The process-wide count above answers "how much expansion has this process done",
    /// which is what a boot's timing report wants. It cannot answer "did THIS load
    /// expand anything", because any other thread expanding concurrently adds to it —
    /// and a caller that reads it as if it could gets an answer that is right almost
    /// every time and occasionally one too high. Since a load runs to completion on the
    /// thread that started it, the per-thread count is the honest measure for that
    /// question.
    /// </para>
    /// </summary>
    public static long ExpandedFormCountOnThisThread => _expandedFormCountOnThread;

    /// <summary>
    /// Resets every accumulator to zero. The process-wide counters are reset for the
    /// whole process; <see cref="ExpandedFormCountOnThisThread"/> is reset for the
    /// CALLING thread only, which is what makes it usable while other threads work.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _readTicks, 0);
        Interlocked.Exchange(ref _expandTicks, 0);
        Interlocked.Exchange(ref _plainEvalTicks, 0);
        Interlocked.Exchange(ref _expandedFormCount, 0);
        _expandedFormCountOnThread = 0;
    }

    /// <summary>Adds a reader span, in <see cref="Stopwatch"/> timestamp ticks.</summary>
    /// <param name="stopwatchTicks">The elapsed timestamp ticks.</param>
    internal static void AddRead(long stopwatchTicks)
    {
        Interlocked.Add(ref _readTicks, stopwatchTicks);
    }

    /// <summary>Adds a plain-evaluation span, in <see cref="Stopwatch"/> timestamp ticks.</summary>
    /// <param name="stopwatchTicks">The elapsed timestamp ticks.</param>
    internal static void AddPlainEval(long stopwatchTicks)
    {
        Interlocked.Add(ref _plainEvalTicks, stopwatchTicks);
    }

    /// <summary>
    /// Enters an expansion span, tracking nesting so that only the outermost span on
    /// this thread is accumulated.
    /// </summary>
    /// <returns><c>true</c> when this span is the outermost one on this thread.</returns>
    internal static bool EnterExpand()
    {
        return _expandDepth++ == 0;
    }

    /// <summary>Leaves an expansion span, accumulating it when it was outermost.</summary>
    /// <param name="outermost">The value <see cref="EnterExpand"/> returned for this span.</param>
    /// <param name="stopwatchTicks">The elapsed timestamp ticks.</param>
    internal static void ExitExpand(bool outermost, long stopwatchTicks)
    {
        _expandDepth--;
        if (outermost)
        {
            Interlocked.Add(ref _expandTicks, stopwatchTicks);
            Interlocked.Increment(ref _expandedFormCount);
            _expandedFormCountOnThread++;
        }
    }

    private static TimeSpan ToTimeSpan(long stopwatchTicks)
    {
        return TimeSpan.FromSeconds(stopwatchTicks / (double)Stopwatch.Frequency);
    }
}
