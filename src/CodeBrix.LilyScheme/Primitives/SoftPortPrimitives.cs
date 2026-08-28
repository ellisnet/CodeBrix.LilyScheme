// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// A <see cref="TextWriter"/> that forwards everything written to a Scheme procedure,
/// backing the <c>(ice-9 soft-ports)</c> shim's output ports. Tracks the line and
/// column of what has passed through, so <c>port-line</c> and <c>port-column</c>
/// answer for soft ports the way they do for Guile's.
/// <para>
/// It is BLOCK-BUFFERED, as Guile's is, and the buffering is observable rather than an
/// internal detail: <c>ice-9/pretty-print.scm</c>'s truncating writer aborts to a prompt
/// from inside <c>write-string</c>, so WHEN the buffer flushes decides where that abort
/// lands — and an abort that lands inside Guile's procedure printer latches
/// <c>print_error</c> for the rest of the process, changing how every later procedure
/// prints. Writing straight through made the port latch earlier than the oracle and
/// differ on twenty-four entries of the generated manual.
/// </para>
/// </summary>
public sealed class SoftPortWriter : TextWriter
{
    /// <summary>
    /// The port's write-buffer size in BYTES, measured against the pinned oracle: 1023
    /// bytes stay buffered until the port is flushed and the 1024th sends the lot.
    /// </summary>
    private const int BufferCapacity = 1024;

    /// <summary>
    /// The unit a write larger than the remaining space is transferred in, measured the
    /// same way. Guile does not fill the buffer to the brim before flushing — with 700
    /// bytes buffered and 4000 more coming, the first flush carries 952 (700 + 252) and
    /// not 1024, and with 800 buffered it carries 800 (800 + 0) because a whole quantum
    /// no longer fits.
    /// </summary>
    private const int TransferQuantum = 252;

    private readonly Interpreter _interpreter;
    private readonly object _writeString;
    private readonly object _close;
    private readonly StringBuilder _buffer = new StringBuilder();
    private int _bufferedBytes;

    /// <summary>Initializes a writer over a Scheme <c>write-string</c> procedure.</summary>
    /// <param name="interpreter">The interpreter the procedures run in.</param>
    /// <param name="writeString">The procedure receiving each written string.</param>
    /// <param name="close">The procedure run when the port closes, or null.</param>
    public SoftPortWriter(Interpreter interpreter, object writeString, object close)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _writeString = writeString;
        _close = close;
    }

    /// <summary>Gets the number of newlines written so far.</summary>
    public long Line { get; private set; }

    /// <summary>Gets the column position after the last write.</summary>
    public long Column { get; private set; }

    /// <summary>Gets the encoding, nominally UTF-8; no transcoding happens here.</summary>
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Forwards one character to the <c>write-string</c> procedure.</summary>
    /// <param name="value">The character written.</param>
    public override void Write(char value) => ForwardText(value.ToString());

    /// <summary>Forwards a string to the <c>write-string</c> procedure.</summary>
    /// <param name="value">The string written.</param>
    public override void Write(string value) => ForwardText(value);

    /// <summary>Forwards a character range to the <c>write-string</c> procedure.</summary>
    /// <param name="buffer">The source characters.</param>
    /// <param name="index">The first index written.</param>
    /// <param name="count">The number of characters written.</param>
    public override void Write(char[] buffer, int index, int count)
        => ForwardText(new string(buffer, index, count));

    /// <summary>Forwards a character span to the <c>write-string</c> procedure.</summary>
    /// <param name="buffer">The characters written.</param>
    public override void Write(ReadOnlySpan<char> buffer) => ForwardText(new string(buffer));

    /// <summary>Runs the port's <c>close</c> procedure, when one was supplied.</summary>
    public void InvokeClose()
    {
        if (_close != null && !(_close is bool absent && !absent))
        {
            _interpreter.Evaluator.Apply(_close, Array.Empty<object>());
        }
    }

    /// <summary>
    /// Empties the buffer to the <c>write-string</c> procedure — Guile's port flush, and
    /// what <c>force-output</c>, <c>close</c> and <c>flush-all-ports</c> reach.
    /// </summary>
    public override void Flush()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        string text = _buffer.ToString();
        _buffer.Clear();
        _bufferedBytes = 0;

        // The procedure may abort to a prompt from inside this call -- pretty-print's
        // truncating writer does exactly that -- so the buffer is emptied BEFORE it runs.
        _interpreter.Evaluator.Apply(_writeString, new object[] { new MutableString(text) });
    }

    private void ForwardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Position tracking is done on entry to the PORT, not on flush: Guile advances a
        // port's line and column as data enters its buffer, and pretty-print's truncating
        // writer reads (port-line port) to decide whether a line has been broken.
        foreach (char c in text)
        {
            if (c == '\n')
            {
                Line++;
                Column = 0;
            }
            else
            {
                Column++;
            }
        }

        int index = 0;
        while (index < text.Length)
        {
            int available = BufferCapacity - _bufferedBytes;
            if (Utf8Length(text, index, text.Length - index) <= available)
            {
                Append(text, index, text.Length - index);
                index = text.Length;
                if (_bufferedBytes >= BufferCapacity)
                {
                    Flush();
                }

                break;
            }

            // The rest does not fit. Guile tops the buffer up by whole TRANSFER QUANTA
            // and flushes; it does NOT fill the buffer to the brim. Measured against the
            // pinned oracle over eleven starting fills: with n bytes already buffered the
            // first flush carries n plus the largest multiple of 252 that fits in the
            // remaining space, and every flush after that carries 1008 = 4 x 252.
            int take = available / TransferQuantum * TransferQuantum;
            int characters = CharactersFitting(text, index, take);
            Append(text, index, characters);
            index += characters;
            Flush();
        }
    }

    private void Append(string text, int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        _buffer.Append(text, start, count);
        _bufferedBytes += Utf8Length(text, start, count);
    }

    /// <summary>
    /// Returns how many CHARACTERS of a range encode to at most <paramref name="bytes"/>
    /// bytes. A character is never split across a flush: Guile encodes into the byte
    /// buffer and stops when the next character would not fit.
    /// </summary>
    private static int CharactersFitting(string text, int start, int bytes)
    {
        int used = 0;
        int index = start;
        while (index < text.Length)
        {
            int width = char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                ? Encoding.UTF8.GetByteCount(text.Substring(index, 2))
                : Encoding.UTF8.GetByteCount(text.Substring(index, 1));
            if (used + width > bytes)
            {
                break;
            }

            used += width;
            index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
        }

        return index - start;
    }

    private static int Utf8Length(string text, int start, int count)
        => count <= 0 ? 0 : Encoding.UTF8.GetByteCount(text.ToCharArray(start, count));
}

