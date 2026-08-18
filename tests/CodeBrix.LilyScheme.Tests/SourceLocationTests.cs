// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Source locations, from the reader through psyntax into a procedure's printed
/// representation, and the program-print re-entry latch that Guile never clears.
/// <para>
/// Every expectation here is read off Guile's own source or off the pinned LilyPond
/// oracle's output, never off this implementation: the line/column convention is
/// <c>system/vm/debug.scm:673-674</c>'s (<c>source-line-for-user</c> adds one to the line
/// and nothing is added to the column), and the printed shapes are
/// <c>system/vm/program.scm:263-313</c>'s.
/// </para>
/// </summary>
public sealed class SourceLocationTests
{
    private static object Eval(string source, string fileName)
    {
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeBootstrap.LoadExpanded(interpreter, source, fileName);
            result = interpreter.EvalString("__result", fileName);
        });

        return result;
    }

    [Fact]
    public void an_anonymous_procedure_prints_the_line_one_based_and_the_column_zero_based()
    {
        //Arrange -- the lambda is on the THIRD line of the source and its opening paren
        //sits at zero-based column 4. Guile shows the line one-based and the column as it
        //stands, so the expected text is 3:4 and not 2:4, 3:5 or 2:5. Each of those three
        //is what a different off-by-one convention would produce, which is why the case
        //is built with the two numbers DIFFERENT.
        string source = "(define (outer)\n"
                        + "  (let ()\n"
                        + "    (lambda (a b) a)))\n"
                        + "(define __result (outer))";

        //Act
        object result = Eval(source, "dir/probe.scm");

        //Assert
        Printer.Display(result).Should().MatchRegex(
            @"^#<procedure [0-9a-f]+ at dir/probe\.scm:3:4 \(a b\)>$");
    }

    [Fact]
    public void a_named_procedure_shows_its_name_and_formals_and_no_location()
    {
        //Arrange -- the control for the case above, and it must come out DIFFERENTLY:
        //print-program emits " at ..." only when the procedure has no name, so naming the
        //very same lambda has to remove the location AND the hex address.
        string source = "(define (outer)\n"
                        + "  (let ()\n"
                        + "    (lambda (a b) a)))\n"
                        + "(define named (outer))\n"
                        + "(define (also-named a b) a)\n"
                        + "(define __result also-named)";

        //Act
        object result = Eval(source, "dir/probe.scm");

        //Assert
        Printer.Display(result).Should().Be("#<procedure also-named (a b)>");
    }

    [Fact]
    public void a_define_does_not_name_the_procedure_it_merely_computed()
    {
        //Arrange -- psyntax names a lambda that a define binds DIRECTLY
        //(maybe-name-value, ice-9/psyntax.scm:202-210) and nothing names one a define
        //merely computed. LilyPond depends on the difference: scm->string documents a
        //named procedure by its name and an unnamed one by its representation, which is
        //why the oracle shows format-coda-mark's value as a location rather than as
        //"format-coda-mark".
        string source = "(define (make-one) (lambda (x) x))\n"
                        + "(define computed (make-one))\n"
                        + "(define direct (lambda (x) x))\n"
                        + "(define __result (list (procedure-name computed) (procedure-name direct)))";

        //Act
        object result = Eval(source, "dir/probe.scm");

        //Assert
        Printer.Display(result).Should().Be("(#f direct)");
    }

    [Fact]
    public void source_properties_carry_the_readers_filename_line_and_column()
    {
        //Arrange -- psyntax reads this alist and nothing else (datum-sourcev,
        //ice-9/psyntax.scm:307-312). Both positions are counted by hand off the text
        //below, and they differ in BOTH coordinates so that a line/column swap or an
        //off-by-one in either one fails the case:
        //  (a b) is on source line 1, and "(define first-form '" is 20 characters, so it
        //        records line 0 (stored zero-based) column 20;
        //  (c d) is on source line 3 behind five spaces and a quote, so it records
        //        line 2 column 6.
        string source = "(define first-form '(a b))\n"
                        + "(define second-form\n"
                        + "     '(c d))\n"
                        + "(define __result (list (source-properties first-form)\n"
                        + "                       (source-properties second-form)))";

        //Act
        object result = Eval(source, "dir/probe.scm");

        //Assert
        Printer.Display(result).Should().Be(
            "(((filename . dir/probe.scm) (line . 0) (column . 20)) "
            + "((filename . dir/probe.scm) (line . 2) (column . 6)))");
    }

    [Fact]
    public void a_symbol_cannot_carry_source_properties()
    {
        //Arrange -- the control for the case above. An interned symbol is ONE object for
        //every occurrence in every file, so recording a location on it would attribute
        //one file's position to all of them. Guile answers #f to
        //supports-source-properties? for exactly that reason.
        string source = "(define __result (list (supports-source-properties? 'sym)\n"
                        + "                       (supports-source-properties? '(a))\n"
                        + "                       (source-properties 'sym)))";

        //Act
        object result = Eval(source, "dir/probe.scm");

        //Assert
        Printer.Display(result).Should().Be("(#f #t ())");
    }

    [Fact]
    public void the_program_print_latch_sticks_after_a_non_local_exit()
    {
        //Arrange -- upstream's guard (libguile/programs.c:110, 136-141) is set before the
        //Scheme printer runs and cleared after, so an emit that never returns leaves it
        //set for good. Measured against the pinned oracle, which prints a procedure
        //normally, then pretty-prints a wide alist, and from then on prints EVERY
        //procedure as #<program ...> -- 206 times in the generated manual.
        Printer.ResetProgramPrintLatch();
        Procedure subject = new Primitive("probe", 0, 0, a => a);
        string before = Printer.Display(subject);

        //Act -- an emit that exits non-locally, which is what the truncating soft port in
        //ice-9/pretty-print.scm does when a line goes over budget.
        try
        {
            Printer.WriteThroughProgramLatch(subject, false, _ => throw new PrintAbort());
        }
        catch (PrintAbort)
        {
        }

        string after = Printer.Display(subject);

        //Assert
        before.Should().Be("#<procedure probe ()>");
        Printer.ProgramPrintLatched.Should().BeTrue();
        after.Should().MatchRegex(@"^#<program [0-9a-f]+ [0-9a-f]+>$");

        //Cleanup -- the latch is process-global, exactly as upstream's static is.
        Printer.ResetProgramPrintLatch();
    }

    [Fact]
    public void an_emit_that_returns_normally_leaves_the_latch_clear()
    {
        //Arrange -- the control: the latch must be a consequence of the NON-LOCAL exit
        //and not of printing a procedure at all, or every procedure after the first would
        //degrade and the count would be 235 rather than the oracle's 206.
        Printer.ResetProgramPrintLatch();
        Procedure subject = new Primitive("probe", 1, 1, a => a);
        string emitted = null;

        //Act
        Printer.WriteThroughProgramLatch(subject, false, text => emitted = text);

        //Assert
        emitted.Should().Be("#<procedure probe (_)>");
        Printer.ProgramPrintLatched.Should().BeFalse();
        Printer.Display(subject).Should().Be("#<procedure probe (_)>");
    }

    private sealed class PrintAbort : System.Exception
    {
    }
}


