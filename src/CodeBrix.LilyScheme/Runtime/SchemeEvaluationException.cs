// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>An error detected by the evaluator itself, rather than raised by Scheme code.</summary>
public sealed class SchemeEvaluationException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">A description of the fault.</param>
    public SchemeEvaluationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with an inner cause.</summary>
    /// <param name="message">A description of the fault.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SchemeEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
