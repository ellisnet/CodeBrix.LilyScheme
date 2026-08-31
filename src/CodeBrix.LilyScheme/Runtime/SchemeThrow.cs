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
public class SchemeThrow : Exception
{
    /// <summary>Initializes a thrown condition.</summary>
    /// <param name="key">The throw key symbol.</param>
    /// <param name="arguments">The remaining throw arguments as a Scheme list.</param>
    /// <remarks>
    /// NOT sealed, for one reason:
    /// <see cref="Reader.SchemeReaderException"/> derives from it. A read error is a
    /// <c>read-error</c> condition in Guile and must be catchable by Scheme, while the
    /// exception TYPE stays what hosts have always caught in C#. Every catch clause in
    /// this library is written <c>catch (SchemeThrow ...)</c>, which matches a derived
    /// type, so a subclass is caught, converted and re-raised like any other condition.
    /// </remarks>
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

    /// <summary>
    /// Builds Guile's <c>out-of-range</c> condition — <c>scm_out_of_range</c>'s shape,
    /// which names the procedure and carries the offending value TWICE, once as the
    /// message's irritant and once as the condition's own rest argument.
    /// </summary>
    /// <param name="procedureName">The procedure the value was passed to.</param>
    /// <param name="value">The value that was out of range.</param>
    /// <returns>The condition to throw.</returns>
    internal static SchemeThrow OutOfRange(string procedureName, object value)
        => new SchemeThrow(
            Symbol.Intern("out-of-range"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Value out of range: ~S"),
                Pair.List(value),
                Pair.List(value)));

    private static string Describe(object key, object arguments)
        => "Scheme error: " + Printer.Write(key) + " " + Printer.Write(arguments);
}
