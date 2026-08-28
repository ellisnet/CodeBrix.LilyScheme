// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>Renders Scheme values in their external representation.</summary>
public static class Printer
{
    /// <summary>Writes a value using <c>write</c> conventions, with strings quoted.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The external representation.</returns>
    public static string Write(object value) => Render(value, true);

    /// <summary>Writes a value using <c>display</c> conventions, with strings unquoted.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The displayed representation.</returns>
    public static string Display(object value) => Render(value, false);

    /// <summary>
    /// Renders host text as a Scheme string LITERAL -- the surrounding quotes and every
    /// escape included -- for splicing into Scheme source a reader is about to read.
    /// <para>
    /// THIS IS WHAT A FILESYSTEM PATH MUST GO THROUGH. A Windows path spliced in raw is
    /// not the path it names: the reader implements Guile's fixed-width hex escapes
    /// (libguile/read.c's SCM_READ_HEX_ESCAPE), so <c>C:\Users\me</c> reaches <c>\U</c>,
    /// takes the next six characters as hex digits and fails on the 's' of Users.
    /// </para>
    /// <para>
    /// The loud failure is the LUCKY case. A path component beginning with a, b, f, n,
    /// r, t or v spells a VALID escape -- <c>\temp</c> is a tab -- so the source reads
    /// without a diagnostic and names a different file. Which happens depends on the
    /// directory names, which is why this surfaces as an intermittent fault rather than
    /// a constant one, and why hand-doubling backslashes at the call site is not enough:
    /// a quote in a path needs escaping too.
    /// </para>
    /// <para>
    /// Guile on Windows reads paths exactly this way, so the reader is not the thing to
    /// change; the host is. Nothing here is Windows-only -- on Linux and macOS a path
    /// simply has no backslashes to escape and the result is the input in quotes.
    /// </para>
    /// </summary>
    /// <param name="value">The host text to render.</param>
    /// <returns>A Scheme string literal that reads back as <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static string WriteString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return "\"" + Escape(value) + "\"";
    }

    // Upstream: the `static int print_error' of libguile/programs.c's
    // scm_i_program_print. It is a re-entry guard -- set before calling out to the Scheme
    // printer, cleared after -- and it is deliberately NOT cleared on a non-local exit,
    // which is the whole of the behaviour reproduced here. See WriteThroughProgramLatch.
    private static bool _programPrintLatched;

    /// <summary>
    /// Renders <paramref name="value"/> and hands the text to <paramref name="emit"/>,
    /// reproducing Guile's program-print re-entry latch.
    /// <para>
    /// <c>scm_i_program_print</c> (<c>libguile/programs.c:108-143</c>) sets a file-static
    /// <c>print_error</c> flag, calls <c>(system vm program)</c>'s <c>write-program</c> to
    /// do the printing, and clears the flag afterwards; while the flag is set — or when
    /// the module cannot be resolved — it prints the low-level
    /// <c>#&lt;program ADDR CODE&gt;</c> form instead. The guard exists so that an error
    /// INSIDE the Scheme printer cannot recurse forever.
    /// </para>
    /// <para>
    /// ⚠ It also never recovers. <c>pretty-print</c> writes through a truncating soft port
    /// that <c>abort-to-prompt</c>s the moment the output exceeds the line budget
    /// (<c>ice-9/pretty-print.scm:29-50</c>), and when that abort lands inside
    /// <c>write-program</c> the flag is left SET for the rest of the process: from then on
    /// EVERY procedure prints as <c>#&lt;program ADDR CODE&gt;</c>. LilyPond knows —
    /// <c>scm-&gt;string</c> (<c>scm/lily-library.scm:1628-1629</c>) carries a regex whose
    /// only purpose is to normalise that form — and the generated manual shows it 206
    /// times against 29 ordinary ones. Reproducing the latch is what makes those 206 agree
    /// (standing rule 2: upstream's own defects are reproduced, not corrected).
    /// </para>
    /// <para>
    /// The latch is held across the EMIT, not just the render, because that is where a
    /// truncating port can abort — the render itself builds a string and cannot.
    /// </para>
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <param name="quoteStrings">Whether to use <c>write</c> conventions.</param>
    /// <param name="emit">Receives the rendered text; may exit non-locally.</param>
    public static void WriteThroughProgramLatch(object value, bool quoteStrings, Action<string> emit)
    {
        if (!(value is Procedure))
        {
            emit(Render(value, quoteStrings));
            return;
        }

        if (_programPrintLatched)
        {
            emit(ProgramFallback(value));
            return;
        }

        string text = Render(value, quoteStrings);
        _programPrintLatched = true;
        emit(text);

        // Deliberately NOT a finally: an emit that exits non-locally must leave the latch
        // set, which is precisely the upstream behaviour being reproduced.
        _programPrintLatched = false;
    }

    /// <summary>
    /// Clears the program-print latch.
    /// <para>
    /// Upstream's flag is a process-global C static, and upstream runs one input file per
    /// process — so "for the rest of the process" and "for the rest of this file" are the
    /// same statement there. The port shares one process across a whole sweep, so the
    /// faithful reset point is the per-file boundary, and the host calls this there.
    /// </para>
    /// </summary>
    public static void ResetProgramPrintLatch() => _programPrintLatched = false;

    /// <summary>Gets a value indicating whether the program-print latch is currently set.</summary>
    public static bool ProgramPrintLatched => _programPrintLatched;

    /// <summary>
    /// Renders a procedure the way <c>print-program</c> does
    /// (<c>system/vm/program.scm:263-313</c>): <c>#&lt;procedure</c>, then a space and
    /// either the NAME or the object address in hex, then — for an unnamed procedure that
    /// knows where it came from — <c>" at file:line:column"</c>, then the parameter list.
    /// <para>
    /// The line is shown one-based (<c>source-line-for-user</c> adds the one back) while
    /// the column is shown as it stands. Both halves of that asymmetry are Guile's.
    /// </para>
    /// </summary>
    private static string RenderProgram(Procedure procedure)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder("#<procedure");
        string name = procedure.EffectiveName;
        builder.Append(' ').Append(name ?? HexAddress(procedure));

        TreeIl.TreeIlClosure closure = procedure as TreeIl.TreeIlClosure;
        Reader.SourceLocation source = closure == null ? null : closure.Source;
        if (source != null && name == null)
        {
            builder.Append(" at ").Append(source.FileName).Append(':')
                .Append((source.Line + 1).ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(source.Column.ToString(CultureInfo.InvariantCulture));
        }

        string formals = Formals(procedure);
        if (formals != null)
        {
            builder.Append(' ').Append(formals);
        }

        return builder.Append('>').ToString();
    }

    /// <summary>
    /// Returns a procedure's parameter list, or <see langword="null"/> when it has no
    /// arity to report — <c>print-program</c> omits the formals entirely in that case.
    /// </summary>
    private static string Formals(Procedure procedure)
    {
        if (procedure is TreeIl.TreeIlClosure closure)
        {
            return closure.LambdaList();
        }

        if (!(procedure is Primitive primitive))
        {
            return null;
        }

        // A C-implemented procedure has no parameter NAMES, so Guile fills the list with
        // placeholders: arity->arguments-alist (system/vm/program.scm:171-174) makes every
        // one of them the symbol `_'.
        List<string> items = new List<string>();
        for (int i = 0; i < primitive.MinimumArgumentCount; i++)
        {
            items.Add("_");
        }

        bool variadic = primitive.MaximumArgumentCount < 0;
        int optionals = variadic ? 0 : primitive.MaximumArgumentCount - primitive.MinimumArgumentCount;
        if (optionals > 0)
        {
            items.Add("#:optional");
            for (int i = 0; i < optionals; i++)
            {
                items.Add("_");
            }
        }

        string joined = string.Join(" ", items);
        if (!variadic)
        {
            return "(" + joined + ")";
        }

        return items.Count == 0 ? "_" : "(" + joined + " . _)";
    }

    /// <summary>
    /// Returns a stand-in for <c>object-address</c>, as TWELVE lowercase hex digits.
    /// <para>
    /// ⚠ THE WIDTH IS LOAD-BEARING, and the value is not. LilyPond strips the address
    /// before the text reaches its manual — <c>scm-&gt;string</c>'s two regexes exist to
    /// "remove the hexadecimal address to ensure reproducible builds" — but it strips it
    /// AFTER <c>pretty-print</c> has already decided where to break lines, and
    /// <c>pretty-print</c> measures the text it actually wrote. A shorter address makes
    /// entries fit that upstream wraps, so the manual differs on lines that contain no
    /// address at all. Guile prints a heap pointer, which on 64-bit is twelve digits
    /// (<c>7f984bcf5690</c>), so twelve is what is produced here: the high bits are a
    /// fixed <c>7f</c> so the count cannot vary with the hash.
    /// </para>
    /// </summary>
    private static string HexAddress(object value)
        => HexAddress((uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value));

    private static string HexAddress(uint seed)
        => (0x7f0000000000UL | seed).ToString("x", CultureInfo.InvariantCulture);

    private static string ProgramFallback(object value)
    {
        // Upstream prints SCM_UNPACK (program) and SCM_PROGRAM_CODE (program) as two
        // hexadecimal numbers. Neither survives: scm->string exists to strip them for
        // reproducible builds, and its regex is "#<program [0-9a-f]+ [0-9a-f]+>", so what
        // matters is the SHAPE -- two lowercase hex runs -- and not the values.
        uint seed = (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        return "#<program " + HexAddress(seed) + " " + HexAddress(seed ^ 0x9e3779b9u) + ">";
    }

    private static string Render(object value, bool quoteStrings)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        RenderInto(builder, value, quoteStrings, 0);
        return builder.ToString();
    }

    private static void RenderInto(System.Text.StringBuilder builder, object value, bool quoteStrings, int depth)
    {
        if (depth > 200)
        {
            builder.Append("...");
            return;
        }

        switch (value)
        {
            case null:
                builder.Append("#<null>");
                return;
            case bool b:
                builder.Append(b ? "#t" : "#f");
                return;
            case Nil _:
                builder.Append("()");
                return;
            case ElispNil _:
                builder.Append("#nil");
                return;
            case Symbol s:
                builder.Append(s.Name);
                return;
            case MutableString ms:
                if (quoteStrings)
                {
                    builder.Append('"').Append(Escape(ms.ToString())).Append('"');
                }
                else
                {
                    builder.Append(ms.ToString());
                }

                return;
            case SchemeChar c:
                builder.Append(quoteStrings ? WriteChar(c) : c.ToString());
                return;
            case Keyword k:
                builder.Append(k.ToString());
                return;
            case Pair pair:
                RenderPair(builder, pair, quoteStrings, depth);
                return;
            case SchemeArray array:
                RenderArray(builder, array, quoteStrings, depth);
                return;
            case object[] vector:
            {
                // A record instance is a vector whose slot 0 holds its RecordType, and
                // prints as boot-9's default-record-printer does: #<name field: value ...>
                // with the values WRITTEN, whatever printer the surrounding render is.
                if (vector.Length > 0 && vector[0] is Values.RecordType recordType)
                {
                    builder.Append("#<").Append(recordType.Name);
                    for (int i = 0; i < recordType.Fields.Count && i + 1 < vector.Length; i++)
                    {
                        builder.Append(' ');
                        RenderInto(builder, recordType.Fields[i], quoteStrings, depth + 1);
                        builder.Append(": ");
                        RenderInto(builder, vector[i + 1], true, depth + 1);
                    }

                    builder.Append('>');
                    return;
                }

                builder.Append("#(");
                for (int i = 0; i < vector.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(' ');
                    }

                    RenderInto(builder, vector[i], quoteStrings, depth + 1);
                }

                builder.Append(')');
                return;
            }

            case Primitives.GenericFunction generic:
                // GOOPS prints a generic as its class, its name and its method count --
                // `#<<generic> + (2)>' -- which is the form Guile's goops-error carries in
                // "No applicable method for ~S". MEASURED on the pinned 2.27.2.
                builder.Append("#<<generic> ")
                    .Append(generic.Name ?? "#f")
                    .Append(" (")
                    .Append(generic.Methods.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(")>");
                return;

            case Procedure procedure:
                // Guile prints every procedure through the program printer, and while the
                // re-entry latch is set that printer is bypassed for the low-level form.
                builder.Append(_programPrintLatched ? ProgramFallback(procedure) : RenderProgram(procedure));
                return;

            default:
                if (Numeric.SchemeNumber.IsNumber(value))
                {
                    builder.Append(Numeric.SchemeNumber.ToDisplayString(value));
                    return;
                }

                // A host object may carry its own external representation, the managed
                // counterpart of a smob's print hook. Guile consults that hook for both
                // write and display, so neither quoteStrings nor depth reaches it.
                if (value is ISchemePrintable printable)
                {
                    builder.Append(printable.PrintRepresentation());
                    return;
                }

                builder.Append(value.ToString());
                return;
        }
    }

    private static void RenderArray(System.Text.StringBuilder builder, SchemeArray array, bool quoteStrings, int depth)
    {
        if (array.IsShared)
        {
            // A shared view's contents live behind an index mapper that may be a Scheme
            // procedure, and the printer has no evaluator to apply one with.
            builder.Append("#<shared-array rank ").Append(array.Rank).Append('>');
            return;
        }

        builder.Append('#').Append(array.Rank);
        bool anyLowerBound = false;
        foreach (int lower in array.LowerBounds)
        {
            if (lower != 0)
            {
                anyLowerBound = true;
            }
        }

        if (anyLowerBound)
        {
            foreach (int lower in array.LowerBounds)
            {
                builder.Append('@').Append(lower);
            }
        }

        int offset = 0;
        RenderArrayLevel(builder, array, 0, ref offset, quoteStrings, depth);
    }

    private static void RenderArrayLevel(
        System.Text.StringBuilder builder,
        SchemeArray array,
        int dimension,
        ref int offset,
        bool quoteStrings,
        int depth)
    {
        builder.Append('(');
        for (int i = 0; i < array.Lengths[dimension]; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            if (dimension == array.Rank - 1)
            {
                RenderInto(builder, array.Storage[offset++], quoteStrings, depth + 1);
            }
            else
            {
                RenderArrayLevel(builder, array, dimension + 1, ref offset, quoteStrings, depth);
            }
        }

        builder.Append(')');
    }

    private static void RenderPair(System.Text.StringBuilder builder, Pair pair, bool quoteStrings, int depth)
    {
        builder.Append('(');
        object cursor = pair;
        bool first = true;
        int guard = 0;
        while (cursor is Pair current)
        {
            if (!first)
            {
                builder.Append(' ');
            }

            RenderInto(builder, current.Car, quoteStrings, depth + 1);
            first = false;
            cursor = current.Cdr;
            if (++guard > 100000)
            {
                builder.Append(" ...");
                cursor = Nil.Instance;
                break;
            }
        }

        if (!(cursor is Nil))
        {
            builder.Append(" . ");
            RenderInto(builder, cursor, quoteStrings, depth + 1);
        }

        builder.Append(')');
    }

    private static string WriteChar(SchemeChar c)
    {
        switch (c.CodePoint)
        {
            case ' ': return "#\\space";
            case '\n': return "#\\newline";
            case '\t': return "#\\tab";
            case '\r': return "#\\return";
            case 0: return "#\\nul";
            default:
                return "#\\" + c;
        }
    }

    private static string Escape(string value)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\r': builder.Append("\\r"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Renders a value for an error message, truncating long output.</summary>
    /// <param name="value">The value to render.</param>
    /// <param name="maximumLength">The maximum number of characters to emit.</param>
    /// <returns>A possibly truncated representation.</returns>
    public static string Abbreviate(object value, int maximumLength)
    {
        string text = Write(value);
        if (text.Length <= maximumLength)
        {
            return text;
        }

        return text.Substring(0, maximumLength) + "..."
               + " (" + text.Length.ToString(CultureInfo.InvariantCulture) + " chars)";
    }
}
