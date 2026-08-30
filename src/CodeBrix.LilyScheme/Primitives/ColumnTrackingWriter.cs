// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text;
using CodeBrix.LilyScheme.Reader;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// A <see cref="TextWriter"/> that forwards everything to an inner writer while tracking
/// the line and column of what has passed through, so that <c>port-line</c> and
/// <c>port-column</c> answer for ordinary ports the way Guile's do.
/// <para>
/// Guile keeps the line and column IN THE PORT and advances them as characters pass, so
/// every port answers them — a console port and a file port as much as a string port.
/// Here the counters have to live on the WRITER rather than on
/// <see cref="SchemeOutputPort"/>, because <c>current-output-port</c> hands back a FRESH
/// port object on every call while the writer behind it is the shared thing; counters on
/// the port would restart at zero on each call and never accumulate.
/// </para>
/// <para>
/// This is load-bearing for <c>ice-9/pretty-print.scm</c>, whose whole line-breaking
/// decision is <c>(port-column port)</c>: its <c>indent</c> emits a newline when the
/// target column is behind the current one and spaces otherwise, and its <c>pp-list</c>
/// passes <c>(port-column port)</c> itself as the target. With the column stuck at zero,
/// <c>indent</c> takes neither branch — no newline, and <c>(spaces 0)</c> writes nothing —
/// so the separator between list items disappeared entirely and the form printed on one
/// unreadable line.
/// </para>
/// </summary>
public sealed class ColumnTrackingWriter : TextWriter
{
    private readonly TextWriter _inner;

    /// <summary>Initializes a tracking writer over an inner writer.</summary>
    /// <param name="inner">The writer receiving everything written here.</param>
    public ColumnTrackingWriter(TextWriter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>Gets the writer this one forwards to.</summary>
    public TextWriter Inner => _inner;

    /// <summary>Gets or sets the number of newlines written so far.</summary>
    /// <remarks><c>set-port-line!</c> assigns it; Guile's is a real setter, not a no-op.</remarks>
    public long Line { get; set; }

    /// <summary>Gets or sets the column position after the last write.</summary>
    /// <remarks><c>set-port-column!</c> assigns it; Guile's is a real setter, not a no-op.</remarks>
    public long Column { get; set; }

    /// <summary>Gets the inner writer's encoding.</summary>
    public override Encoding Encoding => _inner.Encoding;

    /// <summary>Gets or sets the line terminator, forwarding to the inner writer.</summary>
    public override string NewLine
    {
        get => _inner.NewLine;
        set => _inner.NewLine = value;
    }

    /// <summary>
    /// Returns the writer underneath any tracking wrapper, so a caller that needs the
    /// concrete sink — a <see cref="StringWriter"/> for <c>get-output-string</c>, say —
    /// still finds it.
    /// </summary>
    /// <param name="writer">The writer to unwrap.</param>
    /// <returns>The inner writer when <paramref name="writer"/> tracks, else itself.</returns>
    public static TextWriter Unwrap(TextWriter writer)
        => writer is ColumnTrackingWriter tracking ? tracking.Inner : writer;

    /// <summary>
    /// Wraps a writer for tracking unless it already tracks its own position.
    /// </summary>
    /// <param name="writer">The writer to wrap.</param>
    /// <returns>
    /// A tracking writer, or <paramref name="writer"/> itself when it is already a
    /// <see cref="ColumnTrackingWriter"/> or a <see cref="SoftPortWriter"/> (which keeps
    /// its own counters, deliberately updated on entry to the port rather than on flush).
    /// </returns>
    public static TextWriter Wrap(TextWriter writer)
    {
        if (writer == null || writer is ColumnTrackingWriter || writer is SoftPortWriter)
        {
            return writer;
        }

        return new ColumnTrackingWriter(writer);
    }

    /// <summary>Writes a character, tracking it.</summary>
    /// <param name="value">The character to write.</param>
    public override void Write(char value)
    {
        Track(stackalloc char[1] { value });
        _inner.Write(value);
    }

    /// <summary>Writes a string, tracking it.</summary>
    /// <param name="value">The string to write.</param>
    public override void Write(string value)
    {
        Track(value);
        _inner.Write(value);
    }

    /// <summary>Writes a range of a character array, tracking it.</summary>
    /// <param name="buffer">The array to write from.</param>
    /// <param name="index">The first index to write.</param>
    /// <param name="count">How many characters to write.</param>
    public override void Write(char[] buffer, int index, int count)
    {
        Track(buffer.AsSpan(index, count));
        _inner.Write(buffer, index, count);
    }

    /// <summary>Writes a span of characters, tracking it.</summary>
    /// <param name="buffer">The characters to write.</param>
    public override void Write(ReadOnlySpan<char> buffer)
    {
        Track(buffer);
        _inner.Write(buffer);
    }

    /// <summary>Flushes the inner writer.</summary>
    public override void Flush() => _inner.Flush();

    /// <summary>Releases the inner writer.</summary>
    /// <param name="disposing">Whether managed resources are being released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Track(ReadOnlySpan<char> text)
    {
        long line = Line;
        long column = Column;
        PortPosition.Advance(text, ref line, ref column);
        Line = line;
        Column = column;
    }
}
