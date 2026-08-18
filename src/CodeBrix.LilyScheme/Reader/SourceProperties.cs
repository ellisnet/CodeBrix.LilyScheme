// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Runtime.CompilerServices;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Reader;

/// <summary>
/// Where a datum was read from: the source name, and a ZERO-BASED line and column.
/// <para>
/// The line is zero-based because Guile's is: <c>source-line-for-user</c>
/// (<c>system/vm/debug.scm:673-674</c>) is <c>(1+ (source-line source))</c>, so anything
/// that shows a line to a human adds the one back. The column is zero-based on both
/// sides and is shown as it stands.
/// </para>
/// </summary>
public sealed class SourceLocation
{
    /// <summary>Initializes a source location.</summary>
    /// <param name="fileName">The source name the reader was given.</param>
    /// <param name="line">The ZERO-BASED line.</param>
    /// <param name="column">The zero-based column.</param>
    public SourceLocation(string fileName, int line, int column)
    {
        FileName = fileName;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the source name.</summary>
    public string FileName { get; }

    /// <summary>Gets the ZERO-BASED line.</summary>
    public int Line { get; }

    /// <summary>Gets the zero-based column.</summary>
    public int Column { get; }
}

/// <summary>
/// Guile's source-property table: where each datum the reader built came from.
/// <para>
/// This is not decoration. psyntax reads it and nothing else — <c>datum-sourcev</c>
/// (<c>ice-9/psyntax.scm:307-312</c>) asks <c>source-properties</c> for an alist and turns
/// it into the <c>#(filename line column)</c> vector it threads through expansion into
/// every Tree-IL node's <c>src</c> field. With the table empty, psyntax has nothing to
/// propagate and every procedure, every error message and every backtrace loses its
/// location, with no diagnostic anywhere.
/// </para>
/// <para>
/// Entries are keyed by OBJECT IDENTITY in a weak table, exactly as Guile keys its own,
/// so a datum that is collected takes its properties with it. Only objects
/// <see cref="Supports"/> accepts are recorded: interning would make a symbol's location
/// meaningless, since every occurrence of a symbol is the same object.
/// </para>
/// </summary>
public static class SourceProperties
{
    private static readonly ConditionalWeakTable<object, object> Table
        = new ConditionalWeakTable<object, object>();

    private static readonly Symbol FileNameSymbol = Symbol.Intern("filename");
    private static readonly Symbol LineSymbol = Symbol.Intern("line");
    private static readonly Symbol ColumnSymbol = Symbol.Intern("column");

    /// <summary>
    /// Determines whether a datum can carry source properties.
    /// <para>
    /// Pairs and vectors can; an interned symbol, a number or a character cannot, because
    /// the object is shared between every occurrence in every file.
    /// </para>
    /// </summary>
    /// <param name="datum">The datum to test.</param>
    /// <returns><see langword="true"/> when properties may be attached.</returns>
    public static bool Supports(object datum) => datum is Pair || datum is object[];

    /// <summary>Records where the reader found a datum.</summary>
    /// <param name="datum">The datum; ignored when it cannot carry properties.</param>
    /// <param name="location">The location.</param>
    public static void Record(object datum, SourceLocation location)
    {
        if (location == null || !Supports(datum))
        {
            return;
        }

        Table.Remove(datum);
        Table.Add(datum, location);
    }

    /// <summary>Returns a datum's source properties as an alist.</summary>
    /// <param name="datum">The datum to look up.</param>
    /// <returns>The alist, or <see cref="Nil.Instance"/> when nothing was recorded.</returns>
    public static object Get(object datum)
    {
        if (datum == null || !Table.TryGetValue(datum, out object stored))
        {
            return Nil.Instance;
        }

