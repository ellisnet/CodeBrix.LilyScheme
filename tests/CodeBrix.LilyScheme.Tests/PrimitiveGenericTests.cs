using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The LS-FIX3 mechanisms: extending a generic-capable PRIMITIVE globally, the setter a
/// GOOPS <c>#:accessor</c> carries, the renaming half of a <c>use-modules</c>
/// <c>#:select</c>, and the Guile-core names those three exposed as missing.
/// </summary>
public class PrimitiveGenericTests
{
    /// <summary>
    /// Boots an interpreter with psyntax plus the prelude and evaluates every source in
    /// turn, returning the written form of the last result.
    /// </summary>
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

    /// <summary>The class and two instances every arithmetic test below specializes on.</summary>
    private const string BoxClass =
        "(define-class <Box> () (v #:init-keyword #:v #:accessor box-v))"
        + "(define b1 (make <Box> #:v 10))"
        + "(define b2 (make <Box> #:v 4))";

    [Fact]
    public void a_method_on_a_primitive_is_visible_from_another_module()
    {
        //Arrange
        // THE regression this mechanism exists for. Upstream's add-method! calls
        // enable-primitive-generic! on a generic-capable subr, which hangs the generic off
        // the PRIMITIVE — one object every module shares. Defining a fresh generic in the
        // defining module instead reads identically from that module and leaves every other
        // one resolving the raw numeric '-', which is what made (- pitch pitch) throw
        // wrong-type-arg from parser scopes that never loaded lily/operators.scm.
        //Act
        string result = Eval(
            BoxClass,
            "(define-module (lsfix3 defining))",
            "(define-method (- (a <Box>) (b <Box>)) (make <Box> #:v (- (box-v a) (box-v b))))",
            "(define-module (lsfix3 using))",
            "(box-v (- b1 b2))");

        //Assert
        result.Should().Be("6");
    }

    [Fact]
    public void specializing_a_primitive_leaves_ordinary_arithmetic_working()
    {
        //Arrange
        // The primitive stays the default: no method accepts two numbers, so subtraction
        // falls straight through to the subr. Losing this would break every other caller.
        //Act
        string result = Eval(
            BoxClass,
            "(define-method (- (a <Box>) (b <Box>)) (make <Box> #:v (- (box-v a) (box-v b))))",
            "(list (- 10 4) (- 5) (- 20 3 2))");

        //Assert
        result.Should().Be("(6 -5 15)");
    }

