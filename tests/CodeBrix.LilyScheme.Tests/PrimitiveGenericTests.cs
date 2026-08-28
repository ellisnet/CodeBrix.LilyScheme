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
    public void the_primitive_runs_first_and_a_method_on_its_own_domain_is_never_consulted()
    {
        //Arrange / Act
        // GUILE'S ORDER, MEASURED on the pinned 2.27.2: with a method specialized on
        // <integer> attached to max, (max 1 2) is still 2 -- scm_max ran and the generic
        // was never asked. The method exists only for arguments the primitive refuses.
        string result = Eval(
            "(define-method (max (a <integer>) (b <integer>)) 'intercepted)",
            "(list (max 1 2) (max 1 2 3))");

        //Assert
        result.Should().Be("(2 3)");
    }

    [Fact]
    public void a_type_failure_falls_over_to_the_generic_and_a_miss_is_guiles_goops_error()
    {
        //Arrange / Act
        // SCM_WTA_DISPATCH_2: the primitive's own type check fails, the attached generic
        // is consulted, and with no applicable method Guile raises
        // (goops-error #f "No applicable method for ~S in call ~S" (GENERIC CALL) ())
        // -- the generic printed with its method count, the call as a list, and an EMPTY
        // LIST for data. Measured character for character.
        string result = Eval(
            BoxClass,
            "(define-method (- (a <Box>) (b <Box>)) b1)",
            "(catch #t (lambda () (- b1 \"x\")) (lambda (key . args) (cons key args)))");

        //Assert
        result.Should().Be(
            "(goops-error #f \"No applicable method for ~S in call ~S\" (#<<generic> - (1)> (- #<<Box>> \"x\")) ())");
    }

    [Fact]
    public void an_n_ary_call_reports_the_pair_that_failed()
    {
        //Arrange / Act
        // Guile folds pairwise, so the call in the error is the PAIR: (+ 1 2 b) fails as
        // (+ 3 b), and (+ b b b) with a two-Box method fails on the METHOD'S RESULT paired
        // with the third argument.
        string result = Eval(
            BoxClass,
            "(define-method (+ (a <Box>) (b <Box>)) 'box-plus)",
            "(list (catch #t (lambda () (+ 1 2 b1)) (lambda (key . args) (cadr (caddr args))))"
            + " (catch #t (lambda () (+ b1 b2 b1)) (lambda (key . args) (cadr (caddr args)))))");

        //Assert
        result.Should().Be("((+ 3 #<<Box>>) (+ box-plus #<<Box>>))");
    }

    [Fact]
    public void one_plus_and_one_minus_go_through_the_generic_as_plus_and_minus()
    {
        //Arrange / Act
        // 1+ IS (+ x 1) in Guile, generic and all: the Box method does not apply to
        // (b1 1), and the reported call names + with that pair.
        string result = Eval(
            BoxClass,
            "(define-method (+ (a <Box>) (b <Box>)) 'box-plus)",
            "(catch #t (lambda () (1+ b1)) (lambda (key . args) (cadr (caddr args))))");

        //Assert
        result.Should().Be("(+ #<<Box>> 1)");
    }

    [Fact]
    public void arity_is_checked_before_any_dispatch()
    {
        //Arrange / Act
        // (abs b1 b2) with a one-argument Box method on abs: Guile refuses the CALL by
        // arity -- vm_error_wrong_num_args naming the procedure -- and never dispatches.
        string result = Eval(
            BoxClass,
            "(define-method (abs (a <Box>)) 'abs-box)",
            "(list (abs b1) (abs -2)"
            + " (catch #t (lambda () (abs b1 b2)) (lambda (key . args) (list key (car args) (cadr args)))))");

        //Assert
        result.Should().Be("(abs-box 2 (wrong-number-of-args #f \"Wrong number of arguments to ~A\"))");
    }

    [Fact]
    public void a_generic_with_no_applicable_method_names_itself_and_the_call()
    {
        //Arrange / Act
        string result = Eval(
            "(define-generic bar)",
            "(list (format #f \"~s\" bar)"
            + " (catch #t (lambda () (bar 1 2)) (lambda (key . args) (cons key args))))");

        //Assert
        result.Should().Be(
            "(\"#<<generic> bar (0)>\" (goops-error #f \"No applicable method for ~S in call ~S\" (#<<generic> bar (0)> (bar 1 2)) ()))");
    }

    [Fact]
    public void define_method_on_a_plain_procedure_is_refused_as_goops_refuses_it()
    {
        //Arrange / Act
        // MEASURED: a name holding an ordinary procedure is not silently turned into a
        // generic with that procedure as its default; add-method! on a <procedure> that is
        // not generic-capable is (goops-error #f "~S is not a valid generic function" (proc) ()).
        string result = Eval(
            BoxClass,
            "(define (qux x) (list 'plain x))",
            "(catch #t (lambda () (define-method (qux (x <Box>)) 'box-qux) 'defined)"
            + " (lambda (key . args) (list key (car args) (cadr args) (cadddr args))))");

        //Assert
        result.Should().Be("(goops-error #f \"~S is not a valid generic function\" ())");
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