        if (stored is SourceLocation location)
        {
            // The order is sourcev->alist's (ice-9/psyntax.scm:195-200), the only order
            // psyntax itself ever constructs. Every reader of the alist uses assq.
            //
            // The numbers are LONG, not int: long is the canonical exact-integer
            // representation here (SchemeNumber.Normalize hands one back for anything
            // that fits), and these values travel a long way — psyntax copies them into
            // every Tree-IL node's src, and the expansion cache serializes that graph.
            // A raw int is not a value the rest of the system knows, and the cache
            // refuses it at record time rather than writing something it cannot read
            // back, so the whole boot cache silently stops recording.
            return Pair.List(
                new Pair(FileNameSymbol, new MutableString(location.FileName)),
                new Pair(LineSymbol, (long)location.Line),
                new Pair(ColumnSymbol, (long)location.Column));
        }

        return stored;
    }

    /// <summary>Replaces a datum's source properties wholesale.</summary>
    /// <param name="datum">The datum; ignored when it cannot carry properties.</param>
    /// <param name="properties">The alist to store.</param>
    public static void Set(object datum, object properties)
    {
        if (!Supports(datum))
        {
            return;
        }

        Table.Remove(datum);
        Table.Add(datum, properties);
    }

    /// <summary>Reads one source property.</summary>
    /// <param name="datum">The datum to look up.</param>
    /// <param name="key">The property name.</param>
    /// <returns>The value, or <see langword="false"/> when it is not set.</returns>
    public static object Property(object datum, object key)
    {
        foreach (object entry in Pair.ToList(Get(datum)))
        {
            if (entry is Pair pair && ReferenceEquals(pair.Car, key))
            {
                return pair.Cdr;
            }
        }

        return false;
    }

    /// <summary>Sets one source property, leaving the others in place.</summary>
    /// <param name="datum">The datum; ignored when it cannot carry properties.</param>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to store.</param>
    public static void SetProperty(object datum, object key, object value)
    {
        if (!Supports(datum))
        {
            return;
        }

        object existing = Get(datum);
        object rebuilt = new Pair(new Pair(key, value), Nil.Instance);
        Pair last = (Pair)rebuilt;
        foreach (object entry in Pair.ToList(existing))
        {
            if (entry is Pair pair && ReferenceEquals(pair.Car, key))
            {
                continue;
            }

            Pair appended = new Pair(entry, Nil.Instance);
            last.Cdr = appended;
            last = appended;
        }

        Set(datum, rebuilt);
    }

    /// <summary>
    /// Copies whatever properties one datum carries onto another, leaving the target
    /// untouched when the source has none.
    /// </summary>
    /// <param name="source">The datum to read from.</param>
    /// <param name="target">The datum to write to.</param>
    public static void CopyTo(object source, object target)
    {
        if (source == null || !Supports(target) || !Table.TryGetValue(source, out object stored))
        {
            return;
        }

        Table.Remove(target);
        Table.Add(target, stored);
    }

    /// <summary>
    /// Gives every pair in a freshly built form that has NO location the supplied one,
    /// leaving pairs that already carry their own alone.
    /// <para>
    /// This is for a rewrite that INVENTS forms: Guile gives macro-introduced code the
    /// source of the macro use, and forms carried over from the original keep the
    /// locations they were read at.
    /// </para>
    /// </summary>
    /// <param name="form">The rewritten form.</param>
    /// <param name="location">The location to stamp, or <see langword="null"/> to do nothing.</param>
    public static void StampMissing(object form, SourceLocation location)
    {
        if (location == null)
        {
            return;
        }

        object cursor = form;
        while (cursor is Pair pair)
        {
            if (!Table.TryGetValue(pair, out object _))
            {
                Table.Add(pair, location);
            }

            StampMissing(pair.Car, location);
            cursor = pair.Cdr;
        }
    }

    /// <summary>
    /// Returns the location recorded for a datum, or <see langword="null"/>.
    /// <para>
    /// This answers only for a datum whose properties are still the reader's own record.
    /// Once Scheme has replaced them wholesale through <c>set-source-properties!</c>, the
    /// alist is the truth and this answers <see langword="null"/>.
    /// </para>
    /// </summary>
    /// <param name="datum">The datum to look up.</param>
    /// <returns>The location, or <see langword="null"/>.</returns>
    public static SourceLocation Located(object datum)
        => datum != null && Table.TryGetValue(datum, out object stored)
            ? stored as SourceLocation
            : null;
}