/// <summary>
/// The <c>(ice-9 soft-ports)</c> shim: Guile 3's keyword-form <c>make-soft-port</c>,
/// provided from C# because the upstream module is built on custom binary ports and
/// port internals that have no analogue here.
/// </summary>
public static class SoftPortPrimitives
{
    /// <summary>
    /// Installs the <c>(ice-9 soft-ports)</c> shim module.
    /// <para>
    /// Output soft ports work in full: every write reaches the <c>#:write-string</c>
    /// procedure, <c>port-line</c>/<c>port-column</c> track what passed through, and
    /// <c>close</c> runs the <c>#:close</c> procedure. The vendored pretty-print.scm
    /// builds its truncating writer on exactly this surface. Input soft ports
    /// (<c>#:read-string</c>) are refused loudly until something demands them.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to install the shim into.</param>
    public static void InstallShim(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        SchemeModule module = interpreter.Modules.Resolve(Pair.ListFrom(new object[]
        {
            Symbol.Intern("ice-9"), Symbol.Intern("soft-ports"),
        }));
        module.DefinePublic(
            Symbol.Intern("make-soft-port"),
            new Primitive("make-soft-port", 0, -1, a => MakeSoftPort(interpreter, a)));
    }

    private static object MakeSoftPort(Interpreter interpreter, object[] arguments)
    {
        object readString = null;
        object writeString = null;
        object close = null;
        for (int i = 0; i + 1 < arguments.Length; i += 2)
        {
            if (!(arguments[i] is Keyword keyword))
            {
                throw MakeSoftPortError("Wrong type (expecting keyword): ~S", arguments[i]);
            }

            switch (keyword.Name.Name)
            {
                case "id":
                case "input-waiting?":
                case "close-on-gc?":
                    // Accepted and unused: there is no port registry to carry an
                    // id, no input side to poll, and no GC hook to close from.
                    break;
                case "read-string":
                    readString = arguments[i + 1];
                    break;
                case "write-string":
                    writeString = arguments[i + 1];
                    break;
                case "close":
                    close = arguments[i + 1];
                    break;
                default:
                    throw MakeSoftPortError("Unrecognized keyword: ~S", arguments[i]);
            }
        }

        if (readString != null && !(readString is bool noRead && !noRead))
        {
            throw MakeSoftPortError(
                "input soft ports (#:read-string) are not supported here: ~S", readString);
        }

        if (writeString == null || (writeString is bool noWrite && !noWrite))
        {
            throw MakeSoftPortError(
                "Expected at least one of #:read-string, #:write-string: ~S", false);
        }

        return new SchemeOutputPort(new SoftPortWriter(interpreter, writeString, close));
    }

    private static SchemeThrow MakeSoftPortError(string message, object value)
        => new SchemeThrow(
            Symbol.Intern("misc-error"),
            Pair.List(
                new MutableString("make-soft-port"),
                new MutableString(message),
                Pair.List(value),
                false));
}
