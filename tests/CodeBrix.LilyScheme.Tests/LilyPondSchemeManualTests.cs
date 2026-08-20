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
/// The pure-Scheme REPL examples of the "LilyPond's Scheme" manual (Urs Liska and
/// others, CC BY-SA 4.0, github.com/jeanas/lilyponds-scheme), replayed as sessions.
/// The manual is how LilyPond users LEARN this dialect, so its transcripts are a
/// user's-eye contract: every expression here is one the book shows with its answer.
/// Excluded on purpose: names the LilyPond layer supplies rather than Guile
/// (<c>red</c>, <c>color?</c>, <c>x11-color</c>, <c>fraction?</c>, <c>symbol-list?</c>,
/// <c>ly:*</c>) -- those belong to the consumer -- and the manual's Guile-1.8
/// renderings where modern Guile itself answers differently (procedure printing,
/// float precision), where the fence holds the MODERN form.
/// </summary>
public class LilyPondSchemeManualTests
{
    /// <summary>
    /// Boots one interpreter and evaluates the steps in order, asserting each step's
    /// written result when an expectation is given (<see langword="null"/> skips the
    /// assertion, for definitions). One session per test keeps the manual's
    /// define-then-use flow intact, and the failure message names the expression.
    /// </summary>
    private static void Session(params (string Expression, string Expected)[] steps)
    {
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach ((string expression, string expected) in steps)
            {
                string result = null;
                foreach (object form in SchemeReader.ReadAll(expression, "<manual>"))
                {
                    result = Printer.Write(
                        interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
                }

                if (expected != null)
                {
                    (expression + " => " + result).Should().Be(expression + " => " + expected);
                }
            }
        });
    }

    [Fact]
    public void expressions_evaluate_innermost_first()
    {
        //Arrange / Act / Assert (scheme/expressions.md)
        Session(
            ("5", "5"),
            ("\"I'm a string\"", "\"I'm a string\""),
            ("-1.6", "-1.6"),
            ("'(1 . \"Hello\")", "(1 . \"Hello\")"),
            ("(+ 12 17)", "29"),
            ("(string-append \"A\" \" \" \"B\")", "\"A B\""),
            ("(/ 100 2 2 5)", "5"),
            ("(+ (- 14 4) (* 3 (- 5 3)))", "16"),
            ("(with-output-to-string (lambda ()"
                + " (let* ((init-value (+ (- 14 4) (* 3 (- 5 3))))"
                + "        (processed-value (* (+ init-value 4) (- init-value 3))))"
                + "   (display processed-value))))", "\"260\""));
    }

    [Fact]
    public void numbers_keep_exactness_until_a_real_arrives()
    {
        //Arrange / Act / Assert (scheme/data-types/numbers.md)
        Session(
            ("(number? 4.2)", "#t"),
            ("(number? \"Hi\")", "#f"),
            ("(integer? 4.2)", "#f"),
            ("(real? 1.20389175)", "#t"),
            ("(+ 123 345)", "468"),
            ("(+ 3 1.0)", "4.0"),
            ("(/ 4 2)", "2"),
            ("(/ 10 4)", "5/2"),
            ("(/ 4 3)", "4/3"),
            // The manual's Guile 1.8 printed 1.33333333333333; modern Guile prints
            // the shortest round-tripping form, and that is what is fenced.
            ("(/ 4 3.0)", "1.3333333333333333"));
    }

    [Fact]
    public void the_reader_takes_the_manuals_dot_edge_cases()
    {
        //Arrange / Act / Assert (scheme/data-types/lists-and-pairs/creating-pairs.md)
        // Where the dot binds decides pair-versus-list: a dot flush against the next
        // token is part of a NUMBER (.2) or of a SYMBOL (red.), not a pair dot.
        Session(
            ("'(2 . 3.2)", "(2 . 3.2)"),
            ("'( 1 . 3/4     )", "(1 . 3/4)"),
            ("'(apple .2)", "(apple 0.2)"),
            ("'(1. 3)", "(1.0 3)"),
            ("'(red. 4)", "(red. 4)"),
            ("(cons 1 3/4)", "(1 . 3/4)"),
            ("(cons \"Hi\" 2.0)", "(\"Hi\" . 2.0)"),
            ("'(1 . (2 . (3 . (4 . ()))))", "(1 2 3 4)"),
            ("'(1 . (2 . (3 . 4)))", "(1 2 3 . 4)"),
            ("(cons 1 (cons 2 (cons 3 4)))", "(1 2 3 . 4)"),
            ("'(1 2 3 . 4)", "(1 2 3 . 4)"));
    }

    [Fact]
    public void accessor_shorthands_compose_like_their_spellings()
    {
        //Arrange / Act / Assert (scheme/data-types/lists-and-pairs/accessing-pairs.md)
        Session(
            ("(define b (cons '(3 . 4) \"5\"))", null),
            ("b", "((3 . 4) . \"5\")"),
            ("(caar b)", "3"),
            ("(cdar b)", "4"),
            ("(define d (cons (cons (cons (cons (cons 1 2) 3) 4) 5) 6))", null),
            ("d", "(((((1 . 2) . 3) . 4) . 5) . 6)"),
            ("(cdr d)", "6"),
            ("(cdar d)", "5"),
            ("(cdaar d)", "4"),
            ("(cdaaar d)", "3"),
            ("(car (caaaar d))", "1"),
            ("(caar (caaar d))", "1"),
            ("(caaar (caar d))", "1"),
            ("(caaaar (car d))", "1"));
    }

    [Fact]
    public void a_list_is_a_pair_chain_ending_in_nil()
    {
        //Arrange / Act / Assert (scheme/data-types/lists-and-pairs/structure.md)
        Session(
            ("(define lst (list 1 2 3 4))", null),
            ("(list? lst)", "#t"),
            ("(pair? lst)", "#t"),
            ("(define pr (cons 5 6))", null),
            ("(list? pr)", "#f"),
            ("(pair? pr)", "#t"),
            ("(define ll (list 4))", null),
            ("(car ll)", "4"),
            ("(cdr ll)", "()"),
            ("(define pl (cons 1 (cons 2 (cons 3 (cons 4 '())))))", null),
            ("pl", "(1 2 3 4)"),
            ("(pair? '(red blue))", "#t"),
            ("(list? '(red . blue))", "#f"));
    }

    [Fact]
    public void append_attaches_its_last_argument_as_it_stands()
    {
        //Arrange / Act / Assert (structure.md and lists/extend-reverse.md)
        // Only the arguments BEFORE the last must be proper lists; the last is
        // attached unchanged, so a non-list last argument makes an improper list.
        Session(
            ("(define a '(1 2 3))", null),
            ("(define b '(4 5 6))", null),
            ("(define c '(a b c))", null),
            ("(append a c b)", "(1 2 3 a b c 4 5 6)"),
            ("(append '(1 2 3) '(4))", "(1 2 3 4)"),
            ("(append '(1 2 3 4) (cons 5 6))", "(1 2 3 4 5 . 6)"),
            ("(append '(1 2 3 4) 5)", "(1 2 3 4 . 5)"),
            ("(reverse '(1 2 3 4))", "(4 3 2 1)"),
            // The manual's remove-the-last-element idiom.
            ("(reverse (cdr (reverse '(1 2 3 4))))", "(1 2 3)"));
    }

    [Fact]
    public void quote_prevents_evaluation_and_quasiquote_splices()
    {
        //Arrange / Act / Assert (scheme/quoting)
        Session(
            ("(quote red)", "red"),
            ("'violet", "violet"),
            // Quoting a PROCEDURE name answers the symbol, not the procedure.
            ("'random", "random"),
            ("(list 'red 'green 'blue)", "(red green blue)"),
            ("(quote (red green blue))", "(red green blue)"),
            ("(quote (1 . 2))", "(1 . 2)"),
            ("`(red random 1)", "(red random 1)"),
            ("`(1 2 ,(list 3 4) 5)", "(1 2 (3 4) 5)"),
            ("`(1 2 ,@(list 3 4) 5)", "(1 2 3 4 5)"));
    }

    [Fact]
    public void alists_grow_with_acons_and_answer_the_first_match()
    {
        //Arrange / Act / Assert (scheme/alists)
        Session(
            ("(define bool-alist '((subdivide . #t) (use-color . #f)))", null),
            // acons answers a NEW alist; the variable only moves through set!.
            ("(acons 'debug #f bool-alist)", "((debug . #f) (subdivide . #t) (use-color . #f))"),
            ("bool-alist", "((subdivide . #t) (use-color . #f))"),
            ("(set! bool-alist (acons 'debug #f bool-alist))", null),
            ("(set! bool-alist (acons 'subdivide #f bool-alist))", null),
            ("bool-alist", "((subdivide . #f) (debug . #f) (subdivide . #t) (use-color . #f))"),
            // The FIRST match shadows the stale entry deeper in.
            ("(assq 'subdivide bool-alist)", "(subdivide . #f)"),
            ("(assq-set! bool-alist 'debug #t)",
                "((subdivide . #f) (debug . #t) (subdivide . #t) (use-color . #f))"),
            // assq answers the whole ENTRY or #f; assq-ref answers the VALUE or #f --
            // which makes a stored #f indistinguishable from a missing key.
            ("(assq 'use-color bool-alist)", "(use-color . #f)"),
            ("(assq 'use-colors bool-alist)", "#f"),
            ("(assq-ref bool-alist 'use-color)", "#f"),
            ("(assq-ref bool-alist 'use-colors)", "#f"),
            ("(cdr (assq 'subdivide bool-alist))", "#f"),
            ("(define al '((col1 . 1) (col2 . 2) (col1 . 3)))", null),
            ("(assq 'col1 al)", "(col1 . 1)"));
    }

    [Fact]
    public void map_walks_lists_in_step_and_stops_at_the_shortest()
    {
        //Arrange / Act / Assert (scheme/loops)
        Session(
            ("(map abs '(-2 4 -0.25 1))", "(2 4 0.25 1)"),
            ("(map cons '(1 2 3 4) '(2 3 4 5))", "((1 . 2) (2 . 3) (3 . 4) (4 . 5))"),
            ("(map cons '(1 2 3) '(2 3 4 5))", "((1 . 2) (2 . 3) (3 . 4))"),
            ("(map list '(1 2 3) '(4 5 6) '(7 8 9))", "((1 4 7) (2 5 8) (3 6 9))"),
            ("(with-output-to-string (lambda () (for-each display '(1 2 3 4 5 6 7 8 9))))",
                "\"123456789\""));
    }

    [Fact]
    public void filtering_and_modifying_follow_the_lists_chapter()
    {
        //Arrange / Act / Assert (scheme/lists)
        Session(
            ("(filter number? '(1 2 \"d\" 'e 4 '(2 . 3) 5))", "(1 2 4 5)"),
            ("(delete 3 '(1 2 3 4 5 4 3 2))", "(1 2 4 5 4 2)"),
            ("(length '(a b c d))", "4"),
            ("(list-ref '(a b c d) 3)", "d"),
            ("(define lst '(1 2 3 4 5))", null),
            ("(list-head (list-tail lst 2) 2)", "(3 4)"),
            ("(define a '(1 2 3 4 5))", null),
            ("(list-set! a 1 'b)", "b"),
            ("a", "(1 b 3 4 5)"),
            ("(define x '(1 2 3))", null),
            ("(define y '(4 5 6))", null),
            ("(append (list-head x 2) y)", "(1 2 4 5 6)"));
    }

    [Fact]
    public void srfi_1_accessors_arrive_with_the_module()
    {
        //Arrange / Act / Assert (scheme/lists/accessing.md and filtering.md)
        // The manual's REPL ran inside LilyPond, where (srfi srfi-1) is preloaded;
        // here the names arrive the way Guile documents -- through use-modules.
        Session(
            ("(use-modules (srfi srfi-1))", null),
            ("(first '(1 2 3 4 5))", "1"),
            ("(second '(1 2 3 4 5))", "2"),
            ("(third '(1 2 3 4 5))", "3"),
            ("(fifth '(1 2 3 4 5))", "5"),
            ("(last '(1 2 3 4 5))", "5"),
            // last walks pairs, so an improper tail is simply never reached.
            ("(last '(1 2 3 4 . 5))", "4"),
            ("(delete-duplicates '(\"a\" 2 3 \"a\" b 3))", "(\"a\" 2 3 b)"));
    }

    [Fact]
    public void define_forms_carry_their_printed_signatures()
    {
        //Arrange / Act / Assert (scheme/procedures/binding.md)
        Session(
            ("(define (rest-only . args) (length args))", null),
            ("rest-only", "#<procedure rest-only args>"),
            ("(rest-only 'a 'b 'c)", "3"),
            // The hybrid form: two required parameters and a dotted rest.
            ("(define (hybrid arg-1 arg-2 . arg-rest) (length arg-rest))", null),
            ("hybrid", "#<procedure hybrid (arg-1 arg-2 . arg-rest)>"),
            ("(hybrid 1 2 3 4 5)", "3"));
    }

    [Fact]
    public void vectors_read_literally_and_index_from_zero()
    {
        //Arrange / Act / Assert (scheme/data-types/vectors.md)
        Session(
            ("#(1 \"a\")", "#(1 \"a\")"),
            ("(vector 1 \"a\")", "#(1 \"a\")"),
            ("(define v #(1 \"a\"))", null),
            ("(vector-ref v 1)", "\"a\""));
    }

    [Fact]
    public void booleans_and_predicates_answer_as_the_manual_shows()
    {
        //Arrange / Act / Assert (scheme/data-types and scheme/conditionals)
        Session(
            ("(boolean? #f)", "#t"),
            ("(boolean? \"true\")", "#f"),
            ("(string? \"I'm a string\")", "#t"),
            ("(> 2 1)", "#t"),
            ("(string=? \"Yes\" \"No\")", "#f"),
            ("(list? \"I'm a list?\")", "#f"),
            ("(not #f)", "#t"),
            ("(not #t)", "#f"),
            // Everything that is not #f is true, so the negation of a LIST is #f.
            ("(not '(1 2 3))", "#f"),
            ("(not (< 2 1))", "#t"));
    }

    [Fact]
    public void the_manuals_once_failing_examples_now_answer_as_guile_does()
    {
        //Arrange
        // Four transcripts this class could not carry at first, because the port
        // diverged on each; all four now answer (or fail) exactly as the manual's
        // Guile does. The append error is structure.md's own demonstration, list-ref's
        // key is accessing.md's ABORT line, the list-cdr-set! splice is
        // modifying.md's example, and Hello' is strings.md's quote demonstration.

        //Act & Assert
        Session(
            ("(define lst (list 1 2 3 4))", null),
            ("(define lst2 (append lst 5))", null),
            ("lst2", "(1 2 3 4 . 5)"),
            ("(catch 'wrong-type-arg (lambda () (append lst2 6)) (lambda (key . args) key))",
                "wrong-type-arg"),
            ("(catch 'out-of-range (lambda () (list-ref '(a b c d) 4)) (lambda (key . args) key))",
                "out-of-range"),
            ("(define a '(1 2 3))", null),
            ("(define b '(4 5 6))", null),
            ("(list-cdr-set! a 1 b)", "(4 5 6)"),
            ("a", "(1 2 4 5 6)"),
            ("'Hello'", "Hello'"));
    }

    [Fact]
    public void the_manuals_error_examples_raise_the_same_keys()
    {
        //Arrange / Act / Assert
        // The manual demonstrates failures on purpose; the KEYS are the contract a
        // Scheme catch stands on. (Its Guile-1.8 misc-error for applying a
        // non-procedure is not fenced -- the modern key differs.)
        Session(
            ("(catch 'unbound-variable (lambda () violet) (lambda (key . args) key))",
                "unbound-variable"),
            ("(catch 'wrong-number-of-args (lambda () (cons 1 2 3)) (lambda (key . args) key))",
                "wrong-number-of-args"),
            ("(catch 'wrong-type-arg (lambda () (cadr (cons '(3 . 4) \"5\"))) (lambda (key . args) key))",
                "wrong-type-arg"));
    }
}
