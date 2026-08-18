using System;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Exercises the core evaluator directly, without psyntax. These cover the layer that
/// has to work before the macro expander can even be loaded.
/// </summary>
public class InterpreterTests
{
    private static string Eval(string source)
    {
        //Arrange
        string result = null;

        //Act -- deep recursion needs the large stack, so run on the dedicated thread
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            result = Printer.Write(interpreter.EvalString(source, "<test>"));
        });

        //Assert is left to the caller
        return result;
    }

    [Fact]
    public void evaluates_arithmetic()
        => Eval("(+ 1 2 3)").Should().Be("6");

    [Fact]
    public void integer_division_stays_exact()
        => Eval("(/ 1 3)").Should().Be("1/3");

    [Fact]
    public void arithmetic_promotes_to_bignum()
        => Eval("(* 99999999999 99999999999)").Should().Be("9999999999800000000001");

    [Fact]
    public void mixing_exact_and_inexact_yields_inexact()
        => Eval("(+ 1 2.5)").Should().Be("3.5");

    [Fact]
    public void car_and_cdr_walk_a_list()
        => Eval("(cadr '(1 2 3))").Should().Be("2");

    [Fact]
    public void only_false_is_false()
    {
        //Arrange / Act / Assert -- the empty list and zero are both true in Scheme
        Eval("(if '() 'yes 'no)").Should().Be("yes");
        Eval("(if 0 'yes 'no)").Should().Be("yes");
        Eval("(if #f 'yes 'no)").Should().Be("no");
    }

    [Fact]
    public void elisp_nil_is_false_and_null()
    {
        //Arrange / Act / Assert
        Eval("(if #nil 'yes 'no)").Should().Be("no");
        Eval("(null? #nil)").Should().Be("#t");
    }

    [Fact]
    public void lambda_closes_over_its_environment()
        => Eval("(let ((n 10)) ((lambda (x) (+ x n)) 5))").Should().Be("15");

    [Fact]
    public void named_let_iterates()
        => Eval("(let loop ((i 0) (acc '())) (if (= i 3) (reverse acc) (loop (+ i 1) (cons i acc))))")
            .Should().Be("(0 1 2)");

    [Fact]
    public void tail_calls_do_not_grow_the_stack()
    {
        //Arrange / Act -- a quarter of a million frames would overflow without TCO
        string result = Eval("(let loop ((i 0)) (if (< i 250000) (loop (+ i 1)) i))");

        //Assert
        result.Should().Be("250000");
    }

    [Fact]
    public void letrec_makes_bindings_mutually_visible()
        => Eval("(letrec ((even2? (lambda (n) (if (= n 0) #t (odd2? (- n 1)))))"
                + " (odd2? (lambda (n) (if (= n 0) #f (even2? (- n 1)))))) (even2? 10))")
            .Should().Be("#t");

    [Fact]
    public void lambda_star_supports_optional_arguments()
        => Eval("((lambda* (a #:optional (b 10)) (+ a b)) 5)").Should().Be("15");

    [Fact]
    public void lambda_star_supports_keyword_arguments()
        => Eval("((lambda* (a #:key (b 10)) (list a b)) 1 #:b 2)").Should().Be("(1 2)");

    [Fact]
    public void a_dotted_parameter_list_collects_the_rest()
        => Eval("((lambda (a . rest) rest) 1 2 3)").Should().Be("(2 3)");

    [Fact]
    public void quasiquote_unquotes_and_splices()
        => Eval("`(1 ,(+ 1 1) ,@(list 3 4))").Should().Be("(1 2 3 4)");

    [Fact]
    public void equal_compares_structurally_while_eq_does_not()
    {
        //Arrange / Act / Assert
        Eval("(equal? (list 1 2) (list 1 2))").Should().Be("#t");
        Eval("(eq? (list 1 2) (list 1 2))").Should().Be("#f");
    }

    [Fact]
    public void eqv_distinguishes_exactness()
        => Eval("(eqv? 1 1.0)").Should().Be("#f");

    [Fact]
    public void strings_are_mutable()
        => Eval("(let ((s (make-string 3 #\\a))) (string-set! s 1 #\\b) s)").Should().Be("\"aba\"");

    [Fact]
    public void hash_tables_round_trip_values()
        => Eval("(let ((h (make-hash-table))) (hashq-set! h 'k 'v) (hashq-ref h 'k))").Should().Be("v");

    [Fact]
    public void gensym_produces_uninterned_symbols()
        => Eval("(eq? (gensym) (gensym))").Should().Be("#f");

    [Fact]
    public void a_struct_carries_its_vtable_and_fields()
        => Eval("(struct-ref (make-struct/simple (vector-ref %expanded-vtables 1) #f 42) 1)")
            .Should().Be("42");

    [Fact]
    public void expanded_vtables_has_the_eighteen_tree_il_node_types()
        => Eval("(vector-length %expanded-vtables)").Should().Be("18");

    [Fact]
    public void catch_intercepts_a_matching_throw()
        => Eval("(catch 'boom (lambda () (throw 'boom 1)) (lambda args 'caught))").Should().Be("caught");

    [Fact]
    public void with_throw_handler_runs_the_handler_and_lets_the_throw_propagate()
        => Eval("(let ((seen '()))"
                + " (catch 'boom"
                + "  (lambda () (with-throw-handler 'boom"
                + "              (lambda () (throw 'boom 1 2))"
                + "              (lambda (key . args) (set! seen (cons key args)))))"
                + "  (lambda args seen)))")
            .Should().Be("(boom 1 2)");

    [Fact]
    public void with_throw_handler_ignores_a_non_matching_key()
        => Eval("(let ((seen 'untouched))"
                + " (catch 'other"
                + "  (lambda () (with-throw-handler 'boom"
                + "              (lambda () (throw 'other))"
                + "              (lambda args (set! seen 'fired))))"
                + "  (lambda args seen)))")
            .Should().Be("untouched");

    [Fact]
    public void with_throw_handler_with_true_key_sees_every_throw()
        => Eval("(let ((seen 'quiet))"
                + " (catch #t"
                + "  (lambda () (with-throw-handler #t"
                + "              (lambda () (throw 'anything))"
                + "              (lambda args (set! seen 'fired))))"
                + "  (lambda args seen)))")
            .Should().Be("fired");

    [Fact]
    public void with_throw_handler_returns_the_thunk_value_when_nothing_throws()
        => Eval("(with-throw-handler 'boom (lambda () 42) (lambda args 'unused))")
            .Should().Be("42");

    [Fact]
    public void with_throw_handler_rejects_a_key_that_is_not_a_symbol_or_true()
        => Eval("(catch 'wrong-type-arg"
                + " (lambda () (with-throw-handler \"boom\" (lambda () 1) (lambda args 2)))"
                + " (lambda (key . args) key))")
            .Should().Be("wrong-type-arg");

    [Fact]
    public void a_throw_from_inside_the_handler_does_not_replace_the_original_throw()
        => Eval("(catch #t"
                + " (lambda () (with-throw-handler 'boom"
                + "             (lambda () (throw 'boom 'original))"
                + "             (lambda args (throw 'from-handler))))"
                + " (lambda (key . args) (cons key args)))")
            .Should().Be("(boom original)");

    [Fact]
    public void catch_runs_the_pre_unwind_handler_before_unwinding_and_before_the_handler()
        => Eval("(let ((order '()))"
                + " (catch 'x"
                + "  (lambda () (dynamic-wind"
                + "              (lambda () #t)"
                + "              (lambda () (throw 'x))"
                + "              (lambda () (set! order (cons 'wind order)))))"
                + "  (lambda args (set! order (cons 'handler order)) order)"
                + "  (lambda args (set! order (cons 'pre order)))))")
            .Should().Be("(handler wind pre)");

    [Fact]
    public void catch_treats_a_false_pre_unwind_handler_as_absent()
        => Eval("(catch 'boom (lambda () (throw 'boom)) (lambda args 'caught) #f)")
            .Should().Be("caught");

    [Fact]
    public void dynamic_wind_runs_the_after_thunk_on_unwind()
        => Eval("(let ((log '()))"
                + " (catch #t"
                + "  (lambda () (dynamic-wind (lambda () #t) (lambda () (throw 'x)) (lambda () (set! log 'ran))))"
                + "  (lambda args #f))"
                + " log)")
            .Should().Be("ran");

    [Fact]
    public void an_unbound_variable_throws_with_its_name()
    {
        //Arrange / Act
        Exception failure = Assert.ThrowsAny<Exception>(() => Eval("(no-such-binding)"));

        //Assert -- the interpreter thread's exception reaches us as itself, but a
        //primitive may still wrap a cause of its own, so walk to the innermost
        Exception cause = failure;
        while (cause.InnerException != null)
        {
            cause = cause.InnerException;
        }

        cause.Message.Should().Contain("no-such-binding");
    }
}
