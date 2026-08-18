// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Guile 3's modern exception API — <c>raise-exception</c>,
/// <c>with-exception-handler</c>, exception objects, and the vendored
/// <c>(ice-9 exceptions)</c> with its <c>guard</c> and converter table — fenced from
/// BOTH sides of the old/new interop: a <c>catch</c> must see a raised exception
/// through its kind and args, and an exception handler must see a plain
/// <c>throw</c> (including a C# primitive's) as a converted exception object.
/// Expected values are boot-9.scm:1448-1861 and ice-9/exceptions.scm in the pinned
/// Guile 3.0.11 source.
/// </summary>
public class ModernExceptionTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(
                "(use-modules (ice-9 exceptions)) " + source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void catch_sees_a_raised_exception_through_kind_and_args()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'boom"
            + " (lambda () (raise-exception (make-exception-from-throw 'boom '(1 2))))"
            + " (lambda (key . args) (list key args)))");

        //Assert
        result.Should().Be("(boom (1 2))");
    }

    [Fact]
    public void a_raised_plain_object_reaches_catch_as_percent_exception()
    {
        //Arrange / Act
        // exception-kind of a non-kind-and-args object is '%exception and its args are
        // the one-element list holding the object — boot-9.scm:1606-1613.
        string result = Value(
            "(catch #t (lambda () (raise-exception 42)) (lambda (key . args) (list key args)))");

        //Assert
        result.Should().Be("(%exception (42))");
    }

    [Fact]
    public void an_unwinding_handler_sees_a_plain_throw_as_an_exception_object()
    {
        //Arrange / Act
        string result = Value(
            "(with-exception-handler (lambda (e) (list (exception-kind e) (exception-args e)))"
            + " (lambda () (throw 'oops 7)) #:unwind? #t)");

        //Assert
        result.Should().Be("(oops (7))");
    }

    [Fact]
    public void a_primitive_error_converts_to_an_assertion_failure()
    {
        //Arrange / Act
        // (car 5) throws wrong-type-arg from C#; the module's converter table maps it
        // to &assertion-failure inside &error — ice-9/exceptions.scm:322-344.
        string result = Value(
            "(with-exception-handler"
            + " (lambda (e) (list (error? e) (assertion-failure? e) (exception-kind e)))"
            + " (lambda () (car 5)) #:unwind? #t)");

        //Assert
        result.Should().Be("(#t #t wrong-type-arg)");
    }

    [Fact]
    public void unwind_for_type_passes_a_non_matching_exception_on()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'other"
            + " (lambda () (with-exception-handler (lambda (e) 'handled)"
            + "   (lambda () (throw 'other)) #:unwind? #t #:unwind-for-type 'nope))"
            + " (lambda (key . args) 'catch-won))");

        //Assert
        result.Should().Be("catch-won");
    }

    [Fact]
    public void unwind_for_type_accepts_an_exception_type()
    {
        //Arrange / Act
        string result = Value(
            "(with-exception-handler (lambda (e) 'typed)"
            + " (lambda () (error \"boom\")) #:unwind? #t #:unwind-for-type &error)");

        //Assert
        result.Should().Be("typed");
    }

    [Fact]
    public void a_non_unwinding_handler_runs_before_the_unwind_and_the_raise_continues()
    {
        //Arrange / Act
        // The handler runs pre-unwind; when it returns on a non-continuable raise, a
        // fresh &non-continuable — kind '%exception — propagates to the outer catch.
        string result = Value(
            "(let ((order '()))"
            + " (catch #t"
            + "  (lambda () (with-exception-handler"
            + "    (lambda (e) (set! order (cons 'handler order)))"
            + "    (lambda () (throw 'x))))"
            + "  (lambda (key . args) (set! order (cons key order))))"
            + " (reverse order))");

        //Assert
        result.Should().Be("(handler %exception)");
    }

    [Fact]
    public void a_non_unwinding_handler_can_exit_by_throwing()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'escape"
            + " (lambda () (with-exception-handler"
            + "   (lambda (e) (throw 'escape (exception-kind e)))"
            + "   (lambda () (throw 'original))))"
            + " (lambda (key . args) (cons 'caught args)))");

        //Assert
        result.Should().Be("(caught original)");
    }

    [Fact]
    public void raise_continuable_returns_the_handlers_value_to_the_raise_point()
    {
        //Arrange / Act
        string result = Value(
            "(with-exception-handler (lambda (e) (+ e 1))"
            + " (lambda () (+ 10 (raise-exception 5 #:continuable? #t))))");

        //Assert
        result.Should().Be("16");
    }

    [Fact]
    public void a_handler_is_not_its_own_current_handler()
    {
        //Arrange / Act
        // A raise-continuable from within a handler must reach the OUTER handler, not
        // re-enter itself — "the current exception handler is the one that was in
        // place when the handler being called was installed".
        string result = Value(
            "(with-exception-handler (lambda (e) (list 'outer e))"
            + " (lambda ()"
            + "  (with-exception-handler (lambda (e) (raise-continuable (* e 10)))"
            + "   (lambda () (raise-continuable 4)))))");

        //Assert
        result.Should().Be("(outer 40)");
    }

    [Fact]
    public void guard_dispatches_to_a_matching_clause()
    {
        //Arrange / Act
        string result = Value(
            "(guard (e ((symbol? e) (list 'sym e))) (raise-continuable 'aha))");

        //Assert
        result.Should().Be("(sym aha)");
    }

    [Fact]
    public void guard_with_no_matching_clause_reraises_to_the_outer_context()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'deep"
            + " (lambda () (guard (e ((number? e) 'nope)) (throw 'deep)))"
            + " (lambda (key . args) 'outer-caught))");

        //Assert
        result.Should().Be("outer-caught");
    }

    [Fact]
    public void guard_reraise_is_continuable_so_an_outer_handler_can_answer()
    {
        //Arrange / Act
        // R7RS: an unhandled guard re-raises with raise-continuable in the original
        // raise context, so the outer handler's value flows back to the ORIGINAL
        // raise-continuable point — the whole value chain, end to end.
        string result = Value(
            "(with-exception-handler (lambda (e) (* e 2))"
            + " (lambda ()"
            + "  (guard (e ((string? e) 'no-match)) (+ 1 (raise-continuable 20)))))");

        //Assert
        result.Should().Be("41");
    }

    [Fact]
    public void error_converts_to_an_exception_with_message_and_irritants()
    {
        //Arrange / Act
        string result = Value(
            "(guard (e ((exception-with-message? e)"
            + "          (list (exception-message e) (exception-irritants e))))"
            + " (error \"the message\" 1 2))");

        //Assert
        result.Should().Be("(\"the message\" (1 2))");
    }

    [Fact]
    public void make_exception_flattens_and_a_single_component_stays_simple()
    {
        //Arrange / Act
        string result = Value(
            "(list"
            + " (length (simple-exceptions (make-exception (make-error)"
            + "   (make-exception (make-exception-with-message \"m\")"
            + "                   (make-exception-with-irritants '(1))))))"
            + " (eq? (make-exception (make-error)) (make-exception (make-error))))");

        //Assert
        // Three simple components after flattening; a one-component make-exception
        // answers the component itself (so two different components are not eq).
        result.Should().Be("(3 #f)");
    }

    [Fact]
    public void exception_accessors_reach_into_a_compound()
    {
        //Arrange / Act
        string result = Value(
            "(let ((exn (make-exception (make-error) (make-exception-with-message \"mm\"))))"
            + " (list (exception? exn) (error? exn) (exception-message exn)))");

        //Assert
        result.Should().Be("(#t #t \"mm\")");
    }

    [Fact]
    public void define_exception_type_makes_a_working_subtype()
    {
        //Arrange / Act
        string result = Value(
            "(define-exception-type &frob &error make-frob frob? (severity frob-severity))"
            + "(guard (e ((frob? e) (list (error? e) (frob-severity e))))"
            + " (raise-exception (make-exception (make-frob 9)"
            + "                                  (make-exception-with-message \"m\"))))");

        //Assert
        result.Should().Be("(#t 9)");
    }

    [Fact]
    public void a_quit_throw_converts_to_a_quit_exception_with_its_code()
    {
        //Arrange / Act
        string result = Value(
            "(let ((exn (make-exception-from-throw 'quit '(3))))"
            + " (list (quit-exception? exn) (exception-kind exn)))");

        //Assert
        result.Should().Be("(#t quit)");
    }

    [Fact]
    public void with_throw_handler_still_fires_for_a_raised_exception()
    {
        //Arrange / Act
        string result = Value(
            "(let ((seen '()))"
            + " (catch 'k"
            + "  (lambda () (with-throw-handler 'k"
            + "    (lambda () (raise-exception (make-exception-from-throw 'k '(1))))"
            + "    (lambda (key . args) (set! seen (cons 'pre seen)))))"
            + "  (lambda (key . args) (set! seen (cons 'post seen))))"
            + " (reverse seen))");

        //Assert
        result.Should().Be("(pre post)");
    }

    [Fact]
    public void print_exception_answers_the_default_line_and_the_registered_printer()
    {
        //Arrange / Act
        // An unregistered key gets boot-9's default line; misc-error goes through the
        // prelude's scm-error-printer, "In procedure ~a: " and the formatted message.
        string result = Value(
            "(list"
            + " (call-with-output-string (lambda (p) (print-exception p #f 'weird '(1))))"
            + " (call-with-output-string (lambda (p)"
            + "   (print-exception p #f 'misc-error '(\"subr\" \"bad ~a\" (thing) #f)))))");

        //Assert
        result.Should().Be("(\"Throw to key `weird' with args `(1)'.\\n\" \"In procedure subr: bad thing\\n\")");
    }

    [Fact]
    public void a_non_procedure_handler_is_refused_with_wrong_type_arg()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'wrong-type-arg"
            + " (lambda () (with-exception-handler 5 (lambda () 1)))"
            + " (lambda (key . args) 'refused))");

        //Assert
        result.Should().Be("refused");
    }

    [Fact]
    public void a_bad_unwind_for_type_is_refused_with_wrong_type_arg()
    {
        //Arrange / Act
        string result = Value(
            "(catch 'wrong-type-arg"
            + " (lambda () (with-exception-handler (lambda (e) e) (lambda () 1)"
            + "             #:unwind? #t #:unwind-for-type 5))"
            + " (lambda (key . args) 'refused))");

        //Assert
        result.Should().Be("refused");
    }
}
