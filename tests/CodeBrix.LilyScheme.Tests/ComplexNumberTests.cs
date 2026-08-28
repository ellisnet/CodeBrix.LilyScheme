using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>make-rectangular</c> and the narrow complex value it returns.
/// <para>
/// The narrowness is the design and is what these facts pin down: only four procedures
/// understand a complex, and arithmetic REFUSES one rather than quietly using its real
/// part. A half-built numeric tower that coerced instead of refusing would turn every
/// misuse into a plausible wrong number, which is exactly the failure this interpreter's
/// callers cannot detect.
/// </para>
/// </summary>
public class ComplexNumberTests
{
    /// <summary>Boots an interpreter and returns the written form of the last result.</summary>
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

    [Fact]
    public void magnitude_of_a_rectangular_complex_is_the_length_of_the_vector()
    {
        //Arrange & Act
        // The 3-4-5 triangle, so the expected value is exact and hand-checkable. This IS
        // the call LilyPond makes: scm/stencil.scm and scm/define-markup-commands.scm
        // both write (magnitude (make-rectangular dx dy)) to mean a vector's length.
        string result = Eval("(magnitude (make-rectangular 3 4))");

        //Assert
        result.Should().Be("5.0");
    }

    [Fact]
    public void a_complex_with_no_imaginary_part_still_has_its_real_magnitude()
    {
        //Arrange & Act
        string result = Eval("(magnitude (make-rectangular -7 0))");

        //Assert
        // Negative real, zero imaginary: magnitude is the absolute value, which is also
        // what the real-only branch would have answered. The two branches must agree here
        // or a zero-length vector would change answer depending on how it was built.
        result.Should().Be("7.0");
    }

    [Fact]
    public void the_parts_of_a_complex_come_back_out()
    {
        //Arrange & Act & Assert
        Eval("(real-part (make-rectangular 3 4))").Should().Be("3.0");
        Eval("(imag-part (make-rectangular 3 4))").Should().Be("4.0");
    }