/// <summary>
/// The soft port's block buffering, which decides WHERE pretty-print's truncating
/// writer aborts and therefore when the program-print latch sets.
/// <para>
/// Every expectation is a flush sequence measured against the pinned LilyPond oracle,
/// and the model those measurements produced — capacity 1024 bytes, an overflowing
/// write topping the buffer up by whole 252-byte quanta — was then CONFIRMED BY
/// PREDICTION on starting fills it had never been shown (700 → 952, 800 → 800,
/// 772 → 1024, 771 → 1023).
/// </para>
/// </summary>
public sealed class SoftPortBufferingTests
{
    private static string Flushes(params int[] writes)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            string source = @"
(use-modules (ice-9 soft-ports))
(define acc '())
(define p (make-soft-port #:id ""probe""
                          #:write-string (lambda (s) (set! acc (cons (string-length s) acc)))))
(define (put n) (display (make-string n #\a) p))
(define (during) (reverse acc))
(define (final) (begin (force-output p) (reverse acc)))";
            SchemeBootstrap.LoadExpanded(interpreter, source, "probe.scm");
            foreach (int n in writes)
            {
                interpreter.EvalString("(put " + n + ")", "probe.scm");
            }

            object during = interpreter.EvalString("(during)", "probe.scm");
            object final = interpreter.EvalString("(final)", "probe.scm");
            result = Printer.Display(during) + " -> " + Printer.Display(final);
        });

        return result;
    }

    [Fact]
    public void a_write_that_fits_is_buffered_and_a_full_buffer_flushes()
    {
        //Arrange/Act/Assert -- 1023 bytes stay in the buffer and only the flush at the
        //end sends them; one more byte fills the buffer exactly and sends it at once.
        //The pair is what fixes the capacity at 1024 rather than near it.
        Flushes(1023).Should().Be("() -> (1023)");
        Flushes(1024).Should().Be("(1024) -> (1024)");
    }

    [Fact]
    public void an_overflowing_write_tops_up_by_whole_quanta_not_to_the_brim()
    {
        //Arrange/Act/Assert -- the case that discriminates the model. With 700 bytes
        //buffered there are 324 free, and Guile adds 252 (one quantum) rather than the
        //324 that would fill it: the first flush is 952. With 800 buffered only 224 are
        //free, no whole quantum fits, and the flush carries the 800 already there.
        Flushes(700, 4000).Should().Be("(952 1008 1008 1008) -> (952 1008 1008 1008 724)");
        Flushes(800, 4000).Should().Be("(800 1008 1008 1008) -> (800 1008 1008 1008 976)");
    }

    [Fact]
    public void an_empty_buffer_transfers_four_quanta_at_a_time()
    {
        //Arrange/Act/Assert -- 1008 is 4 x 252, the largest multiple of the quantum that
        //fits in an empty 1024-byte buffer, so a long write leaves 1024 - 1008 = 16 bytes
        //of the buffer unused on every pass.
        Flushes(4000).Should().Be("(1008 1008 1008) -> (1008 1008 1008 976)");
    }

    [Fact]
    public void many_small_writes_accumulate_rather_than_passing_straight_through()
    {
        //Arrange -- the control that matters for the latch: an unbuffered port would call
        //write-string five times here, and pretty-print's truncating writer aborts inside
        //that call. Buffering is what keeps a short structure from ever aborting mid-print.
        //Act/Assert -- 500 x 5: two flushes of 1000, because the third and fifth writes
        //find only 24 bytes free and no whole quantum fits.
        Flushes(500, 500, 500, 500, 500).Should().Be("(1000 1000) -> (1000 1000 500)");
    }
}