    [Fact]
    public void a_method_on_a_primitive_does_not_shadow_it_with_a_module_binding()
    {
        //Arrange
        // The extension is a property of the subr, so '-' still answers to the primitive
        // predicate afterwards rather than having become some other kind of object.
        //Act
        string result = Eval(
            BoxClass,
            "(define-method (- (a <Box>) (b <Box>)) b1)",
            "(generic-capability? -)");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void generic_capability_follows_guiles_own_declarations()
    {
        //Arrange
        // Guile declares a fixed set with SCM_PRIMITIVE_GENERIC (plus display and write
        // through the older SCM_GPROC). 'car' is not in it; '+' is.
        //Act
        string result = Eval("(list (generic-capability? +) (generic-capability? car))");

        //Assert
        result.Should().Be("(#t #f)");
    }

    [Fact]
    public void a_specialized_primitive_dispatches_on_argument_count_too()
    {
        //Arrange
        // lily/operators.scm specializes both (- a b) and the one-argument negation, so
        // selection has to distinguish them by arity as well as by class.
        //Act
        string result = Eval(
            BoxClass,
            "(define-method (- (a <Box>) (b <Box>)) (make <Box> #:v (- (box-v a) (box-v b))))",
            "(define-method (- (a <Box>)) (make <Box> #:v (- (box-v a))))",
            "(list (box-v (- b1 b2)) (box-v (- b1)))");

        //Assert
        result.Should().Be("(6 -10)");
    }

    [Fact]
    public void an_accessor_carries_a_setter_for_generalized_assignment()
    {
        //Arrange
        // GOOPS's #:accessor makes an <accessor>, a generic whose setter is a generic, so
        // (set! (acc obj) v) works. A bare lambda reads identically everywhere the accessor
        // is only called — and part-combiner.scm's (set! (split-index state) idx) then
        // throws wrong-type-arg on `setter`.
        //Act
        string result = Eval(
            BoxClass,
            "(set! (box-v b1) 99)",
            "(list (box-v b1) (procedure-with-setter? box-v))");

        //Assert
        result.Should().Be("(99 #t)");
    }

    [Fact]
    public void an_accessor_still_reads_and_writes_by_argument_count()
    {
        //Arrange
        // The dual-arity form predates the setter and stays: one procedure that reads with
        // one argument and writes with two.
        //Act
        string result = Eval(BoxClass, "(box-v b2 7)", "(box-v b2)");

        //Assert
        result.Should().Be("7");
    }

    [Fact]
    public void use_modules_binds_a_renamed_select()
    {
        //Arrange
        // scm/lily.scm opens with ((ice-9 format) #:select ((format . ice9-format))) and
        // then calls ice9-format from stencil.scm. A bare #:select needs nothing, because
        // the whole module is imported anyway — but a rename binds a name that exists
        // nowhere else, so dropping it leaves that name unbound with no diagnostic.
        //Act
        string result = Eval(
            "(use-modules ((srfi srfi-1) #:select ((fold . srfi-fold))))",
            "(srfi-fold + 0 '(1 2 3 4))");

        //Assert
        result.Should().Be("10");
    }

    [Fact]
    public void the_integer_division_family_rounds_each_named_way()
    {
        //Arrange
        // lily-library.scm picks between ceiling-quotient and floor-quotient in one `if`,
        // so both arms are evaluated as variable references and both names must exist.
        //Act
        string result = Eval(
            "(list (floor-quotient 7 2) (ceiling-quotient 7 2)"
            + " (floor-quotient -7 2) (ceiling-quotient -7 2)"
            + " (truncate-quotient -7 2))");

        //Assert
        result.Should().Be("(3 4 -4 -3 -3)");
    }

    [Fact]
    public void euclidean_remainder_is_never_negative()
    {
        //Arrange
        // The defining property: whatever the signs, the remainder lands in [0, |d|).
        // auto-beam.scm positions beams inside a measure with it.
        //Act
        string result = Eval(
            "(list (euclidean-remainder 7 2) (euclidean-remainder -7 2)"
            + " (euclidean-remainder 7 -2) (euclidean-remainder -7 -2))");

        //Assert
        result.Should().Be("(1 1 1 1)");
    }

    [Fact]
    public void integer_division_keeps_its_identity_over_ratios()
    {
        //Arrange
        // dividend = divisor * quotient + remainder has to hold exactly, which is why the
        // remainder is derived from the quotient rather than computed on its own.
        //Act
        string result = Eval(
            "(list (floor-quotient 7/2 3/4) (floor-remainder 7/2 3/4)"
            + " (+ (* 3/4 (floor-quotient 7/2 3/4)) (floor-remainder 7/2 3/4)))");

        //Assert
        result.Should().Be("(4 1/2 7/2)");
    }

    [Fact]
    public void integer_division_returns_both_parts_from_the_slash_form()
    {
        //Arrange & Act
        string result = Eval("(call-with-values (lambda () (floor/ -7 2)) list)");

        //Assert
        result.Should().Be("(-4 1)");
    }

    [Fact]
    public void finite_answers_for_the_whole_tower_and_refuses_a_non_number()
    {
        //Arrange
        // Guile's finite? REQUIRES a number and raises on anything else, where nan? and
        // inf? simply answer #f. Every exact number is finite by construction.
        //Act
        string result = Eval(
            "(list (finite? 1) (finite? 1/2) (finite? 1.5) (finite? (/ 1.0 0.0))"
            + " (catch 'wrong-type-arg (lambda () (finite? 'x)) (lambda (k . a) 'refused)))");

        //Assert
        result.Should().Be("(#t #t #t #f refused)");
    }

    [Fact]
    public void string_capitalize_titles_every_word()
    {
        //Arrange
        // Guile upcases the first character of each run of alphanumerics and downcases the
        // rest of it. chord-name.scm titles chord names with it.
        //Act
        string result = Eval("(string-capitalize \"hello wORLD of mUSIC\")");

        //Assert
        result.Should().Be("\"Hello World Of Music\"");
    }

    [Fact]
    public void substring_shared_slices_like_substring()
    {
        //Arrange
        // Sharing storage is an optimization SRFI-13 permits, never a promise; what the
        // callers in lily-library.scm need is the slice and the name.
        //Act
        string result = Eval(
            "(list (substring/shared \"abcdef\" 2) (substring/shared \"abcdef\" 1 3))");

        //Assert
        result.Should().Be("(\"cdef\" \"bc\")");
    }
}