    [Fact]
    public void angle_reads_the_quadrant_from_both_parts()
    {
        //Arrange & Act
        // Straight up the imaginary axis: pi/2. atan2 needs BOTH parts to answer this,
        // so a complex that lost its imaginary part would say 0 instead.
        string result = Eval("(< 1.5707 (angle (make-rectangular 0 1)) 1.5709)");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void a_real_number_keeps_its_own_answers_from_the_same_four_procedures()
    {
        //Arrange & Act & Assert
        // The real-only branches these four had before must be untouched: every existing
        // caller passes a real, and there are far more of those than complex ones.
        //
        // magnitude answers 4.0 rather than Guile's exact 4, because its real branch has
        // always gone through ToDouble. That is PRE-EXISTING and deliberately left alone
        // here — this test pins the behaviour so the difference is recorded rather than
        // discovered, and changing it belongs to a session that can sweep behind it.
        Eval("(magnitude -4)").Should().Be("4.0");
        Eval("(real-part 5)").Should().Be("5");
        Eval("(imag-part 5)").Should().Be("0");
        Eval("(angle 3)").Should().Be("0.0");
    }

    [Fact]
    public void a_complex_is_a_number_but_is_not_a_real_one()
    {
        //Arrange & Act & Assert
        // REWRITTEN 2026-08-09. This test used to assert (number? 3+4i) => #f, which is
        // not Guile's answer — number? and complex? are the same predicate there. It said
        // so, and gave the reason: arithmetic did not accept a complex, so number? saying
        // yes would have promised what the tower could not deliver. Arithmetic accepts
        // one now, so the fence went from recording a limit to defending a divergence.
        //
        // real? and rational? are fenced on BOTH sides in the same test, because the
        // obvious way to widen number? is to widen the predicate all four share — and
        // that would take real? along with it, silently.
        Eval("(number? (make-rectangular 3 4))").Should().Be("#t");
        Eval("(complex? (make-rectangular 3 4))").Should().Be("#t");
        Eval("(real? (make-rectangular 3 4))").Should().Be("#f");
        Eval("(rational? (make-rectangular 3 4))").Should().Be("#f");
        Eval("(integer? (make-rectangular 3 0.0))").Should().Be("#f");

        Eval("(number? 3)").Should().Be("#t");
        Eval("(complex? 3)").Should().Be("#t");
        Eval("(real? 3)").Should().Be("#t");
        Eval("(rational? 3)").Should().Be("#t");
    }

    [Fact]
    public void the_rectangular_literal_reads_as_a_complex()
    {
        //Arrange & Act & Assert
        // Guile's own syntax. Every one of these is a form scm/stencil.scm's
        // arrow-stencil-maker contains literally, and until 2026-08-09 each read as a
        // SYMBOL and died as an unbound variable the moment an arrow was drawn.
        Eval("(imag-part 0+1i)").Should().Be("1.0");
        Eval("(real-part -1+0.25i)").Should().Be("-1.0");
        Eval("(imag-part -1+0.25i)").Should().Be("0.25");
        Eval("(imag-part -1-0.25i)").Should().Be("-0.25");

        // The bare unit imaginary, sign and all, with no digits.
        Eval("(imag-part +i)").Should().Be("1.0");
        Eval("(imag-part -i)").Should().Be("-1.0");
    }

    [Fact]
    public void an_exact_zero_imaginary_part_collapses_to_the_real()
    {
        //Arrange & Act & Assert
        // Guile reads 1+0i AS THE EXACT INTEGER 1 — not as a complex that happens to have
        // no imaginary part. stencil.scm binds e_x to it and multiplies coordinates by
        // it, so a version that kept the zero would make every arrow coordinate inexact
        // and every arrow polygon differ from the oracle in its last digits.
        Eval("1+0i").Should().Be("1");
        Eval("(exact? 1+0i)").Should().Be("#t");
        Eval("(real? 1+0i)").Should().Be("#t");

        // The control, and it is the half that makes the rule a rule: an INEXACT zero
        // imaginary part does NOT collapse.
        Eval("(real? 1.0+0.0i)").Should().Be("#f");
        Eval("(complex? 1.0+0.0i)").Should().Be("#t");
    }

    [Fact]
    public void tokens_that_only_look_like_complex_literals_stay_symbols()
    {
        //Arrange & Act & Assert
        // The reader change must not capture ordinary names. `pi' and `midi' end in a
        // letter that is not `i'; `xi' ends in one that is, and neither starts numeric.
        // A reader that grabbed these would break the init layer everywhere at once.
        Eval("(symbol? 'pi)").Should().Be("#t");
        Eval("(symbol? 'midi)").Should().Be("#t");
        Eval("(symbol? 'xi)").Should().Be("#t");
        Eval("(symbol? '1+2)").Should().Be("#t");

        // And the exponent forms must still read as REALS rather than splitting at the
        // sign inside the exponent.
        Eval("(real? 1e-5)").Should().Be("#t");
        Eval("(real? -2.5e+3)").Should().Be("#t");
    }

    [Fact]
    public void complex_arithmetic_follows_the_ordinary_rules()
    {
        //Arrange & Act & Assert
        // Every expected value hand-computed. (1+2i)(3+4i) = 3+4i+6i+8i^2 = -5+10i.
        Eval("(real-part (* 1+2i 3+4i))").Should().Be("-5.0");
        Eval("(imag-part (* 1+2i 3+4i))").Should().Be("10.0");

        Eval("(real-part (+ 1+2i 3+4i))").Should().Be("4.0");
        Eval("(imag-part (- 1+2i 3+4i))").Should().Be("-2.0");

        // (-5+10i)/(1+2i) = 3+4i — the multiplication above, run backwards.
        Eval("(real-part (/ -5+10i 1+2i))").Should().Be("3.0");
        Eval("(imag-part (/ -5+10i 1+2i))").Should().Be("4.0");

        // A real operand widens rather than being refused.
        Eval("(imag-part (* 2 0+1i))").Should().Be("2.0");

        // (make-polar 1 ang) times z rotates z, which is the whole of stencil.scm's
        // `rotate'. A quarter turn takes 1 to i.
        Eval("(< 0.999 (imag-part (* (make-polar 1 (/ (* 4 (atan 1)) 2)) 1)) 1.001)")
            .Should().Be("#t");
    }

    [Fact]
    public void a_product_with_an_exact_zero_is_the_computed_inexact_complex()
    {
        //Arrange & Act & Assert
        // MEASURED on the pinned 2.27.2 (Guile 3.0): an exact zero factor does NOT
        // short-circuit a complex product -- (* 0 1+2i) is 0.0+0.0i and its real part is
        // 0.0. //was previously: the test asserted exact 0 as "Guile's rule", with the
        // claim that stencil.scm's arrow heads (the literal 0 rotated by a complex, read
        // back with real-part) depended on it; they run on 0.0 in LilyPond itself.
        Eval("(* 0 1+2i)").Should().Be("0.0+0.0i");
        Eval("(* 1+2i 0)").Should().Be("0.0+0.0i");
        Eval("(real-part (* 0 1+2i))").Should().Be("0.0");

        // An INEXACT zero, the same.
        Eval("(complex? (* 0.0 1+2i))").Should().Be("#t");

        // The REAL branch is unchanged and also Guile's: (* 0 1.5) is 0.0.
        Eval("(* 0 1.5)").Should().Be("0.0");
    }
}
