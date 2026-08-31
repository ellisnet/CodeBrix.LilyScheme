using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The Guile surface beyond psyntax: module autoloading, quasisyntax, SRFI-13 strings,
/// SRFI-14 character sets, SRFI-9 records, generalized setters and the sort family.
/// </summary>
public class GuileCompatibilityTests
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

    [Fact]
    public void to_bool_narrows_scheme_truth_to_an_actual_boolean()
    {
        //Arrange
        // Guile's boot-9.scm defines (define (->bool x) (not (not x))). LilyPond's scm/
        // layer reaches it through the tuplet path, and a missing ->bool is an
        // unbound-variable that kills the whole file, not a wrong answer.

        //Act & Assert
        Eval("(->bool #f)").Should().Be("#f");
        Eval("(->bool #t)").Should().Be("#t");

        // Everything that is not #f is true in Scheme, and ->bool says so as #t.
        Eval("(->bool 0)").Should().Be("#t");
        Eval("(->bool '())").Should().Be("#t");
        Eval("(->bool \"\")").Should().Be("#t");
    }

    [Fact]
    public void use_modules_autoloads_the_vendored_srfi_1_source()
    {
        //Arrange
        // Guile autoloads a module's source the first time it is named. Without that, a
        // freshly resolved module is simply empty and every name it supplies is unbound --
        // which reads as dozens of unrelated failures rather than one missing mechanism.
        //Act
        string result = Eval("(use-modules (srfi srfi-1))", "(fold + 0 '(1 2 3 4))");

        //Assert
        result.Should().Be("10");
    }

    [Fact]
    public void use_modules_autoloads_ice9_match()
    {
        //Arrange & Act
        string result = Eval(
            "(use-modules (ice-9 match))",
            "(match '(1 2 3) ((a b c) (+ a b c)))");

        //Assert
        result.Should().Be("6");
    }

    [Fact]
    public void a_module_with_no_vendored_source_stays_empty_rather_than_raising()
    {
        //Arrange
        // The host program creates modules of its own; resolving one must not be an error.
        //Act
        string result = Eval("(begin (resolve-module '(no such module)) 'resolved)");

        //Assert
        result.Should().Be("resolved");
    }

    [Fact]
    public void quasisyntax_is_available_without_importing_anything()
    {
        //Arrange
        // Guile pulls quasisyntax into the core environment from boot-9, not through a
        // module import, so LilyPond's scm/ can use #` templates with no use-modules.
        //Act
        string result = Eval(
            "(define-syntax pick (lambda (x) (syntax-case x () ((_ a) #`(list #,#'a 2)))))",
            "(pick 1)");

        //Assert
        result.Should().Be("(1 2)");
    }

    [Fact]
    public void quasisyntax_splices_an_unsyntax_in_tail_position()
    {
        //Arrange
        // (let (...) . #,body) is the shape scm/music-functions.scm is built on.
        //Act
        // The spliced body cannot see the template's own binding -- that is hygiene, and
        // it is what makes the splice safe. What is under test is that the tail unsyntax
        // splices at all rather than reading as two list elements.
        string result = Eval(
            "(define-syntax wrap (lambda (x) (syntax-case x () ((_ b) #`(let ((t 1)) . #,#'b)))))",
            "(wrap ((+ 41 1)))");

        //Assert
        result.Should().Be("42");
    }

    [Fact]
    public void the_reader_reads_the_non_finite_reals()
    {
        //Arrange & Act
        string positive = Eval("(list (inf? +inf.0) (inf? -inf.0) (nan? +nan.0))");

        //Assert
        positive.Should().Be("(#t #t #t)");
    }

    [Fact]
    public void string_tokenize_splits_on_a_character_set()
    {
        //Arrange & Act
        string result = Eval("(string-tokenize \"Linux 6.12\" char-set:letter)");

        //Assert
        result.Should().Be("(\"Linux\")");
    }

    [Fact]
    public void string_trim_defaults_to_whitespace_and_accepts_a_predicate()
    {
        //Arrange & Act
        string trimmed = Eval("(string-trim-both \"  hello  \")");
        string custom = Eval("(string-trim \"xxhello\" (lambda (c) (char=? c #\\x)))");

        //Assert
        trimmed.Should().Be("\"hello\"");
        custom.Should().Be("\"hello\"");
    }

    [Fact]
    public void a_procedure_can_carry_a_setter_for_generalized_assignment()
    {
        //Arrange
        // (set! (proc args ...) value) expands to ((setter proc) args ... value), so the
        // setter has to travel with the procedure rather than be looked up by name.
        //Act
        string result = Eval(
            "(define prop (make-object-property))",
            "(define key (list 'a))",
            "(set! (prop key) 42)",
            "(prop key)");

        //Assert
        result.Should().Be("42");
    }

    [Fact]
    public void define_record_type_creates_a_disjoint_type()
    {
        //Arrange & Act
        string result = Eval(
            "(define-record-type point (make-point x y) point? (x point-x) (y point-y set-point-y!))",
            "(define p (make-point 3 4))",
            "(set-point-y! p 5)",
            "(list (point? p) (point? 7) (point-x p) (point-y p))");

        //Assert
        result.Should().Be("(#t #f 3 5)");
    }

    [Fact]
    public void sorting_is_stable_and_tolerates_a_partial_ordering()
    {
        //Arrange
        // LilyPond passes predicates that are not a strict weak ordering -- alist<?
        // compares only the keys it recognizes. A merge sort answers only "does b come
        // before a", so it copes; an introsort validates its comparer and throws.
        //Act
        string result = Eval(
            "(sort '((1 . a) (1 . b) (0 . c) (1 . d)) (lambda (x y) (< (car x) (car y))))");

        //Assert
        result.Should().Be("((0 . c) (1 . a) (1 . b) (1 . d))");
    }

    [Fact]
    public void goops_dispatches_on_the_classes_of_built_in_values()
    {
        //Arrange
        // scm/operators.scm specializes + and * on moments mixed with plain numbers, so
        // every value needs a class -- numbers included.
        //Act
        string result = Eval("(list (class-name (class-of 1)) (class-name (class-of \"s\")))");

        //Assert
        result.Should().Be("(<integer> <string>)");
    }

    [Fact]
    public void specializing_an_existing_procedure_keeps_it_working_for_everything_else()
    {
        //Arrange
        // Adding a method to '+' must not discard the arithmetic already bound there.
        //Act
        string result = Eval(
            "(define-class <box> () (v))",
            "(define-method (+ (a <box>) (b <box>)) 'boxes)",
            "(list (+ 1 2) (+ (make <box>) (make <box>)))");

        //Assert
        result.Should().Be("(3 boxes)");
    }

    [Fact]
    public void string_ports_capture_written_output()
    {
        //Arrange & Act
        string result = Eval("(call-with-output-string (lambda (p) (display \"hi\" p)))");

        //Assert
        result.Should().Be("\"hi\"");
    }

    [Fact]
    public void a_registered_hash_extension_takes_over_its_dispatch_character()
    {
        //Arrange
        SchemeReader.RegisterHashExtension('!', reader =>
        {
            reader.ReadCharacterRaw();
            return Symbol.Intern("bang");
        });

        //Act
        List<object> forms;
        try
        {
            forms = SchemeReader.ReadAll("(#! 1)", "<test>");
        }
        finally
        {
            SchemeReader.RegisterHashExtension('!', null);
        }

        //Assert
        Printer.Write(forms[0]).Should().Be("(bang 1)");
    }

    [Fact]
    public void the_bootstrap_reads_guile_source_with_extensions_suspended()
    {
        //Arrange
        // psyntax-pp.scm contains extended symbols such as #{ $sc-ellipsis }#. A host
        // handler for '{' -- LilyPond registers one for embedded music -- must not
        // capture them, or the expander is corrupted before anything else can fail.
        SchemeReader.RegisterHashExtension('{', reader =>
        {
            reader.ReadCharacterRaw();
            return Symbol.Intern("hijacked");
        });

        //Act
        string result;
        try
        {
            result = Eval("(+ 1 2)");
        }
        finally
        {
            SchemeReader.RegisterHashExtension('{', null);
        }

        //Assert
        result.Should().Be("3");
    }

    [Fact]
    public void case_evaluates_its_key_expression_exactly_once()
    {
        //Arrange
        // R7RS requires this, and ice-9/format.scm depends on it: its directive
        // dispatch is (case (char-upcase (next-char)) ...), whose key advances the
        // parse position. A case that re-evaluates the key per clause reads off the
        // end of the format string and recurses through format-error without bound.
        //Act
        string result = Eval(
            "(let ((n 0))"
            + " (case (begin (set! n (+ n 1)) #\\S)"
            + "  ((#\\A) (list 'a n)) ((#\\S) (list 's n)) (else (list 'no n))))");

        //Assert
        result.Should().Be("(s 1)");
    }

    [Fact]
    public void format_directives_beyond_the_first_case_clause_work()
    {
        //Arrange
        // ~a matches the dispatch case's first clause, so it always worked; every
        // later clause (~d, ~x, ~%, iteration, ...) is reachable only with the
        // once-evaluated case key.
        //Act
        string result = Eval(
            "(use-modules (ice-9 format))",
            "(format #f \"~d ~x~%~{~a ~}\" 9 255 '(1 2))");

        //Assert
        result.Should().Be("\"9 ff\\n1 2 \"");
    }

    [Fact]
    public void a_format_error_is_a_catchable_scheme_throw()
    {
        //Arrange
        // ice-9/format.scm routes every format error through format-error, whose
        // thunk rebuilds a diagnostic with format itself under
        // (with-throw-handler #t ...); the handler is the base case that reports
        // with bare display/write before the error keeps propagating. This needs
        // BOTH 2026-08-03 fixes: with-throw-handler honouring its handler, and
        // case evaluating its key once.
        //Act
        string result = Eval(
            "(use-modules (ice-9 format))",
            "(catch #t (lambda () (format #f \"~a ~a\" 1)) (lambda (key . args) key))");

        //Assert
        result.Should().Be("misc-error");
    }

    [Fact]
    public void list_copy_preserves_an_improper_tail()
    {
        //Arrange
        // Guile's list-copy copies the spine and keeps the improper tail. Dropping the
        // tail silently truncated every dotted pair run through list-copy — which is
        // how LilyPond's completize-grob-entry lost every atom-valued grob default.
        //Act
        string dotted = Eval("(list-copy '(outside-staff-priority . 450))");
        string proper = Eval("(list-copy '(1 2 3))");
        string atom = Eval("(list-copy 5)");

        //Assert
        dotted.Should().Be("(outside-staff-priority . 450)");
        proper.Should().Be("(1 2 3)");
        atom.Should().Be("5");
    }

    [Fact]
    public void array_literals_carry_rank_and_lower_bounds()
    {
        //Arrange
        // #1@1(...) is Guile's syntax for a one-dimensional array indexed from 1 —
        // qr-code.scm's capacity tables are written this way.
        //Act
        string first = Eval("(array-ref '#1@1(17 32 53) 1)");
        string last = Eval("(array-ref '#1@1(17 32 53) 3)");

        //Assert
        first.Should().Be("17");
        last.Should().Be("53");
    }

    [Fact]
    public void a_two_dimensional_array_literal_prints_back_as_itself()
    {
        //Arrange & Act
        string result = Eval("'#2((a b) (c d))");

        //Assert
        result.Should().Be("#2((a b) (c d))");
    }

    [Fact]
    public void make_array_array_set_and_array_ref_round_trip()
    {
        //Arrange
        // Guile's argument order is (array-set! array VALUE index ...).
        //Act
        string result = Eval(
            "(let ((m (make-array 'empty 2 2)))"
            + " (array-set! m 'filled 1 0)"
            + " (list (array-ref m 1 0) (array-ref m 0 0)))");

        //Assert
        result.Should().Be("(filled empty)");
    }

    [Fact]
    public void make_shared_array_maps_view_indices_through_the_scheme_procedure()
    {
        //Arrange
        // The lily-library array-copy/subarray! pattern: a shifted 2x2 view into a
        // 4x4 destination, written through with array-copy!.
        //Act
        string result = Eval(
            "(let* ((dst (make-array 'a 4 4))"
            + "       (src (make-array 'b 2 2))"
            + "       (view (make-shared-array dst"
            + "                                (lambda (i j) (list (+ i 2) (+ j 1)))"
            + "                                2 2)))"
            + "  (array-copy! src view)"
            + "  (list (array-ref dst 2 1) (array-ref dst 3 2) (array-ref dst 0 0)))");

        //Assert
        result.Should().Be("(b b a)");
    }

    [Fact]
    public void transpose_array_and_array_cell_ref_give_shared_views()
    {
        //Arrange & Act
        string transposed = Eval("(array-ref (transpose-array '#2((1 2) (3 4)) 1 0) 0 1)");
        string row = Eval("(array->list (array-cell-ref '#2((1 2) (3 4)) 1))");

        //Assert
        transposed.Should().Be("3");
        row.Should().Be("(3 4)");
    }

    [Fact]
    public void array_index_map_fills_and_array_for_each_walks()
    {
        //Arrange & Act
        string result = Eval(
            "(let ((a (make-array 0 2 3)) (sum 0))"
            + " (array-index-map! a (lambda (i j) (+ (* 10 i) j)))"
            + " (array-for-each (lambda (x) (set! sum (+ sum x))) a)"
            + " sum)");

        //Assert
        result.Should().Be("36");
    }

    [Fact]
    public void the_system_vm_program_shim_answers_false_for_everything()
    {
        //Arrange
        // The vendored ice-9/session.scm reaches for
        // ((@ (system vm program) program?) proc); LilyScheme has no VM, so no value
        // is ever a program.
        //Act
        string result = Eval("((@ (system vm program) program?) car)");

        //Assert
        result.Should().Be("#f");
    }

    [Fact]
    public void the_bitwise_family_folds_over_all_arguments()
    {
        //Arrange & Act
        string result = Eval(
            "(list (logand 12 10) (logior 12 10) (logxor 12 10) (lognot 0)"
            + " (ash 1 4) (ash 16 -4) (make-list 3 'x))");

        //Assert
        result.Should().Be("(8 14 6 -1 16 1 (x x x))");
    }

    [Fact]
    public void procedure_arguments_describes_closures_through_the_arglist_property()
    {
        //Arrange
        // The vendored ice-9/session.scm builds procedure-arguments from the 'arglist
        // procedure property, which LilyScheme synthesizes on demand — this is what
        // lets document-functions.scm walk every public binding of (lily).
        //Act
        string result = Eval(
            "(use-modules (ice-9 session))",
            "(procedure-arguments (lambda* (a #:optional b c) a))");

        //Assert
        result.Should().Be(
            "((required a) (optional b c) (keyword) (allow-other-keys? . #f) (rest . #f))");
    }

    [Fact]
    public void iconv_string_to_bytevector_encodes_with_the_named_charset()
    {
        //Arrange
        // qr-code.scm imports string->bytevector from (ice-9 iconv) with #:select and
        // encodes payload text as latin1.
        //Act
        string result = Eval(
            "(use-modules ((ice-9 iconv) #:select (string->bytevector)))",
            "(bytevector-u8-ref (string->bytevector \"AB\" \"latin1\") 0)");

        //Assert
        result.Should().Be("65");
    }

    [Fact]
    public void put_string_writes_whole_strings_and_ranges()
    {
        //Arrange
        // (put-string port string [start [count]]) -- R6RS argument order, port
        // first. The vendored pretty-print.scm writes through it exclusively.
        //Act
        string result = Eval(
            "(call-with-output-string (lambda (p)"
            + " (put-string p \"hello\")"
            + " (put-string p \"hello\" 3)"
            + " (put-string p \"hello\" 1 3)))");

        //Assert
        result.Should().Be("\"helloloell\"");
    }

    [Fact]
    public void pretty_print_writes_through_put_string()
    {
        //Arrange
        // LilyPond's #{ ... #} embedding reaches pretty-print via
        // parser-ly-from-scheme.scm; every write in that file is a put-string.
        //Act
        string result = Eval(
            "(use-modules (ice-9 pretty-print))",
            "(equal? (call-with-output-string (lambda (p) (pretty-print '(a b (c d)) p)))"
            + " \"(a b (c d))\\n\")");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void logcount_counts_one_bits_and_negative_zero_bits()
    {
        //Arrange
        // Guile counts the 1 bits of a non-negative integer and the 0 bits of a
        // negative one's two's-complement form; define-music-callbacks.scm sizes
        // tremolo dot counts with it.
        //Act
        string result = Eval(
            "(list (logcount #b10101010) (logcount 0) (logcount -2) (logcount (- (ash 1 64) 1)))");

        //Assert
        result.Should().Be("(4 0 1 64)");
    }

    [Fact]
    public void integer_length_measures_the_ones_complement_magnitude()
    {
        //Arrange
        // Expected values are libguile's own: the three in numbers.c's docstring for
        // integer-length, plus the negative cases scm_integer_length_i defines by
        // replacing n with (-1 - n) first -- so -1 and 0 both answer 0.
        //Act
        string result = Eval(
            "(list (integer-length #b10101010) (integer-length 0) (integer-length #b1111)"
            + " (integer-length -1) (integer-length -2) (integer-length -3)"
            + " (integer-length (ash 1 100)))");

        //Assert
        result.Should().Be("(8 0 4 0 1 2 101)");
    }

    [Fact]
    public void logbit_tests_one_bit_counting_from_the_least_significant()
    {
        //Arrange
        // The five expected answers are the five lines of numbers.c's logbit? docstring.
        // The negative case is the CONTROL for the two's-complement reading: -1 has
        // every bit set, so a high index must still answer #t.
        //Act
        string result = Eval(
            "(list (logbit? 0 #b1101) (logbit? 1 #b1101) (logbit? 2 #b1101)"
            + " (logbit? 3 #b1101) (logbit? 4 #b1101) (logbit? 99 -1) (logbit? 99 1))");

        //Assert
        result.Should().Be("(#t #f #t #t #f #t #f)");
    }

    [Fact]
    public void bytevector_to_u8_list_answers_exact_integers()
    {
        //Arrange
        // (rnrs bytevectors) EXPORTS this name and leaves load-extension to supply it,
        // so the vendored module is no evidence that anything defines it. LilyPond's
        // qr-code.scm walks a string's bytes through it. Exactness is asserted
        // separately because inexact elements would print identically in a list.
        //Act
        string result = Eval(
            "(use-modules (rnrs bytevectors) (ice-9 iconv))",
            "(let ((bv (string->bytevector \"AB\" \"latin1\")))"
            + " (list (bytevector->u8-list bv)"
            + "       (exact? (car (bytevector->u8-list bv)))"
            + "       (bytevector->u8-list (string->bytevector \"\" \"latin1\"))))");

        //Assert
        result.Should().Be("((65 66) #t ())");
    }

    [Fact]
    public void a_vector_is_a_rank_one_array()
    {
        //Arrange
        // libguile/arrays.c's scm_is_array counts scm_tc7_vector, so array? and the
        // array accessors take a plain vector -- which is how qr-code.scm reads its
        // #(...) format-information tables. The array-set! case is the one that says
        // the wrapper SHARES the vector rather than copying it, and the list CONTROL
        // must answer #f so "everything is an array" cannot pass.
        //Act
        string result = Eval(
            "(let ((v (vector 10 20 30)))"
            + " (array-set! v 99 1)"
            + " (list (array? v) (array? '(1 2 3)) (array-rank v) (array-dimensions v)"
            + "       (array-ref v 0) (array-ref v 1) (vector-ref v 1)))");

        //Assert
        result.Should().Be("(#t #f 1 (3) 10 99 99)");
    }

    [Fact]
    public void array_length_answers_the_first_dimension_and_refuses_rank_zero()
    {
        //Arrange / Act
        // MEASURED: a rank-2 array answers its FIRST dimension -- 2 for #2((a b c) (d e
        // f)), not 3 and not 6 -- and a rank-0 array has no dimension to report, so it is
        // a wrong-type-arg. The list CONTROL must refuse, so "everything has a length"
        // cannot pass.
        string result = Eval(
            "(let ((try (lambda (th) (catch #t th (lambda (k . a) (list 'ERR k))))))"
            + " (list (try (lambda () (array-length (vector 1 2 3))))"
            + "       (try (lambda () (array-length '#2((a b c) (d e f)))))"
            + "       (try (lambda () (array-length '#0(a))))"
            + "       (try (lambda () (array-length '(1 2))))))");

        //Assert
        result.Should().Be("(3 2 (ERR wrong-type-arg) (ERR wrong-type-arg))");
    }

    [Fact]
    public void array_type_is_the_general_type_and_bitvectors_do_not_exist()
    {
        //Arrange / Act
        // Every array here is a general one, which is upstream's #t for a vector and for
        // a multi-dimensional array alike. bitvector? answers #f for every value because
        // there is no such type to be one -- true, not merely plausible.
        string result = Eval(
            "(let ((try (lambda (th) (catch #t th (lambda (k . a) (list 'ERR k))))))"
            + " (list (try (lambda () (array-type (vector 1 2))))"
            + "       (try (lambda () (array-type '#2((a b) (c d)))))"
            + "       (try (lambda () (array-type '(1 2))))"
            + "       (bitvector? (vector 1)) (bitvector? \"ab\")"
            + "       (bitvector? '(1 2)) (bitvector? 5)))");

        //Assert
        result.Should().Be("(#t #t (ERR wrong-type-arg) #f #f #f #f)");
    }

    [Fact]
    public void truncated_print_matches_upstream_across_the_shapes_it_dispatches_on()
    {
        //Arrange / Act
        // The procedure the array accessors were the last blockers for. It reaches
        // with-output-to-port, %default-port-conversion-strategy, port-encoding,
        // array-length, array-type and bitvector? -- six names that were all absent --
        // so it doubles as the end-to-end fence for the whole group. Every expected
        // string measured on the oracle.
        string result = Eval(
            "(use-modules (ice-9 pretty-print))",
            "(list (call-with-output-string (lambda (p) (truncated-print '(a b c) p)))"
            + " (call-with-output-string"
            + "   (lambda (p) (truncated-print '(a b c d e f g h i j k l m n o p q r s t"
            + "                                 u v w x y z) p #:width 20)))"
            + " (call-with-output-string"
            + "   (lambda (p) (truncated-print \"a fairly long string that will not fit\""
            + "                                p #:width 20)))"
            + " (call-with-output-string"
            + "   (lambda (p) (truncated-print (vector 1 2 3 4 5 6 7 8 9 10 11 12)"
            + "                                p #:width 20)))"
            + " (call-with-output-string (lambda (p) (truncated-print 3.14159 p))))");

        //Assert
        result.Should().Be(
            "(\"(a b c)\" \"(a b c d e f g h \u2026)\""
            + " \"\\\"a fairly long str\u2026\\\"\""
            + " \"#(1 2 3 4 5 6 7 8 \u2026)\" \"3.14159\")");
    }

    [Fact]
    public void module_map_walks_a_modules_own_bindings()
    {
        //Arrange
        // ly/context-mods-init.ly walks an output-def scope with module-map to
        // collect \accepts entries.
        //Act
        string result = Eval(
            "(define-module (test walked))",
            "(define alpha 1)",
            "(define beta 2)",
            "(let ((symbols (module-map (lambda (sym var) sym) (current-module))))"
            + " (list (and (memq 'alpha symbols) #t) (and (memq 'beta symbols) #t)))");

        //Assert
        result.Should().Be("(#t #t)");
    }

    [Fact]
    public void string_eq_compares_whole_strings_and_ranges()
    {
        //Arrange
        // SRFI-13's string=, distinct from R7RS string=?; ly/articulate.ly
        // compares tempo markup text with it.
        //Act
        string result = Eval(
            "(list (string= \"rall\" \"rall\")"
            + " (string= \"rall\" \"rit.\")"
            + " (string= \"abcde\" \"bcd\" 1 4)"
            + " (string= \"abc\" \"abd\" 0 2 0 2))");

        //Assert
        result.Should().Be("(#t #f #t #t)");
    }

    [Fact]
    public void call_with_prompt_runs_the_thunk_and_handles_aborts()
    {
        //Arrange
        // pretty-print's truncating writer is built on the escape-only protocol:
        // run the thunk, and on abort-to-prompt run the handler with the
        // continuation first and the abort arguments after it.
        //Act
        string result = Eval(
            "(list (call-with-prompt (make-prompt-tag) (lambda () 'ran) (lambda (k) 'aborted))"
            + " (let ((tag (make-prompt-tag)))"
            + "   (call-with-prompt tag"
            + "     (lambda () (abort-to-prompt tag 'payload) 'unreached)"
            + "     (lambda (k v) v))))");

        //Assert
        result.Should().Be("(ran payload)");
    }

    [Fact]
    public void an_abort_passes_through_an_intervening_catch()
    {
        //Arrange
        // A prompt abort is not a throw: catch clauses between the abort and its
        // prompt must not intercept it.
        //Act
        string result = Eval(
            "(let ((tag (make-prompt-tag)))"
            + " (call-with-prompt tag"
            + "   (lambda () (catch #t (lambda () (abort-to-prompt tag 'through)) (lambda args 'caught)))"
            + "   (lambda (k v) v)))");

        //Assert
        result.Should().Be("through");
    }

    [Fact]
    public void a_soft_output_port_forwards_writes_and_tracks_position()
    {
        //Arrange
        // (ice-9 soft-ports)' keyword make-soft-port, the modern Guile 3 form;
        // the truncating writer inspects port-line while writing.
        //Act
        string result = Eval(
            "(use-modules (ice-9 soft-ports))",
            "(let* ((chunks '())"
            + "       (p (make-soft-port #:id \"t\""
            + "                          #:write-string (lambda (s) (set! chunks (cons s chunks))))))"
            + " (put-string p \"ab\")"
            + " (display \"c\" p)"
            + " (newline p)"
            + " (let ((line (port-line p)) (column (port-column p)))"
            + "   (close p)"
            + "   (list (string-concatenate-reverse chunks) line column)))");

        //Assert
        result.Should().Be("(\"abc\\n\" 1 0)");
    }

    [Fact]
    public void pretty_print_wraps_a_long_expression()
    {
        //Arrange
        // A pair wider than #:width forces the truncating writer to abort and
        // pretty-print to split lines -- the whole prompt/soft-port path at once.
        //Act
        string result = Eval(
            "(use-modules (ice-9 pretty-print))",
            "(let ((out (call-with-output-string (lambda (p)"
            + " (pretty-print '(alpha beta gamma delta epsilon zeta eta theta) p #:width 20)))))"
            + " (number? (string-contains out \"\\n \")))");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void use_modules_autoloads_ice9_list_for_rassoc()
    {
        //Arrange
        // chord-name.scm and define-music-display-methods.scm import rassoc from
        // (ice-9 list) with #:select; the module autoloads like any other.
        //Act
        string result = Eval(
            "(use-modules (ice-9 list))",
            "(list (rassoc 2 '((a . 1) (b . 2) (c . 3)))"
            + " (rassq 'y '((x . y)))"
            + " (rassv 2 '((n . 2)))"
            + " (rassoc 9 '((a . 1))))");

        //Assert
        result.Should().Be("((b . 2) (x . y) (n . 2) #f)");
    }

    [Fact]
    public void iota_counts_with_any_integer_valued_number_not_only_a_fixnum()
    {
        //Arrange
        // ADDED at PARITY 6. Guile's iota takes (count [start [step]]) and counts with
        // ANY integer-valued number; the port matched on the C# type `long' and silently
        // answered the EMPTY list for anything else — trap 10's shape, a type pattern
        // where the numeric tower was owed.
        //
        // It is not a corner case. LilyPond's scm/bar-line.scm computes both the dashed
        // and the dotted bar line's dash count from a real division through `round',
        // which in Scheme answers a REAL — (round (/ 8.0 2.0)) is 4.0 and inexact on
        // BOTH engines — so every call arrived as a double. No dashed or dotted bar line
        // had ever drawn a single dash.
        //
        // Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2
        // answers (4.0 2.0 0.0 -2.0 -4.0) for the real form and (4 2 0 -2 -4) for the
        // exact one, keeping each count's own exactness in the elements.

        //Act & Assert
        // The exact form is the CONTROL: it passed before the fix and must keep passing,
        // so a change that merely coerced everything to inexact would fail here.
        Eval("(iota 5 4 -2)").Should().Be("(4 2 0 -2 -4)");

        // The real form is the defect.
        Eval("(iota 5.0 4.0 -2)").Should().Be("(4.0 2.0 0.0 -2.0 -4.0)");

        // The exact shape bar-line.scm actually produces, spelled out.
        Eval("(iota (1+ (round (/ 8.0 2.0))) (round (/ 8.0 2.0)) -2)")
            .Should().Be("(4.0 2.0 0.0 -2.0 -4.0)");

        // A non-integer count is still refused, so this did not become "coerce anything".
        Eval("(iota 2.5)").Should().Be("()");
    }

    [Fact]
    public void a_hash_table_compares_its_keys_the_way_the_accessor_does()
    {
        //Arrange
        // THE AUTHORITY IS THE GUILE MANUAL, not a remembered convention (rule 35a):
        // "hash-ref ... uses `equal?' to compare keys", while hashq-ref uses `eq?' and
        // hashv-ref uses `eqv?'. A table carries no comparer of its own -- the ACCESSOR
        // decides -- so a table made by `make-hash-table' and read by `hash-ref' compares
        // structurally.
        //
        // LilyScheme gives the TABLE the comparer, and `make-hash-table' answered a
        // REFERENCE-keyed one, so every structured key silently missed. LilyPond's
        // ly/predefined-fretboards-init.ly stores a chord shape under
        // (cons key-symbol tuning) and reads it back with a FRESH cons of the same two
        // things; the read answered '() every time, for 322 errors in one sweep.

        //Act & Assert
        // A pair key, stored and read through two distinct but equal? conses.
        Eval("(let ((t (make-hash-table 7)))"
            + " (hash-set! t (cons 'c9 '(1 2 3)) 'shape)"
            + " (hash-ref t (cons 'c9 '(1 2 3)) 'missing))")
            .Should().Be("shape");

        // A STRING key, which is how file-cache.scm and musicQuotes are keyed.
        Eval("(let ((t (make-hash-table 7)))"
            + " (hash-set! t (string-append \"gui\" \"tar\") 42)"
            + " (hash-ref t \"guitar\" 'missing))")
            .Should().Be("42");

        // THE CONTROLS. A key that is NOT equal? must still miss, or the table would be
        // answering everything -- trap 11's shape.
        Eval("(let ((t (make-hash-table 7)))"
            + " (hash-set! t (cons 'c9 '(1 2 3)) 'shape)"
            + " (hash-ref t (cons 'c9 '(1 2 4)) 'missing))")
            .Should().Be("missing");

        // And a symbol key must keep working through the hashq family, which is what
        // boot-9's own object-property shim routes symbols to.
        Eval("(let ((t (make-hash-table 7)))"
            + " (hashq-set! t 'k 'v) (hashq-ref t 'k 'missing))")
            .Should().Be("v");
    }
}
