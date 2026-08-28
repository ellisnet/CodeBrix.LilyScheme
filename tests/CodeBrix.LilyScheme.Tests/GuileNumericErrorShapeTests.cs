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
/// The numeric family raises GUILE'S errors, in Guile's shape, at Guile's positions
/// (2026-08-28, the two-mode proposal's item 4a and everything found beside it).
/// <para>
/// Every expected string below was MEASURED on the pinned LilyPond 2.27.2 binary
/// ("running Guile 3.0") with a <c>(catch #t … (lambda (key . args) (cons key args)))</c>
/// probe, on operators that carry no GOOPS methods in that context, and is asserted
/// character for character. The shape is
/// <c>(wrong-type-arg NAME "Wrong type argument in position ~A: ~S" (POS VALUE) (VALUE))</c>:
/// a template message, the position and value as its arguments, the value again in the
/// data slot. The positions are pairwise for the n-ary operators — the accumulator is
/// position 1 of every later pair — and <c>=</c> names position 1 whichever side is bad.
/// </para>
/// <para>
/// //was previously: <c>+ * gcd lcm</c> raised subr "arithmetic" and the comparisons
/// "comparison", unpositioned, with <c>#f</c> data; a dozen primitives let a raw .NET
/// <c>ArgumentException</c> escape to the host; <c>floor</c>, <c>round</c>,
/// <c>inexact->exact</c> and <c>numerator</c> answered a non-number unchanged.
/// </para>
/// </summary>
public class GuileNumericErrorShapeTests
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

    private static string Condition(string expression)
        => Eval("(catch #t (lambda () " + expression + ") (lambda (key . args) (cons key args)))");

    [Theory]
    [InlineData("(+ \"1\" \"2\")", "(wrong-type-arg \"+\" \"Wrong type argument in position ~A: ~S\" (1 \"1\") (\"1\"))")]
    [InlineData("(+ 1 \"2\")", "(wrong-type-arg \"+\" \"Wrong type argument in position ~A: ~S\" (2 \"2\") (\"2\"))")]
    [InlineData("(+ 1 2 \"x\")", "(wrong-type-arg \"+\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(+ \"x\")", "(wrong-type-arg \"+\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(- \"1\")", "(wrong-type-arg \"-\" \"Wrong type argument in position ~A: ~S\" (1 \"1\") (\"1\"))")]
    [InlineData("(- 1 \"x\")", "(wrong-type-arg \"-\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(* 0 \"x\")", "(wrong-type-arg \"*\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(/ \"1\" 2)", "(wrong-type-arg \"/\" \"Wrong type argument in position ~A: ~S\" (1 \"1\") (\"1\"))")]
    [InlineData("(/ 1 2 \"x\")", "(wrong-type-arg \"/\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(> 1 \"x\")", "(wrong-type-arg \">\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(> 3 2 \"x\")", "(wrong-type-arg \">\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(> \"x\" 2 1)", "(wrong-type-arg \">\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(<= 1 \"x\" 3)", "(wrong-type-arg \"<=\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(>= \"x\" 1)", "(wrong-type-arg \">=\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(= 1 \"x\")", "(wrong-type-arg \"=\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(= \"x\" 1)", "(wrong-type-arg \"=\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(max 1 2 \"x\")", "(wrong-type-arg \"max\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(max \"x\")", "(wrong-type-arg \"max\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(min \"x\" 1 2)", "(wrong-type-arg \"min\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(abs \"x\")", "(wrong-type-arg \"abs\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(sqrt \"x\")", "(wrong-type-arg \"sqrt\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(quotient 1.5 2)", "(wrong-type-arg \"quotient\" \"Wrong type argument in position ~A: ~S\" (1 1.5) (1.5))")]
    [InlineData("(modulo 1 \"x\")", "(wrong-type-arg \"modulo\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(gcd 1.5 2)", "(wrong-type-arg \"gcd\" \"Wrong type argument in position ~A: ~S\" (1 1.5) (1.5))")]
    [InlineData("(lcm 1 \"x\")", "(wrong-type-arg \"lcm\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(exact->inexact \"x\")", "(wrong-type-arg \"exact->inexact\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(inexact->exact \"x\")", "(wrong-type-arg \"inexact->exact\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(floor \"x\")", "(wrong-type-arg \"floor\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(round \"x\")", "(wrong-type-arg \"round\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(numerator \"x\")", "(wrong-type-arg \"numerator\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(atan 1 \"x\")", "(wrong-type-arg \"atan\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(1+ \"x\")", "(wrong-type-arg \"+\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(1- \"x\")", "(wrong-type-arg \"-\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(even? 2.5)", "(wrong-type-arg \"even?\" \"Wrong type argument in position ~A: ~S\" (1 2.5) (2.5))")]
    [InlineData("(nan? \"x\")", "(wrong-type-arg \"nan?\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(logand 2.0 1)", "(wrong-type-arg \"logand\" \"Wrong type argument in position ~A: ~S\" (1 2.0) (2.0))")]
    [InlineData("(logand 1 \"x\")", "(wrong-type-arg \"logand\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    [InlineData("(lognot 2.0)", "(wrong-type-arg \"logxor\" \"Wrong type argument in position ~A: ~S\" (1 2.0) (2.0))")]
    [InlineData("(ash \"x\" 1)", "(wrong-type-arg \"ash\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(ash 1 \"x\")", "(wrong-type-arg \"<\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(ash 2 1.5)", "(wrong-type-arg #f \"Wrong type (expecting ~A): ~S\" (\"exact integer\" 1.5) (1.5))")]
    [InlineData("(number->string \"x\")", "(wrong-type-arg \"number->string\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(number->string 1 \"x\")", "(wrong-type-arg #f \"Wrong type (expecting ~A): ~S\" (\"exact integer\" \"x\") (\"x\"))")]
    [InlineData("(string->number 5)", "(wrong-type-arg \"string->number\" \"Wrong type argument in position ~A (expecting ~A): ~S\" (1 \"string\" 5) (5))")]
    [InlineData("(expt \"x\" 2)", "(wrong-type-arg \"*\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(expt 2 \"x\")", "(wrong-type-arg \"expt\" \"Wrong type argument in position ~A: ~S\" (2 \"x\") (\"x\"))")]
    public void a_numeric_type_failure_has_guiles_shape_name_and_position(string expression, string expected)
    {
        //Arrange / Act
        string condition = Condition(expression);

        //Assert
        condition.Should().Be(expected);
    }

    [Theory]
    [InlineData("(* 1 \"x\")", "\"x\"")]
    [InlineData("(* \"x\" 1)", "\"x\"")]
    [InlineData("(* 1 \"x\" 2)", "(wrong-type-arg \"*\" \"Wrong type argument in position ~A: ~S\" (1 \"x\") (\"x\"))")]
    [InlineData("(< \"x\")", "#t")]
    [InlineData("(= \"x\")", "#t")]
    [InlineData("(> 1 2 \"x\")", "#f")]
    [InlineData("(= 1 2 \"x\")", "#f")]
    [InlineData("(expt \"x\" 1)", "\"x\"")]
    [InlineData("(expt \"x\" 0)", "1")]
    public void guiles_own_quirks_are_reproduced_not_corrected(string expression, string expected)
    {
        //Arrange / Act
        // An exact 1 is the universal multiplicative identity BEFORE any type check; a
        // one-argument comparison is #t unchecked; an n-ary comparison stops at the
        // first false pair without looking further; expt with an exact-integer exponent
        // is repeated multiplication, so 1 and 0 never touch the base. All measured.
        string result = Condition(expression);

        //Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("(quotient 4.0 2)", "2.0")]
    [InlineData("(remainder 5.0 3)", "2.0")]
    [InlineData("(modulo 5.0 3)", "2.0")]
    [InlineData("(gcd 4.0 2)", "2.0")]
    [InlineData("(lcm 4.0 6)", "12.0")]
    [InlineData("(odd? 2.0)", "#f")]
    [InlineData("(numerator 1.5)", "3.0")]
    [InlineData("(denominator 1.5)", "2.0")]
    [InlineData("(inexact->exact 1.5)", "3/2")]
    [InlineData("(integer? \"x\")", "#f")]
    [InlineData("(exact-integer? \"x\")", "#f")]
    [InlineData("(+)", "0")]
    [InlineData("(abs -3)", "3")]
    public void the_integer_family_takes_inexact_integers_and_the_predicates_stay_predicates(
        string expression, string expected)
    {
        //Arrange / Act
        string result = Eval(expression);

        //Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1+2i", "1.0+2.0i")]
    [InlineData("(* 2 1+2i)", "2.0+4.0i")]
    [InlineData("(- 1+2i)", "-1.0-2.0i")]
    [InlineData("+i", "0.0+1.0i")]
    [InlineData("(* 0 1+2i)", "0.0+0.0i")]
    [InlineData("(make-rectangular 1 0)", "1")]
    [InlineData("(make-rectangular 1.5 0)", "1.5")]
    [InlineData("(make-rectangular 1 0.0)", "1.0+0.0i")]
    [InlineData("(make-polar 1 0)", "1")]
    [InlineData("(sqrt -4)", "0.0+2.0i")]
    [InlineData("(sqrt (make-rectangular -3 4))", "1.0+2.0i")]
    [InlineData("(exp (make-rectangular 0 1))", "0.5403023058681398+0.8414709848078965i")]
    [InlineData("(expt (make-rectangular 0 1) 2)", "-1.0+0.0i")]
    [InlineData("(zero? (make-rectangular 0 0.0))", "#t")]
    [InlineData("(= 1+2i 1+2i)", "#t")]
    [InlineData("(inexact->exact (make-rectangular 3 0.0))", "3")]
    [InlineData("(exact->inexact (make-rectangular 3 1))", "3.0+1.0i")]
    [InlineData("(magnitude (make-rectangular 3 4))", "5.0")]
    [InlineData("(integer? (make-rectangular 3 0.0))", "#f")]
    public void complex_numbers_print_and_compute_as_guile_does(string expression, string expected)
    {
        //Arrange / Act
        // MEASURED on the pinned 2.27.2. Both parts of a complex print as inexact reals; an
        // EXACT zero imaginary part (or polar angle) is no complex at all; a negative real
        // and a complex have complex square roots; the transcendental functions compute in
        // the complex plane; zero? and = take a complex; inexact->exact of a zero imaginary
        // part is the exact real.
        string result = Eval(expression);

        //Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("(< (make-rectangular 3 1) 4)", "(wrong-type-arg \"<\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    [InlineData("(max (make-rectangular 3 1) 4)", "(wrong-type-arg \"max\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    [InlineData("(abs (make-rectangular 3 1))", "(wrong-type-arg \"abs\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    [InlineData("(floor (make-rectangular 3 1))", "(wrong-type-arg \"floor\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    [InlineData("(positive? (make-rectangular 3 1))", "(wrong-type-arg \"positive?\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    [InlineData("(inexact->exact (make-rectangular 3 1))", "(wrong-type-arg \"inexact->exact\" \"Wrong type argument in position ~A: ~S\" (1 3.0+1.0i) (3.0+1.0i))")]
    public void a_real_only_operation_refuses_a_complex_with_the_positioned_error(string expression, string expected)
    {
        //Arrange / Act
        string condition = Condition(expression);

        //Assert
        condition.Should().Be(expected);
    }

    [Fact]
    public void no_numeric_primitive_lets_a_host_exception_escape()
    {
        //Arrange
        // The class of defect WrongTypeArgumentTests exists for, at the sites that used
        // to have it: each of these threw a raw .NET ArgumentException before 2026-08-28.
        string[] expressions =
        {
            "(max 1 \"x\")", "(abs \"x\")", "(expt \"x\" 2)", "(sqrt \"x\")", "(quotient \"x\" 2)",
            "(remainder \"x\" 1)", "(modulo 1 \"x\")", "(exact->inexact \"x\")", "(exp \"x\")",
            "(log \"x\")", "(sin \"x\")", "(atan \"x\" 1)", "(1+ \"x\")", "(1- \"x\")",
            "(odd? \"x\")", "(ash \"x\" 1)", "(logand 1 \"x\")",
        };

        //Act / Assert
        foreach (string expression in expressions)
        {
            Condition(expression).Should().StartWith("(wrong-type-arg ");
        }
    }
}
