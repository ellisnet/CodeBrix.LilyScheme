// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// The kinds of frame on <see cref="ExceptionRuntime.Handlers"/>. Every construct that
/// can intercept an in-flight throw registers one, so <c>raise-exception</c>'s
/// continuable dispatch can tell whether the INNERMOST interceptor is a non-unwinding
/// handler — the only case where a handler's return value flows back to the raise
/// point — or something that .NET exception propagation must reach instead.
/// </summary>
internal enum ExceptionHandlerFrameKind
{
    /// <summary>A <c>with-exception-handler</c> with <c>#:unwind? #f</c>.</summary>
    NonUnwinding,

    /// <summary>A <c>with-exception-handler</c> with <c>#:unwind? #t</c>; the match
    /// condition is its <c>#:unwind-for-type</c>.</summary>
    Unwinding,

    /// <summary>A <c>catch</c> frame; the match condition is its key.</summary>
    CatchFrame,

    /// <summary>A <c>with-throw-handler</c> frame; the match condition is its key.</summary>
    ThrowHandlerFrame,
}

/// <summary>One entry on the dynamic exception-handler stack.</summary>
internal sealed class ExceptionHandlerFrame
{
    /// <summary>Initializes a frame.</summary>
    /// <param name="kind">What established the frame.</param>
    /// <param name="handler">The handler procedure, for a non-unwinding frame.</param>
    /// <param name="typeOrKey">The match condition: an <c>#:unwind-for-type</c> for an
    /// unwinding frame, a throw key for a catch or throw-handler frame.</param>
    internal ExceptionHandlerFrame(ExceptionHandlerFrameKind kind, object handler, object typeOrKey)
    {
        Kind = kind;
        Handler = handler;
        TypeOrKey = typeOrKey;
    }

    /// <summary>Gets what established the frame.</summary>
    internal ExceptionHandlerFrameKind Kind { get; }

    /// <summary>Gets the handler procedure of a non-unwinding frame.</summary>
    internal object Handler { get; }

    /// <summary>Gets the frame's match condition.</summary>
    internal object TypeOrKey { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the frame is out of play — set while
    /// (and after) its handler runs, which is how "the current exception handler is
    /// the outer one" is kept during a handler's own raises.
    /// </summary>
    internal bool Disabled { get; set; }
}

/// <summary>
/// Per-interpreter state for Guile's modern exception API: the standard exception
/// record types, the dynamic handler stack, the live <c>make-exception-from-throw</c>
/// binding, and the <c>print-exception</c> printer alist.
/// </summary>
internal sealed class ExceptionRuntime
{
    /// <summary>Gets or sets <c>&amp;exception</c>.</summary>
    internal RecordType ExceptionType { get; set; }

    /// <summary>Gets or sets <c>&amp;compound-exception</c> — deliberately NOT a
    /// subtype of <c>&amp;exception</c>, exactly as boot-9 builds it.</summary>
    internal RecordType CompoundExceptionType { get; set; }

    /// <summary>Gets or sets <c>&amp;exception-with-kind-and-args</c> (sealed).</summary>
    internal RecordType KindAndArgsType { get; set; }

    /// <summary>Gets or sets <c>&amp;quit-exception</c> (sealed).</summary>
    internal RecordType QuitExceptionType { get; set; }

    /// <summary>Gets or sets <c>&amp;error</c>.</summary>
    internal RecordType ErrorType { get; set; }

    /// <summary>Gets or sets <c>&amp;programming-error</c>.</summary>
    internal RecordType ProgrammingErrorType { get; set; }

    /// <summary>Gets or sets <c>&amp;non-continuable</c>.</summary>
    internal RecordType NonContinuableType { get; set; }

    /// <summary>
    /// Gets or sets the core <c>make-exception-from-throw</c> variable. Read LIVE on
    /// every conversion: the vendored ice-9/exceptions.scm upgrades the binding with
    /// <c>set!</c> to its converter table, exactly as it does over Guile's boot
    /// definition.
    /// </summary>
    internal Variable MakeExceptionFromThrow { get; set; }

    /// <summary>Gets or sets the <c>display</c> variable, used to write through
    /// whatever port <c>print-exception</c> is handed.</summary>
    internal Variable Display { get; set; }

    /// <summary>Gets or sets the printer alist behind <c>set-exception-printer!</c> —
    /// newest first, so a re-registration shadows, as boot-9's acons does.</summary>
    internal object ExceptionPrinters { get; set; } = Nil.Instance;

    /// <summary>Gets the dynamic handler stack; the innermost frame is last.</summary>
    internal List<ExceptionHandlerFrame> Handlers { get; } = new List<ExceptionHandlerFrame>();
}
