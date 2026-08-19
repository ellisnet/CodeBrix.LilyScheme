// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// An input port over a string or file. Enough of Guile's port model to let psyntax
/// read source and to let <c>display</c> and <c>write</c> reach an output sink.
/// </summary>
public sealed class SchemeInputPort
{
    private readonly SchemeReader _reader;
    private readonly TextReader _stream;
    private readonly Stack<char> _pushback = new Stack<char>();

    /// <summary>Initializes an input port.</summary>
    /// <param name="text">The text to read from.</param>
    /// <param name="fileName">The name reported for this port.</param>
    public SchemeInputPort(string text, string fileName)
    {
        _reader = new SchemeReader(text, fileName);
        FileName = fileName;
    }

    /// <summary>
    /// Initializes an input port that STREAMS from a reader instead of holding the
    /// whole text up front — the shape a pipe (or the process's standard input) needs,
    /// where reading ahead to end-of-stream would block on a live producer.
    /// </summary>
    /// <param name="stream">The reader supplying characters as they become available.</param>
    /// <param name="fileName">The name reported for this port.</param>
    public SchemeInputPort(TextReader stream, string fileName)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        FileName = fileName;
    }

    /// <summary>
    /// Gets the reader behind a stream-backed port, or <see langword="null"/> for a
    /// string-backed one. <c>close-port</c> disposes it.
    /// </summary>
    public TextReader Stream => _stream;

    /// <summary>Gets the name reported for this port.</summary>
    public string FileName { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this port reads a FILE.
    /// <para>
    /// <see cref="FileName"/> alone cannot answer that: a string port carries a name too
    /// (Guile's own <c>&lt;string&gt;</c>), and <c>file-port?</c> must tell the two apart.
    /// </para>
    /// </summary>
    public bool IsFilePort { get; set; }

    /// <summary>Gets or sets a value indicating whether the port has been closed.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Reads the next datum from the port.</summary>
    /// <returns>The datum, or the end-of-file object.</returns>
    public object ReadDatum()
    {
        if (_stream != null)
        {
            // Refused loudly rather than half-served: the datum reader works over a
            // string, and buffering a live pipe to end-of-stream would block on the
            // producer. Nothing has demanded datum reads from a pipe yet.
            throw new SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(
                    new MutableString("read"),
                    new MutableString("Cannot read a datum from a stream-backed port: ~S"),
                    Pair.List(new MutableString(FileName ?? "<stream>")),
                    false));
        }

        return IsClosed ? EofObject.Instance : _reader.Read();
    }

    /// <summary>
    /// Reads every remaining character, backing <c>get-string-all</c>.
    /// <para>
    /// The cursor is the READER's, not a second one, so a port that has had data read
    /// from it hands back only what follows — mixing datum reads and character reads
    /// stays consistent, which is the property Guile's single port position gives.
    /// </para>
    /// </summary>
    /// <returns>The remaining text; empty when the port is at end of file or closed.</returns>
    public string ReadRemainingCharacters() => ReadCharacters(int.MaxValue);

    /// <summary>
    /// Reads up to <paramref name="count"/> characters, backing <c>get-string-n</c>.
    /// </summary>
    /// <param name="count">The maximum number of characters to read.</param>
    /// <returns>The characters read; empty when the port is at end of file or closed.</returns>
    public string ReadCharacters(int count)
    {
        if (IsClosed || count <= 0)
        {
            return string.Empty;
        }

        StringBuilder text = new StringBuilder();
        while (text.Length < count)
        {
            char? c = ReadCharacter();
            if (!c.HasValue)
            {
                break;
            }

            text.Append(c.Value);
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads ONE character, backing <c>read-char</c>, or returns
    /// <see langword="null"/> at end of file. A character pushed back with
    /// <see cref="PushbackCharacter"/> is delivered first.
    /// </summary>
    /// <returns>The character, or <see langword="null"/>.</returns>
    public char? ReadCharacter()
    {
        if (IsClosed)
        {
            return null;
        }

        if (_pushback.Count > 0)
        {
            return _pushback.Pop();
        }

        if (_stream != null)
        {
            int c = _stream.Read();
            return c < 0 ? (char?)null : (char)c;
        }

        return _reader.IsAtEnd ? (char?)null : _reader.ReadCharacterRaw();
    }

    /// <summary>
    /// Returns the next character WITHOUT consuming it, backing <c>peek-char</c>, or
    /// <see langword="null"/> at end of file.
    /// </summary>
    /// <returns>The character, or <see langword="null"/>.</returns>
    public char? PeekCharacter()
    {
        if (IsClosed)
        {
            return null;
        }

        if (_pushback.Count > 0)
        {
            return _pushback.Peek();
        }

        if (_stream != null)
        {
            int c = _stream.Peek();
            return c < 0 ? (char?)null : (char)c;
        }

        return _reader.IsAtEnd ? (char?)null : _reader.PeekCharacter();
    }

    /// <summary>
    /// Pushes a character back so the next read delivers it first, backing
    /// <c>unread-char</c>. Multiple pushbacks stack, most recent first, as Guile's
    /// <c>scm_ungetc</c> does.
    /// </summary>
    /// <param name="value">The character to push back.</param>
    public void PushbackCharacter(char value) => _pushback.Push(value);

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the port's file name.</returns>
    public override string ToString() => "#<input-port " + FileName + ">";
}

/// <summary>An output port accumulating text, backing <c>open-output-string</c>.</summary>
public sealed class SchemeOutputPort
{
    /// <summary>Initializes an output port over a writer.</summary>
    /// <param name="writer">The sink for written text.</param>
    public SchemeOutputPort(TextWriter writer)
    {
        Writer = writer;
    }

    /// <summary>Gets or sets the sink for written text.</summary>
    /// <remarks>
    /// SETTABLE for one reason: <c>set-port-encoding!</c>. Guile changes a port's codec
    /// in place, and .NET's writers bind their encoding at construction, so honouring
    /// that call means putting a differently-encoded writer behind the same port object.
    /// </remarks>
    public TextWriter Writer { get; set; }

    /// <summary>
    /// Gets or sets the file this port writes, or <see langword="null"/> when it is not a
    /// file port. <c>port-filename</c> answers it.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>Gets a value indicating whether this port writes a FILE.</summary>
    public bool IsFilePort => FileName != null;

    /// <summary>Gets or sets a value indicating whether the port has been closed.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;output-port&gt;</c>.</returns>
    public override string ToString() => "#<output-port>";
}

/// <summary>Output, reading and the load path.</summary>
public static class PortPrimitives
{
    /// <summary>Installs the port primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallOutput(interpreter);
        InstallInput(interpreter);
        InstallLoadPath(interpreter);
    }

    private static void InstallOutput(Interpreter interpreter)
    {
        // Both go through the program latch rather than straight to Printer, because the
        // latch has to still be SET while the text reaches the port: pretty-print writes
        // through a truncating soft port that aborts non-locally the moment a line is over
        // budget, and upstream leaves the latch set when that abort lands inside its
        // procedure printer. See Printer.WriteThroughProgramLatch.
        interpreter.DefinePrimitive("display", 1, 2, a =>
        {
            TextWriter writer = Writer(interpreter, a, 1);
            Printer.WriteThroughProgramLatch(a[0], false, writer.Write);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("write", 1, 2, a =>
        {
            TextWriter writer = Writer(interpreter, a, 1);
            Printer.WriteThroughProgramLatch(a[0], true, writer.Write);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("newline", 0, 1, a =>
        {
            Writer(interpreter, a, 0).Write('\n');
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("write-char", 1, 2, a =>
        {
            Writer(interpreter, a, 1).Write(TypeChecks.AsChar(a[0], "write-char", 1).ToString());
            return Unspecified.Instance;
        });

        // (put-string port string [start [count]]) -- R6RS textual output, with the
        // port FIRST, unlike display's optional trailing port. Guile defines it in C
        // beside the port machinery and (ice-9 textual-ports) merely re-exports it,
        // so it belongs in the core here too; the vendored pretty-print.scm writes
        // through it exclusively.
        interpreter.DefinePrimitive("put-string", 2, 4, a =>
        {
            if (!(a[0] is SchemeOutputPort port))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("put-string"),
                        new MutableString("Not an output port: ~S"),
                        Pair.List(a[0]),
                        false));
            }

            string text = StringPrimitives.Text(a[1], "put-string");
            int start = a.Length > 2 ? (int)SchemeNumber.ToBigInteger(a[2]) : 0;
            if (start < 0 || start > text.Length)
            {
                throw PutStringRangeError(a[2]);
            }

            int count = a.Length > 3 ? (int)SchemeNumber.ToBigInteger(a[3]) : text.Length - start;
            if (count < 0 || count > text.Length - start)
            {
                throw PutStringRangeError(a[3]);
            }

            port.Writer.Write(text.AsSpan(start, count));
            return Unspecified.Instance;
        });

        // libguile/rdelim.c's scm_write_line: "This function is equivalent to
        // (display obj [port]) (newline [port])" — DISPLAY, not write. It rendered
        // through Printer.Write for a time, which put quotes around every string it
        // wrote; RdelimTests caught it when (ice-9 rdelim) was vendored.
        interpreter.DefinePrimitive("write-line", 1, 2, a =>
        {
            TextWriter writer = Writer(interpreter, a, 1);
            writer.Write(Printer.Display(a[0]));
            writer.Write('\n');
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("force-output", 0, 1, a =>
        {
            Writer(interpreter, a, 0).Flush();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("object->string", 1, 2, a => new MutableString(Printer.Write(a[0])));

        // (simple-format dest fmt args...) with dest #f returning a string, #t writing
        // to the current output port -- Guile's convention.
        interpreter.DefinePrimitive("simple-format", 2, -1, a => SimpleFormat(interpreter, a));
        interpreter.DefinePrimitive("format", 2, -1, a => SimpleFormat(interpreter, a));

        interpreter.DefinePrimitive("current-output-port", 0, 0, a => new SchemeOutputPort(interpreter.OutputWriter));
        interpreter.DefinePrimitive("current-error-port", 0, 0, a => new SchemeOutputPort(interpreter.ErrorWriter));
        interpreter.DefinePrimitive("current-warning-port", 0, 0, a => new SchemeOutputPort(interpreter.ErrorWriter));
        interpreter.DefinePrimitive("set-current-warning-port", 1, 1, a => Unspecified.Instance);
        interpreter.DefinePrimitive("port?", 1, 1, a => a[0] is SchemeInputPort || a[0] is SchemeOutputPort);
        interpreter.DefinePrimitive("output-port?", 1, 1, a => a[0] is SchemeOutputPort);
        interpreter.DefinePrimitive("input-port?", 1, 1, a => a[0] is SchemeInputPort);
    }

    private static object SimpleFormat(Interpreter interpreter, object[] arguments)
    {
        object destination = arguments[0];
        string template = arguments[1] is MutableString text
            ? text.ToString()
            : Printer.Display(arguments[1]);

        StringBuilder builder = new StringBuilder();
        int argumentIndex = 2;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c != '~' || i + 1 >= template.Length)
            {
                builder.Append(c);
                continue;
            }

            char directive = char.ToLowerInvariant(template[++i]);
            switch (directive)
            {
                case 'a':
                    builder.Append(argumentIndex < arguments.Length
                        ? Printer.Display(arguments[argumentIndex++])
                        : string.Empty);
                    break;
                case 's':
                    builder.Append(argumentIndex < arguments.Length
                        ? Printer.Write(arguments[argumentIndex++])
                        : string.Empty);
                    break;
                case '%':
                case 'n':
                    builder.Append('\n');
                    break;
                case '~':
                    builder.Append('~');
                    break;
                default:
                    builder.Append('~').Append(template[i]);
                    break;
            }
        }

        string result = builder.ToString();
        if (destination is bool flag)
        {
            if (!flag)
            {
                return new MutableString(result);
            }

            interpreter.OutputWriter.Write(result);
            return Unspecified.Instance;
        }

        if (destination is SchemeOutputPort port)
        {
            port.Writer.Write(result);
            return Unspecified.Instance;
        }

        interpreter.OutputWriter.Write(result);
        return Unspecified.Instance;
    }

    private static void InstallInput(Interpreter interpreter)
    {
        // Guile's signature is
        //   (open-input-file file #:key (binary #f) (encoding #f) (guess-encoding #f))
        // (ice-9/ports.scm). The encoding keyword is load-bearing rather than decorative:
        // lily-library.scm's gulp-file-with-encoding is the ONLY way LilyPond reads a
        // file into a string, and ly:gulp-file asks it for LATIN-1 while
        // ly:gulp-file-utf8 asks for UTF-8 — decoding both as UTF-8 would corrupt every
        // byte above 0x7F in an EPS or PostScript file. #:binary selects Guile's "rb"
        // mode, which for a port that only ever yields characters here means Latin-1:
        // the one encoding whose bytes and characters correspond one-to-one.
        interpreter.DefinePrimitive("open-input-file", 1, -1, a =>
        {
            string path = StringPrimitives.Text(a[0], "open-input-file");
            return OpenInputFile(path, a, 1, "open-input-file");
        });

        // The counterpart an earlier fix recorded as the ONE unbound name it left behind:
        // it is the next layer in the very file whose mkdir that fix added. A StreamWriter
        // is handed straight to SchemeOutputPort, so write/display/newline reach the file
        // through the same path they reach any other port.
        // Guile's signature is
        //   (open-output-file file #:key (binary #f) (encoding #f))
        // (ice-9/ports.scm:423), the mirror of open-input-file below. LilyPond's
        // documentation generator opens every one of its nineteen outputs as
        // (open-output-file "internals.texi" #:encoding "UTF-8"), so refusing the
        // keyword is not a missing nicety — it is the whole file.
        //
        // The open ports are REMEMBERED, because a buffered writer that is never closed
        // loses whatever is still in its buffer. Scheme code is entitled not to close
        // them: Guile flushes every open port on the way out (libguile/init.c:332 calls
        // scm_flush_all_ports), and LilyPond's documentation generator relies on exactly
        // that — it opens nineteen files, displays to them and returns. Without the
        // registry and the flush-all-ports below, each file ends at a buffer boundary:
        // the observable is a set of outputs whose sizes are all multiples of 1024 and
        // an internals.texi of zero bytes.
        List<SchemeOutputPort> openFilePorts = new List<SchemeOutputPort>();

        interpreter.DefinePrimitive("open-output-file", 1, -1, a =>
        {
            string path = StringPrimitives.Text(a[0], "open-output-file");
            SchemeOutputPort port = OpenOutputFile(path, a, 1, "open-output-file");
            openFilePorts.Add(port);
            return port;
        });

        // (open-file filename mode) -- libguile/fports.c's scm_open_file. This is the
        // MODE-STRING form, and it is a different procedure from the two keyword-form
        // openers above rather than a spelling of them: scm/backend-library.scm reaches
        // for it four times (writing a header field, and the EPS/PostScript byte copies
        // that ask for "rb" and "wb"), and scm/framework-ps.scm twice more.
        interpreter.DefinePrimitive("open-file", 2, 2, a =>
        {
            string path = StringPrimitives.Text(a[0], "open-file");
            string mode = StringPrimitives.Text(a[1], "open-file");
            object port = OpenFileByMode(path, mode);
            if (port is SchemeOutputPort output)
            {
                openFilePorts.Add(output);
            }

            return port;
        });

        // (flush-all-ports) -- libguile/ports.c:4118. Guile calls this itself at exit;
        // a host embedding the interpreter has no exit to hang it on, so the name is
        // bound for whoever owns the run to call when the run is over.
        interpreter.DefinePrimitive("flush-all-ports", 0, 0, a =>
        {
            foreach (SchemeOutputPort port in openFilePorts)
            {
                // A port Scheme already closed is not flushed again -- its writer has been
                // disposed, and Guile's scm_flush_all_ports likewise walks only OPEN ports.
                if (!port.IsClosed)
                {
                    port.Writer.Flush();
                }
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("open-input-string", 1, 1, a =>
            new SchemeInputPort(StringPrimitives.Text(a[0], "open-input-string"), "<string>"));

        // (call-with-port port proc) -- ice-9/ports.scm. Guile threads MULTIPLE VALUES
        // back out through call-with-values; a single value is the only case reached
        // here, and returning it directly is that same behaviour for one value.
        interpreter.DefinePrimitive("call-with-port", 2, 2, a =>
        {
            try
            {
                return interpreter.Evaluator.Apply(a[1], new[] { a[0] });
            }
            finally
            {
                ClosePort(a[0]);
            }
        });

        // (call-with-input-file file proc #:key binary encoding guess-encoding)
        // -- ice-9/ports.scm, which opens the file and hands it to call-with-port.
        // This is how EVERY file LilyPond reads into a string is read: ly:gulp-file and
        // ly:gulp-file-utf8 both go through lily-library.scm's gulp-file-with-encoding,
        // which calls it with #:encoding. \markup \epsfile is the first caller the port
        // reaches, by way of \image reading the EPS to find its bounding box.
        interpreter.DefinePrimitive("call-with-input-file", 2, -1, a =>
        {
            string path = StringPrimitives.Text(a[0], "call-with-input-file");
            SchemeInputPort port = OpenInputFile(path, a, 2, "call-with-input-file");
            try
            {
                return interpreter.Evaluator.Apply(a[1], new object[] { port });
            }
            finally
            {
                port.IsClosed = true;
            }
        });

        // (get-string-all port) and (get-string-n port count) -- (ice-9 textual-ports),
        // which lily-library.scm imports. They live core-side here for the same reason
        // open-input-file and close-port do: LilyScheme's scope is deliberately WIDER
        // than Guile's per-module scope and never narrower.
        //
        // Guile's get-string-all is (read-string port), and read-string returns the
        // EMPTY STRING at end of file, never the eof object -- R6RS's get-string-all
        // does return eof there, and (ice-9 textual-ports) is the one LilyPond imports.
        // get-string-n is the other way round: it answers the eof object when it could
        // not read a single character, and a SHORT string when it read some but fewer
        // than asked.
        interpreter.DefinePrimitive("get-string-all", 1, 1, a =>
            new MutableString(InputPort(a[0], "get-string-all").ReadRemainingCharacters()));

        interpreter.DefinePrimitive("get-string-n", 2, 2, a =>
        {
            string text = InputPort(a[0], "get-string-n")
                .ReadCharacters((int)SchemeNumber.ToBigInteger(a[1]));
            return text.Length == 0 ? (object)EofObject.Instance : new MutableString(text);
        });

        // read-char and peek-char, which are ordinary R5RS and were simply absent.
        // font-name-add-files.ly is the corpus's demand for them: its base64 decoder
        // walks a string port a character at a time.
        interpreter.DefinePrimitive("read-char", 0, 1, a =>
        {
            char? c = a.Length > 0 && a[0] is SchemeInputPort port
                ? port.ReadCharacter()
                : null;
            return c.HasValue ? (object)SchemeChar.Get(c.Value) : EofObject.Instance;
        });

        interpreter.DefinePrimitive("peek-char", 0, 1, a =>
        {
            char? c = a.Length > 0 && a[0] is SchemeInputPort port
                ? port.PeekCharacter()
                : null;
            return c.HasValue ? (object)SchemeChar.Get(c.Value) : EofObject.Instance;
        });

        // The current input port and the rdelim builtins. (ice-9 rdelim) is vendored
        // VERBATIM and is pure Scheme over three C-side names — %read-line,
        // %read-delimited! and %init-rdelim-builtins (libguile/rdelim.c) — plus
        // unread-char for its 'peek handle-delim. The contracts below are that file's.
        SchemeInputPort currentInput = null;
        Func<SchemeInputPort> currentInputPort = () =>
        {
            if (currentInput == null || !ReferenceEquals(currentInput.Stream, interpreter.InputReader))
            {
                currentInput = new SchemeInputPort(interpreter.InputReader, "<stdin>");
            }

            return currentInput;
        };

        interpreter.DefinePrimitive("current-input-port", 0, 0, a => currentInputPort());

        interpreter.DefinePrimitive("unread-char", 1, 2, a =>
        {
            SchemeInputPort port = a.Length > 1
                ? InputPort(a[1], "unread-char")
                : currentInputPort();
            char c = (char)TypeChecks.AsChar(a[0], "unread-char", 1).CodePoint;
            port.PushbackCharacter(c);
            return a[0];
        });

        interpreter.DefinePrimitive("%init-rdelim-builtins", 0, 0, a => Unspecified.Instance);

        // (line . delimiter): the newline is removed; the delimiter is the newline
        // character or the eof-object; at end of file the pair is (#<eof> . #<eof>).
        interpreter.DefinePrimitive("%read-line", 0, 1, a =>
        {
            SchemeInputPort port = a.Length > 0
                ? InputPort(a[0], "%read-line")
                : currentInputPort();
            StringBuilder line = new StringBuilder();
            while (true)
            {
                char? c = port.ReadCharacter();
                if (!c.HasValue)
                {
                    return line.Length == 0
                        ? new Pair(EofObject.Instance, EofObject.Instance)
                        : new Pair(new MutableString(line.ToString()), EofObject.Instance);
                }

                if (c.Value == '\n')
                {
                    return new Pair(new MutableString(line.ToString()), SchemeChar.Get('\n'));
                }

                line.Append(c.Value);
            }
        });

        // (terminator . nchars): terminator is the delimiter character (consumed when
        // gobble, pushed back otherwise), the eof-object at end of file, or #f when the
        // buffer range filled without meeting either.
        interpreter.DefinePrimitive("%read-delimited!", 3, 6, a =>
        {
            string delims = StringPrimitives.Text(a[0], "%read-delimited!");
            MutableString buffer = a[1] as MutableString;
            if (buffer == null)
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("%read-delimited!"),
                        new MutableString("Not a mutable string: ~S"),
                        Pair.List(a[1]),
                        false));
            }

            bool gobble = !(a[2] is bool g) || g;
            SchemeInputPort port = a.Length > 3 && a[3] is SchemeInputPort given
                ? given
                : currentInputPort();
            int start = a.Length > 4 ? (int)SchemeNumber.ToBigInteger(a[4]) : 0;
            int end = a.Length > 5 ? (int)SchemeNumber.ToBigInteger(a[5]) : buffer.Length;

            int stored = 0;
            while (start + stored < end)
            {
                char? c = port.ReadCharacter();
                if (!c.HasValue)
                {
                    return new Pair(EofObject.Instance, (long)stored);
                }

                if (delims.IndexOf(c.Value) >= 0)
                {
                    if (!gobble)
                    {
                        port.PushbackCharacter(c.Value);
                    }

                    return new Pair(SchemeChar.Get(c.Value), (long)stored);
                }

                buffer[start + stored] = c.Value;
                stored++;
            }

            return new Pair(false, (long)stored);
        });

        interpreter.DefinePrimitive("read", 0, 1, a =>
            a.Length > 0 && a[0] is SchemeInputPort port ? port.ReadDatum() : EofObject.Instance);

        // read-syntax yields plain data here; psyntax attaches its own wraps, and we do
        // not yet thread source locations through the reader.
        interpreter.DefinePrimitive("read-syntax", 0, 1, a =>
            a.Length > 0 && a[0] is SchemeInputPort port ? port.ReadDatum() : EofObject.Instance);

        // close-port is Guile's ANY-port close (libguile/ports.c's scm_close_port), so it
        // goes through the same helper `close' does. Handling only the input side left an
        // output file port unflushed: scm/backend-library.scm's output-scope opens a
        // header field's file, displays to it and closes it, and with the close a no-op
        // whatever was still in the writer's buffer never reached the disk.
        interpreter.DefinePrimitive("close-port", 1, 1, a =>
        {
            ClosePort(a[0]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("close-input-port", 1, 1, a =>
        {
            if (a[0] is SchemeInputPort port)
            {
                port.IsClosed = true;
            }

            return Unspecified.Instance;
        });

        // (close port) -- Guile's close works on any port. close-port above only
        // handles input; pretty-print's truncating writer closes its soft OUTPUT
        // port through this one, which is when the port's #:close procedure runs.
        interpreter.DefinePrimitive("close", 1, 1, a =>
        {
            switch (a[0])
            {
                case SchemeInputPort:
                case SchemeOutputPort:
                    ClosePort(a[0]);
                    return true;
                default:
                    throw new SchemeThrow(
                        Symbol.Intern("wrong-type-arg"),
                        Pair.List(
                            new MutableString("close"),
                            new MutableString("Not a port: ~S"),
                            Pair.List(a[0]),
                            false));
            }
        });

        interpreter.DefinePrimitive("file-encoding", 1, 1, a => false);

        //was previously: a no-op that accepted the call and changed nothing.
        //
        // THAT MADE IT IMPOSSIBLE TO WRITE BYTES. Scheme code that produces binary
        // output does it by setting an 8-bit codec and writing one character per byte —
        // font-name-add-files.ly decodes two base64 OpenType fonts that way,
        // (set-port-encoding! port "ISO-8859-1") and then write-char per octet. Under a
        // UTF-8 writer every octet above 0x7F comes out as TWO bytes, so the file on
        // disk is not the font: it is the font's mojibake, and nothing downstream can
        // read it. The port silently produced one for the life of the sweep.
        //
        // The writer is REPLACED rather than reconfigured, because .NET binds a writer's
        // encoding at construction. Appending rather than truncating keeps whatever the
        // caller already wrote — Guile changes the codec of a live port and does not
        // discard its file.
        interpreter.DefinePrimitive("set-port-encoding!", 2, 2, a =>
        {
            if (a[0] is SchemeOutputPort output && output.FileName != null && !output.IsClosed)
            {
                Encoding encoding = WithoutPreamble(
                    SchemeBootstrap.ResolveEncoding(
                        StringPrimitives.Text(a[1], "set-port-encoding!")));

                output.Writer.Flush();
                output.Writer.Dispose();
                output.Writer = new StreamWriter(output.FileName, true, encoding);
            }

            return Unspecified.Instance;
        });

        // Answers for an OUTPUT file port too. scm/graphviz.scm's graph-write gates a
        // (port-filename out) on (file-port? out), so the two have to agree about the
        // same port -- answering #f here for a port file-port? called a file would put
        // "Writing graph to `#f'" in the message.
        interpreter.DefinePrimitive("port-filename", 1, 1, a =>
        {
            switch (a[0])
            {
                case SchemeInputPort input:
                    return new MutableString(input.FileName);
                case SchemeOutputPort output when output.IsFilePort:
                    return new MutableString(output.FileName);
                default:
                    return false;
            }
        });

        // (file-port? obj) -- libguile/fports.c's scm_file_port_p, which asks whether the
        // port's implementation is the FILE one, not whether it has a name. A string port
        // and the current error port are ports and are not file ports; scm/graphviz.scm
        // relies on exactly that distinction, because its regression test writes the graph
        // to (current-error-port).
        interpreter.DefinePrimitive("file-port?", 1, 1, a => a[0] switch
        {
            SchemeInputPort input => input.IsFilePort,
            SchemeOutputPort output => output.IsFilePort,
            _ => false,
        });
    }

    /// <summary>
    /// Answers the same encoding without a byte-order mark.
    /// <para>
    /// <c>Encoding.UTF8</c> is a <c>UTF8Encoding</c> built to EMIT the BOM, and
    /// <c>StreamWriter</c> writes an encoding's preamble at the head of a new file.
    /// Guile writes no BOM, so a port asked for "UTF-8" that produced one would put
    /// three bytes in front of every generated file — invisible in a text editor, and
    /// a byte difference on the first line of all nineteen.
    /// </para>
    /// </summary>
    /// <param name="encoding">The encoding asked for.</param>
    /// <returns>An encoding that writes the same bytes with no preamble.</returns>
    private static Encoding WithoutPreamble(Encoding encoding)
        => encoding is UTF8Encoding && encoding.GetPreamble().Length > 0
            ? new UTF8Encoding(false)
            : encoding;

    /// <summary>
    /// Opens a file as an output port, reading Guile's <c>#:binary</c> and
    /// <c>#:encoding</c> keywords off the tail of the argument list.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="arguments">The whole primitive argument vector.</param>
    /// <param name="firstKeyword">The index the keyword pairs start at.</param>
    /// <param name="procedureName">The name to report in errors.</param>
    /// <returns>An output port over the encoded file.</returns>
    private static SchemeOutputPort OpenOutputFile(
        string path, object[] arguments, int firstKeyword, string procedureName)
    {
        // NO byte-order mark. StreamWriter's own UTF-8 default writes one, and a BOM at
        // the head of a generated Texinfo file is three bytes the oracle does not emit.
        Encoding encoding = new UTF8Encoding(false);
        for (int i = firstKeyword; i + 1 < arguments.Length; i += 2)
        {
            if (!(arguments[i] is Keyword keyword))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString(procedureName),
                        new MutableString("Wrong type (expecting keyword): ~S"),
                        Pair.List(arguments[i]),
                        false));
            }

            switch (keyword.Name.Name)
            {
                case "encoding":
                    if (arguments[i + 1] is MutableString name)
                    {
                        encoding = WithoutPreamble(
                            SchemeBootstrap.ResolveEncoding(name.ToString()));
                    }

                    break;
                case "binary":
                    // Guile's "wb" mode, the counterpart of the reader's "rb": one
                    // character out is one byte on disk.
                    if (Evaluator.IsTrue(arguments[i + 1]))
                    {
                        encoding = Encoding.Latin1;
                    }

                    break;
                default:
                    throw new SchemeThrow(
                        Symbol.Intern("wrong-type-arg"),
                        Pair.List(
                            new MutableString(procedureName),
                            new MutableString("Unrecognized keyword: ~S"),
                            Pair.List(arguments[i]),
                            false));
            }
        }

        return new SchemeOutputPort(new StreamWriter(path, false, encoding)) { FileName = path };
    }

    /// <summary>
    /// Opens a file as a port from Guile's MODE STRING — <c>scm_open_file</c>
    /// (libguile/fports.c), the form <c>open-file</c> takes.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="mode">The mode string, e.g. <c>"r"</c>, <c>"w"</c>, <c>"a"</c>, <c>"rb"</c>.</param>
    /// <returns>An input port for a reading mode, an output port for a writing one.</returns>
    private static object OpenFileByMode(string path, string mode)
    {
        // libguile/fports.c reads the mode one character at a time: r/w/a select the
        // direction, '+' adds the other one, and b/0/l/e are flags. Only the direction and
        // 'b' can be honoured here -- a port is either a reader or a writer in this model,
        // and the buffering flags describe a file descriptor there is none of.
        bool read = mode.IndexOf('r') >= 0;
        bool write = mode.IndexOf('w') >= 0;
        bool append = mode.IndexOf('a') >= 0;
        bool binary = mode.IndexOf('b') >= 0;

        if (mode.IndexOf('+') >= 0)
        {
            // Refused LOUDLY rather than silently answering half of what was asked for.
            throw new SchemeThrow(
                Symbol.Intern("misc-error"),
                Pair.List(
                    new MutableString("open-file"),
                    new MutableString("Read/write ports are not modelled: ~S"),
                    Pair.List(new MutableString(mode)),
                    false));
        }

        if (!read && !write && !append)
        {
            throw new SchemeThrow(
                Symbol.Intern("misc-error"),
                Pair.List(
                    new MutableString("open-file"),
                    new MutableString("No direction in mode string: ~S"),
                    Pair.List(new MutableString(mode)),
                    false));
        }

        // Guile's "b" is the one encoding whose bytes and characters correspond one to
        // one, which is the same reading #:binary gets on the keyword form.
        Encoding encoding = binary ? Encoding.Latin1 : new UTF8Encoding(false);

        if (read)
        {
            return new SchemeInputPort(encoding.GetString(HostFile.ReadAllBytes(path)), path)
            {
                IsFilePort = true,
            };
        }

        return new SchemeOutputPort(new StreamWriter(path, append, encoding)) { FileName = path };
    }

    /// <summary>
    /// Opens a file as an input port, reading Guile's <c>#:binary</c>,
    /// <c>#:encoding</c> and <c>#:guess-encoding</c> keywords off the tail of the
    /// argument list.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="arguments">The whole primitive argument vector.</param>
    /// <param name="firstKeyword">The index the keyword pairs start at.</param>
    /// <param name="procedureName">The name to report in errors.</param>
    /// <returns>An input port over the decoded text.</returns>
    private static SchemeInputPort OpenInputFile(
        string path, object[] arguments, int firstKeyword, string procedureName)
    {
        Encoding encoding = Encoding.UTF8;
        for (int i = firstKeyword; i + 1 < arguments.Length; i += 2)
        {
            if (!(arguments[i] is Keyword keyword))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString(procedureName),
                        new MutableString("Wrong type (expecting keyword): ~S"),
                        Pair.List(arguments[i]),
                        false));
            }

            switch (keyword.Name.Name)
            {
                case "encoding":
                    if (arguments[i + 1] is MutableString name)
                    {
                        encoding = SchemeBootstrap.ResolveEncoding(name.ToString());
                    }

                    break;
                case "binary":
                    // Guile's "rb" mode yields a port whose characters ARE its bytes.
                    if (Evaluator.IsTrue(arguments[i + 1]))
                    {
                        encoding = Encoding.Latin1;
                    }

                    break;
                case "guess-encoding":
                    // Guile sniffs a coding: declaration in the first two lines. Nothing
                    // LilyPond reads carries one, and guessing wrongly is worse than the
                    // declared default, so the keyword is accepted and ignored.
                    break;
                default:
                    throw new SchemeThrow(
                        Symbol.Intern("wrong-type-arg"),
                        Pair.List(
                            new MutableString(procedureName),
                            new MutableString("Unrecognized keyword: ~S"),
                            Pair.List(arguments[i]),
                            false));
            }
        }

        // Read the BYTES and decode them, rather than letting File.ReadAllText sniff a
        // byte-order mark and override the encoding the caller asked for.
        return new SchemeInputPort(encoding.GetString(HostFile.ReadAllBytes(path)), path)
        {
            IsFilePort = true,
        };
    }

    private static SchemeInputPort InputPort(object value, string procedureName)
    {
        if (value is SchemeInputPort port)
        {
            return port;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Not an input port: ~S"),
                Pair.List(value),
                false));
    }

    private static void ClosePort(object value)
    {
        switch (value)
        {
            case SchemeInputPort input:
                input.IsClosed = true;
                if (input.Stream != null)
                {
                    input.Stream.Dispose();
                }

                break;
            case SchemeOutputPort output:
                if (output.IsClosed)
                {
                    break;
                }

                output.Writer.Flush();
                if (output.Writer is SoftPortWriter soft)
                {
                    soft.InvokeClose();
                }

                output.IsClosed = true;

                // Only a FILE port's writer is disposed. Guile's close-port releases the
                // file descriptor, and a StreamWriter that is merely flushed holds the
                // file open for the rest of the process -- but the same call reaches the
                // current output and error ports, whose writers belong to the HOST and
                // must survive being closed from Scheme.
                if (output.IsFilePort)
                {
                    output.Writer.Dispose();
                }

                break;
        }
    }

    private static void InstallLoadPath(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("%search-load-path", 1, 1, a =>
        {
            string name = StringPrimitives.Text(a[0], "%search-load-path");
            foreach (string directory in interpreter.LoadPath)
            {
                foreach (string suffix in new[] { string.Empty, ".scm" })
                {
                    string candidate = Path.Combine(directory, name + suffix);
                    if (File.Exists(candidate))
                    {
                        return new MutableString(candidate);
                    }
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("absolute-file-name?", 1, 1, a =>
            Path.IsPathRooted(StringPrimitives.Text(a[0], "absolute-file-name?")));

        interpreter.DefinePrimitive("canonicalize-path", 1, 1, a =>
            new MutableString(Path.GetFullPath(StringPrimitives.Text(a[0], "canonicalize-path"))));

        interpreter.DefinePrimitive("dirname", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "dirname");
            string directory = Path.GetDirectoryName(path);
            return new MutableString(string.IsNullOrEmpty(directory) ? "." : directory);
        });

        interpreter.DefinePrimitive("basename", 1, 1, a =>
            new MutableString(Path.GetFileName(StringPrimitives.Text(a[0], "basename"))));

        interpreter.DefinePrimitive("in-vicinity", 2, 2, a =>
        {
            string directory = StringPrimitives.Text(a[0], "in-vicinity");
            string name = StringPrimitives.Text(a[1], "in-vicinity");
            return new MutableString(string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name));
        });

        interpreter.DefinePrimitive("getcwd", 0, 0, a => new MutableString(Directory.GetCurrentDirectory()));

        interpreter.DefinePrimitive("load", 1, 1, a =>
            interpreter.LoadFile(StringPrimitives.Text(a[0], "load")));

        interpreter.DefinePrimitive("primitive-load", 1, 1, a =>
            interpreter.LoadFile(StringPrimitives.Text(a[0], "primitive-load")));

        interpreter.DefinePrimitive("primitive-load-path", 1, 2, a =>
        {
            string name = StringPrimitives.Text(a[0], "primitive-load-path");
            foreach (string directory in interpreter.LoadPath)
            {
                foreach (string extension in LoadExtensionsFor(name))
                {
                    string candidate = Path.Combine(directory, name + extension);
                    if (File.Exists(candidate))
                    {
                        return interpreter.LoadFile(candidate);
                    }
                }
            }

            return false;
        });
    }

    /// <summary>
    /// Guile's <c>%load-extensions</c>, applied the way <c>search_path</c> applies it
    /// (<c>libguile/load.c</c>): the list is <c>(".scm" "")</c>, but a name that ALREADY
    /// ends in one of those extensions is searched for as it stands and nothing is
    /// appended. Without that second half, <c>(load-from-path
    /// "lily/documentation-generate.scm")</c> — the form LilyPond's own
    /// <c>generate-documentation.ly</c> uses — looks for a file named
    /// <c>…generate-documentation.scm.scm</c> and quietly finds nothing.
    /// </summary>
    private static IEnumerable<string> LoadExtensionsFor(string name)
    {
        if (name.EndsWith(".scm", StringComparison.Ordinal))
        {
            yield return string.Empty;
            yield break;
        }

        yield return ".scm";
        yield return string.Empty;
    }

    private static SchemeThrow PutStringRangeError(object value)
        => new SchemeThrow(
            Symbol.Intern("out-of-range"),
            Pair.List(
                new MutableString("put-string"),
                new MutableString("Argument out of range: ~S"),
                Pair.List(value),
                false));

    private static TextWriter Writer(Interpreter interpreter, object[] arguments, int portIndex)
    {
        if (arguments.Length > portIndex && arguments[portIndex] is SchemeOutputPort port)
        {
            return port.Writer;
        }

        return interpreter.OutputWriter;
    }
}
