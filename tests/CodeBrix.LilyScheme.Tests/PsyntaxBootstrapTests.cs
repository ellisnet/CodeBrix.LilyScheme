using System;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The milestone-2 gate: Guile's macro expander, loaded from the pre-expanded
/// <c>psyntax-pp.scm</c>, running on the C# core and producing Tree-IL that the
/// Tree-IL evaluator executes.
/// </summary>
public class PsyntaxBootstrapTests
{
    /// <summary>
    /// Boots an interpreter with psyntax plus the prelude and evaluates one form through
    /// the full pipeline: read, macroexpand, evaluate Tree-IL.
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
                object form = SchemeReader.ReadAll(source, "<test>")[0];
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void psyntax_loads_from_the_embedded_resource()
    {
        //Arrange
        int formCount = 0;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            formCount = SchemeBootstrap.LoadPsyntax(interpreter);
        });

        //Assert -- psyntax-pp.scm is 17 top-level forms
        formCount.Should().Be(17);
    }

    [Fact]
    public void macroexpand_returns_tree_il_structs_not_s_expressions()
    {
        //Arrange
        object expanded = null;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadPsyntax(interpreter);
            expanded = interpreter.EvalString("(macroexpand '(if 1 2 3))", "<test>");
        });

        //Assert
        expanded.Should().BeOfType<SchemeStruct>();
        ((SchemeStruct)expanded).Vtable.Name.Should().Be("conditional");
    }

    [Fact]
    public void evaluates_a_lambda_application_through_tree_il()
        => Eval("((lambda (x) (* x x)) 7)").Should().Be("49");

    [Fact]
    public void evaluates_a_recursive_definition()
        => Eval("(define (fact n) (if (= n 0) 1 (* n (fact (- n 1)))))", "(fact 20)")
            .Should().Be("2432902008176640000");

    [Fact]
    public void tree_il_tail_calls_do_not_grow_the_stack()
        => Eval("(let loop ((i 0)) (if (< i 200000) (loop (+ i 1)) i))").Should().Be("200000");

    // ---- the acceptance criterion -------------------------------------------------

    [Fact]
    public void syntax_rules_macro_expands_and_runs()
    {
        //Arrange / Act
        string result = Eval(
            "(define-syntax swap! (syntax-rules () ((_ a b) (let ((t a)) (set! a b) (set! b t)))))",
            "(let ((x 1) (y 2)) (swap! x y) (list x y))");

        //Assert
        result.Should().Be("(2 1)");
    }

    [Fact]
    public void macro_expansion_is_hygienic()
    {
        //Arrange -- swap! uses an internal binding named t, and so does the caller.
        //Without hygiene the macro's t would capture the user's t and the swap would
        //silently produce (5 5) or (6 6).

        //Act
        string result = Eval(
            "(define-syntax swap! (syntax-rules () ((_ a b) (let ((t a)) (set! a b) (set! b t)))))",
            "(let ((t 5) (u 6)) (swap! t u) (list t u))");

        //Assert
        result.Should().Be("(6 5)");
    }

    [Fact]
    public void recursive_syntax_rules_macro_works()
        => Eval(
                "(define-syntax my-or (syntax-rules () ((_) #f) ((_ e) e)"
                + " ((_ e r ...) (let ((v e)) (if v v (my-or r ...))))))",
                "(my-or #f #f 42)")
            .Should().Be("42");

    [Fact]
    public void a_recursive_macro_does_not_capture_a_users_binding()
        => Eval(
                "(define-syntax my-or (syntax-rules () ((_) #f) ((_ e) e)"
                + " ((_ e r ...) (let ((v e)) (if v v (my-or r ...))))))",
                "(let ((v 'captured)) (my-or #f v))")
            .Should().Be("captured");

    [Fact]
    public void syntax_case_with_a_syntax_template_works()
        => Eval("(define-syntax inc (lambda (x) (syntax-case x () ((_ n) #'(+ n 1)))))", "(inc 41)")
            .Should().Be("42");

    [Fact]
    public void define_star_supports_optional_and_keyword_arguments()
        => Eval(
                "(define* (g a #:optional (b 10) #:key (c 100)) (list a b c))",
                "(g 1 2 #:c 3)")
            .Should().Be("(1 2 3)");

    // ---- prelude-derived syntax ---------------------------------------------------

    [Fact]
    public void cond_with_an_arrow_clause_applies_the_receiver()
        => Eval("(cond ((assv 2 '((1 a) (2 b))) => cadr) (else 'none))").Should().Be("b");

    [Fact]
    public void case_selects_on_membership()
        => Eval("(case 3 ((1 2) 'low) ((3 4) 'mid) (else 'high))").Should().Be("mid");

    [Fact]
    public void do_loops_until_its_test_holds()
        => Eval("(do ((i 0 (+ i 1)) (acc '() (cons i acc))) ((= i 4) (reverse acc)))")
            .Should().Be("(0 1 2 3)");

    [Fact]
    public void let_values_destructures_multiple_values()
        => Eval("(let-values (((q r) (values 3 4))) (list q r))").Should().Be("(3 4)");

    [Fact]
    public void define_values_binds_each_name()
        => Eval("(define-values (a b) (values 7 8))", "(+ a b)").Should().Be("15");

    [Fact]
    public void and_and_or_short_circuit()
    {
        //Arrange / Act / Assert
        Eval("(and 1 2 3)").Should().Be("3");
        Eval("(and 1 #f 3)").Should().Be("#f");
        Eval("(or #f #f 3)").Should().Be("3");
    }

    [Fact]
    public void defmacro_defines_an_unhygienic_macro()
    {
        //Arrange -- LilyPond's define-markup-command is a defmacro, so this path matters

        //Act
        string result = Eval(
            "(defmacro my-when (test . body) `(if ,test (begin ,@body) #f))",
            "(my-when #t 'fired)");

        //Assert
        result.Should().Be("fired");
    }

    [Fact]
    public void parameterize_rebinds_for_the_dynamic_extent()
        => Eval("(define p (make-parameter 10))", "(parameterize ((p 20)) (p))").Should().Be("20");

    // ---- vendored Guile modules ---------------------------------------------------

    [Fact]
    public void the_vendored_srfi_1_module_loads_and_works()
    {
        //Arrange
        string result = null;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeBootstrap.LoadExpanded(
                interpreter, SchemeBootstrap.ReadVendoredSource("srfi-1.scm"), "srfi-1.scm");
            object form = SchemeReader.ReadAll("(fold + 0 '(1 2 3 4))", "<test>")[0];
            result = Printer.Write(interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
        });

        //Assert
        result.Should().Be("10");
    }

    [Fact]
    public void the_vendored_ice9_match_module_loads_and_works()
    {
        //Arrange
        string result = null;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeBootstrap.LoadExpanded(
                interpreter,
                SchemeBootstrap.ReadVendoredSource("match.upstream.scm"),
                "match.upstream.scm");
            object form = SchemeReader.ReadAll("(match '(1 2 3) ((a b c) (+ a b c)))", "<test>")[0];
            result = Printer.Write(interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
        });

        //Assert -- ice-9 match is ~950 lines of portable syntax-rules, so this passing
        //is a broad statement about the expander rather than about one macro
        result.Should().Be("6");
    }

    [Fact]
    public void the_bootstrap_reports_the_expected_form_count()
    {
        //Arrange
        int forms = 0;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            forms = SchemeBootstrap.LoadCore(interpreter);
        });

        //Assert -- 17 from psyntax-pp plus the prelude's top-level forms
        forms.Should().BeGreaterThan(17);
    }
}
