// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Applying a procedure with the wrong number of arguments raises Guile's catchable
/// <c>wrong-number-of-args</c> from the Tree-IL path too (2026-08-28), instead of
/// running the body with the missing parameters bound to <c>#&lt;unspecified&gt;</c>
/// and the surplus ones dropped.
/// <para>
/// Found through LilyPond: Mutopia scores call <c>unfold-repeats</c> from embedded
/// Scheme with its pre-2.23 arity. LilyPond 2.27.2 refuses such a file with
/// <c>Wrong number of arguments to #&lt;procedure unfold-repeats (types music)&gt;</c>;
/// the interpreter used to let the body run with <c>music</c> unbound, and the port
/// engraved a score upstream never draws. The error's shape is the VM's
/// <c>vm_error_wrong_num_args</c>: subr <c>#f</c>, message
/// <c>"Wrong number of arguments to ~A"</c>, the procedure object as the one argument.
/// </para>
/// </summary>
public class WrongNumberOfArgumentsTests
{
    private static string Eval(params string[] sources)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (string source in sources)
            {
                foreach (object form in SchemeReader.ReadAll(source, "<test>"))
                {
                    result = Printer.Write(
                        interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
                }
            }
        });

        return result;
    }

    private static SchemeThrow Raise(params string[] sources)
    {
        SchemeThrow thrown = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            try
            {
                foreach (string source in sources)
                {
                    foreach (object form in SchemeReader.ReadAll(source, "<test>"))
                    {
                        interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
                    }
                }
            }
            catch (SchemeThrow schemeThrow)
            {
                thrown = schemeThrow;
            }
        });

        return thrown;
    }

    [Fact]
    public void too_few_arguments_raise_a_catchable_wrong_number_of_args()
    {
        //Arrange / Act
        string result = Eval(
            "(define (needs-two a b) (list a b))",
            "(catch 'wrong-number-of-args"
            + " (lambda () (needs-two 1))"
            + " (lambda (key . args) key))");

        //Assert
        result.Should().Be("wrong-number-of-args");
    }

    [Fact]
    public void too_many_arguments_raise_it_as_well()
    {
        //Arrange / Act
        string result = Eval(
            "(define (needs-two a b) (list a b))",
            "(catch 'wrong-number-of-args"
            + " (lambda () (needs-two 1 2 3))"
            + " (lambda (key . args) key))");

        //Assert
        result.Should().Be("wrong-number-of-args");
    }

    [Fact]
    public void the_error_has_the_vm_shape_and_names_the_procedure_object()
    {
        //Arrange / Act
        SchemeThrow thrown = Raise("(define (needs-two a b) a)", "(needs-two 1)");

        //Assert
        // vm_error_wrong_num_args: (wrong-number-of-args #f "Wrong number of arguments
        // to ~A" (proc) #f). The procedure OBJECT is the format argument, so the
        // report can print its name and its parameter list.
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-number-of-args"));
        Pair arguments = (Pair)thrown.Arguments;
        arguments.Car.Should().Be(false);
        ((Pair)arguments.Cdr).Car.ToString().Should().Be("Wrong number of arguments to ~A");
        Pair formatArguments = (Pair)((Pair)((Pair)arguments.Cdr).Cdr).Car;
        Procedure named = (Procedure)formatArguments.Car;
        named.EffectiveName.Should().Be("needs-two");
        formatArguments.Cdr.Should().Be(Nil.Instance);
    }

    [Fact]
    public void the_report_reads_as_guile_prints_it()
    {
        //Arrange / Act
        string result = Eval(
            "(define (unfold-repeats types music) music)",
            "(catch 'wrong-number-of-args"
            + " (lambda () (unfold-repeats 'music-only))"
            + " (lambda (key subr message args . rest) (apply format #f message args)))");

        //Assert
        // The line LilyPond 2.27.2 prints for the Mutopia case, character for character.
        result.Should().Be("\"Wrong number of arguments to #<procedure unfold-repeats (types music)>\"");
    }

    [Fact]
    public void a_missing_optional_parameter_still_defaults_to_false()
    {
        //Arrange / Act
        string result = Eval("(define* (f a #:optional b) (list a b))", "(f 1)");

        //Assert
        result.Should().Be("(1 #f)");
    }

    [Fact]
    public void a_rest_parameter_still_takes_any_count()
    {
        //Arrange / Act
        string result = Eval("(define (f a . more) more)", "(list (f 1) (f 1 2 3))");

        //Assert
        result.Should().Be("(() (2 3))");
    }

    [Fact]
    public void keyword_parameters_still_read_the_positional_tail()
    {
        //Arrange / Act
        // A keyword clause has no positional ceiling: the arguments past the required
        // ones are keyword/value pairs, and must not be refused as surplus.
        string result = Eval("(define* (f a #:key (b 2)) (list a b))", "(list (f 1) (f 1 #:b 5))");

        //Assert
        result.Should().Be("((1 2) (1 5))");
    }

    [Fact]
    public void a_case_lambda_with_no_fitting_clause_names_itself_not_its_last_arm()
    {
        //Arrange / Act
        string fits = Eval(
            "(define g (case-lambda ((a) 'one) ((a b) 'two)))",
            "(list (g 1) (g 1 2))");
        SchemeThrow thrown = Raise(
            "(define g (case-lambda ((a) 'one) ((a b) 'two)))",
            "(g 1 2 3)");

        //Assert
        fits.Should().Be("(one two)");
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-number-of-args"));
        Pair formatArguments = (Pair)((Pair)((Pair)((Pair)thrown.Arguments).Cdr).Cdr).Car;
        ((Procedure)formatArguments.Car).EffectiveName.Should().Be("g");
    }

    [Fact]
    public void a_primitive_reports_the_vm_shape_too()
    {
        //Arrange / Act
        // MEASURED: (abs 1 2) on the pinned 2.27.2 is
        // (wrong-number-of-args #f "Wrong number of arguments to ~A" (#<procedure abs (_)>) #f).
        //was previously: ("abs" "Wrong number of arguments" () #f), the interpreter's own shape.
        SchemeThrow thrown = Raise("(abs 1 2)");

        //Assert
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-number-of-args"));
        Pair arguments = (Pair)thrown.Arguments;
        arguments.Car.Should().Be(false);
        ((Pair)arguments.Cdr).Car.ToString().Should().Be("Wrong number of arguments to ~A");
        Pair formatArguments = (Pair)((Pair)((Pair)arguments.Cdr).Cdr).Car;
        ((Procedure)formatArguments.Car).EffectiveName.Should().Be("abs");
    }

    [Fact]
    public void a_call_with_the_declared_arity_is_unaffected()
    {
        //Arrange / Act
        string result = Eval(
            "(define (unfold-repeats types music) (list types music))",
            "(unfold-repeats '() 'music)");

        //Assert
        result.Should().Be("(() music)");
    }
}
