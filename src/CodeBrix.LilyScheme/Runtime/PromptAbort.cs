// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// The non-local exit thrown by <c>abort-to-prompt</c> and caught by the matching
/// <c>call-with-prompt</c>.
/// <para>
/// This deliberately does not derive from <see cref="SchemeThrow"/>: a prompt abort
/// must pass through every <c>catch</c> in between untouched, exactly as Guile's
/// aborts pass through its throw handlers.
/// </para>
/// </summary>
public sealed class PromptAbort : Exception
{
    /// <summary>Initializes a prompt abort.</summary>
    /// <param name="tag">The prompt tag, matched by <c>eq?</c> identity.</param>
    /// <param name="arguments">The arguments handed to the prompt's handler.</param>
    public PromptAbort(object tag, object[] arguments)
        : base("abort to prompt")
    {
        Tag = tag;
        Arguments = arguments ?? Array.Empty<object>();
    }

    /// <summary>Gets the prompt tag, matched by <c>eq?</c> identity.</summary>
    public object Tag { get; }

    /// <summary>Gets the arguments handed to the prompt's handler.</summary>
    public object[] Arguments { get; }
}
