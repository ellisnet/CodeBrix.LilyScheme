// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>An error raised by Scheme code, carrying the arguments Guile's <c>throw</c> passes.</summary>
public sealed class SchemeThrow : Exception
{
    /// <summary>Initializes a thrown condition.</summary>
    /// <param name="key">The throw key symbol.</param>
    /// <param name="arguments">The remaining throw arguments as a Scheme list.</param>
    public SchemeThrow(object key, object arguments)
        : base(Describe(key, arguments))
    {
        Key = key;
        Arguments = arguments;
    }

    /// <summary>Gets the throw key.</summary>
    public object Key { get; }

    /// <summary>Gets the throw arguments as a Scheme list.</summary>
    public object Arguments { get; }

    /// <summary>
    /// Gets or sets the exception OBJECT this throw carries, in Guile's modern-API
    /// sense. <c>raise-exception</c> sets it so a handler recovers the identical
    /// object; for a plain <c>throw</c> it stays null until a handler needs one, when
    /// <c>make-exception-from-throw</c>'s answer is cached here so conversion runs
    /// once per throw.
    /// </summary>
    public object ExceptionObject { get; set; }

    private static string Describe(object key, object arguments)
        => "Scheme error: " + Printer.Write(key) + " " + Printer.Write(arguments);
}
