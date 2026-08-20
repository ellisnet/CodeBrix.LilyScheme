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
/// The optional <c>[start [end]]</c> ranges of the SRFI-13 string family. Half of the
/// family once DECLARED the range and ignored it -- the worst arity to ship, because a
/// ranged call succeeds and the unranged answer comes back. Every fence here is
/// therefore a case whose ranged answer DIFFERS from the unranged one; a range that is
/// accepted and ignored cannot pass. The expectations come from libguile/srfi-13.c,
/// read rather than recalled, and the out-of-range contract is libguile/strings.c's
/// scm_i_get_substring_spec: 0 &lt;= start &lt;= end &lt;= length, violations raised as
/// a catchable <c>out-of-range</c>.
/// </summary>
public class Srfi13RangeTests
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
    public void string_index_searches_only_the_given_window()
    {
        //Arrange
        // The glyph name font-table.ly splits; unranged, the first dot is at 11.

        //Act & Assert
        Eval("(string-index \"accidentals.doublesharp.arrowdown\" #\\.)").Should().Be("11");

        // From 16 on, the ranged search must skip the dot at 11 and answer 23 -- the
        // call that used to die on wrong-number-of-args.
        Eval("(string-index \"accidentals.doublesharp.arrowdown\" #\\. 16)").Should().Be("23");

        // A window holding no match answers #f even though the whole string has one:
        // "a.b.c" has dots at 1 and 3, and [2, 3) sees neither.
        Eval("(string-index \"a.b.c\" #\\. 2 3)").Should().Be("#f");
    }

    [Fact]
    public void string_rindex_honours_the_range_it_already_accepted()
    {
        //Arrange
        // This is the silent half of the font-table pair: string-rindex ACCEPTED the
        // range and scanned the whole string anyway, answering 23 where Guile answers
        // 11 -- no error, just the wrong split.

        //Act & Assert
        Eval("(string-rindex \"accidentals.doublesharp.arrowdown\" #\\. 0 16)").Should().Be("11");
        Eval("(string-rindex \"accidentals.doublesharp.arrowdown\" #\\.)").Should().Be("23");
    }

    [Fact]
    public void the_font_table_middle_split_lands_on_the_middle_dot()
    {
        //Arrange / Act
        // Documentation/en/included/font-table.ly's doc-char arithmetic, verbatim in
        // shape: with both halves honest the name splits as
        // accidentals / .doublesharp.arrowdown, not accidentals.doublesharp / .arrowdown.
        string result = Eval(
            "(let* ((name \"accidentals.doublesharp.arrowdown\")"
            + "       (middle-pos (round (/ (string-length name) 2)))"
            + "       (left-dot-pos (string-rindex name #\\. 0 middle-pos))"
            + "       (right-dot-pos (string-index name #\\. middle-pos)))"
            + "  (list middle-pos left-dot-pos right-dot-pos))");

        //Assert
        result.Should().Be("(16 11 23)");
    }

    [Fact]
    public void string_count_counts_inside_the_window_only()
    {
        //Arrange / Act
        // "banana" holds three #\a, at 1, 3 and 5; [2, ...) sees two and [2, 4) one.
        string result = Eval(
            "(list (string-count \"banana\" #\\a)"
            + " (string-count \"banana\" #\\a 2)"
            + " (string-count \"banana\" #\\a 2 4))");

        //Assert
        result.Should().Be("(3 2 1)");
    }

    [Fact]
    public void the_ranged_twins_take_a_char_set_or_predicate_criterion()
    {
        //Arrange
        // string-index used to hand-roll a char-versus-procedure branch and could not
        // take a character set at all; both twins now go through the family's shared
        // criterion dispatch.

        //Act & Assert
        Eval("(string-index \"foo bar baz\" char-set:whitespace 4)").Should().Be("7");

        // "ab1cd2e" has digits at 2 and 5; the ranged rindex must not see the one at 5.
        Eval("(string-rindex \"ab1cd2e\" char-numeric? 0 4)").Should().Be("2");
        Eval("(string-rindex \"ab1cd2e\" char-numeric?)").Should().Be("5");
    }

    [Fact]
    public void string_pad_pads_and_truncates_the_selected_region()
    {
        //Arrange
        // With a range, the region [start, end) is what gets padded or truncated;
        // characters outside it never appear. string-pad keeps the RIGHT of the region
        // when truncating and pads on the left; string-pad-right is the mirror.

        //Act & Assert
        Eval("(string-pad \"12345\" 4 #\\* 2 4)").Should().Be("\"**34\"");
        Eval("(string-pad \"12345\" 2 #\\* 0 3)").Should().Be("\"23\"");
        Eval("(string-pad-right \"12345\" 4 #\\* 2 4)").Should().Be("\"34**\"");
        Eval("(string-pad-right \"12345\" 2 #\\* 0 3)").Should().Be("\"12\"");

        // The unranged truncations, so the whole-string case stays fenced too.
        Eval("(string-pad \"12345\" 3)").Should().Be("\"345\"");
        Eval("(string-pad-right \"12345\" 3)").Should().Be("\"123\"");
    }

    [Fact]
    public void string_reverse_reverses_the_region_inside_the_whole_string()
    {
        //Arrange
        // Guile copies the WHOLE string and reverses the region within the copy
        // (libguile/srfi-13.c scm_string_reverse) -- SRFI-13's reference implementation
        // answers just the reversed window, and the oracle wins over the document.

        //Act & Assert
        Eval("(string-reverse \"abcdef\" 1 4)").Should().Be("\"adcbef\"");
        Eval("(string-reverse \"abcdef\")").Should().Be("\"fedcba\"");
    }

    [Fact]
    public void string_titlecase_transforms_the_region_only()
    {
        //Arrange
        // Like string-reverse: the region is transformed inside a whole-string copy,
        // the rest arrives untouched -- so the shouting outside [4, 9) survives.

        //Act & Assert
        Eval("(string-titlecase \"THE QUICK FOX\" 4 9)").Should().Be("\"THE Quick FOX\"");
        Eval("(string-titlecase \"hello world\")").Should().Be("\"Hello World\"");
    }

    [Fact]
    public void string_delete_and_filter_answer_from_the_window_alone()
    {
        //Arrange
        // The range selects the substring the filter runs over, and characters outside
        // it are DROPPED, not kept: deleting #\a from "banana" over [2, 4) filters "na"
        // down to "n", it does not answer "bnn" less a window.

        //Act & Assert
        Eval("(string-delete #\\a \"banana\" 2 4)").Should().Be("\"n\"");
        Eval("(string-filter #\\a \"banana\" 1 4)").Should().Be("\"aa\"");

        // Guile's backward-compatible string-first argument order, ranged.
        Eval("(string-delete \"banana\" #\\a 2 4)").Should().Be("\"n\"");
    }

    [Fact]
    public void string_trim_family_trims_within_the_region_and_answers_it()
    {
        //Arrange
        // Guile's string-trim answers the trimmed [start, end) REGION -- characters
        // outside it are dropped even when nothing gets trimmed, so the ranged answer
        // differs from the unranged one on an untrimmable string too.

        //Act & Assert
        Eval("(string-trim \"  ab  \" #\\space 1)").Should().Be("\"ab  \"");
        Eval("(string-trim-right \"  ab  \" #\\space 0 5)").Should().Be("\"  ab\"");
        Eval("(string-trim-both \"xxabxx\" #\\x 1 5)").Should().Be("\"ab\"");

        // Nothing trims here, and the region alone comes back anyway.
        Eval("(string-trim \"ab\" #\\z 1)").Should().Be("\"b\"");
        Eval("(string-trim \"ab\" #\\z)").Should().Be("\"ab\"");

        // The no-criterion whitespace default, unranged, stays as it was.
        Eval("(string-trim-both \"  ab  \")").Should().Be("\"ab\"");
    }

    [Fact]
    public void string_any_and_every_take_ranges_and_all_three_criterion_kinds()
    {
        //Arrange
        // (char_pred s [start [end]]) -- criterion FIRST. "banana" has #\a at 1, 3
        // and 5, so [4, 5) sees only the #\n.

        //Act & Assert
        Eval("(string-any #\\a \"banana\" 4)").Should().Be("#t");
        Eval("(string-any #\\a \"banana\" 4 5)").Should().Be("#f");

        // A char-set criterion: the space sits at 2, outside [3, ...).
        Eval("(string-any char-set:whitespace \"ab cd\" 3)").Should().Be("#f");
        Eval("(string-any char-set:whitespace \"ab cd\")").Should().Be("#t");

        // string-every over [0, 3) never sees the #\b.
        Eval("(string-every #\\a \"aaab\" 0 3)").Should().Be("#t");
        Eval("(string-every #\\a \"aaab\")").Should().Be("#f");

        // Empty windows: vacuous truth for every, no witness for any.
        Eval("(string-any #\\a \"aaa\" 1 1)").Should().Be("#f");
        Eval("(string-every #\\z \"abc\" 2 2)").Should().Be("#t");
    }

    [Fact]
    public void string_any_and_every_answer_the_predicates_own_value()
    {
        //Arrange
        // libguile's string-any-c-code / string-every-c-code return their `res'
        // variable: a PREDICATE criterion answers the value of the last call, not a
        // boolean washed through #t. The digits of "1ab2" sit at 0 and 3, so the
        // range decides WHICH digit's code comes back.

        //Act & Assert
        Eval("(define (digit-value c) (if (char-numeric? c) (char->integer c) #f))",
            "(string-any digit-value \"1ab2\")").Should().Be("49");
        Eval("(define (digit-value c) (if (char-numeric? c) (char->integer c) #f))",
            "(string-any digit-value \"1ab2\" 1)").Should().Be("50");

        // string-every answers the LAST call's value when every call is truthy.
        Eval("(string-every char->integer \"ab\")").Should().Be("98");
    }

    [Fact]
    public void string_tokenize_tokenizes_the_region_only()
    {
        //Arrange / Act & Assert
        // The range restricts tokenizing to [start, end); tokens outside never appear.
        Eval("(string-tokenize \"one two three\" char-set:graphic 0 7)")
            .Should().Be("(\"one\" \"two\")");
        Eval("(string-tokenize \"one two three\" char-set:graphic 4)")
            .Should().Be("(\"two\" \"three\")");

        // The default graphic token set, unranged, stays as it was.
        Eval("(string-tokenize \"one two\")").Should().Be("(\"one\" \"two\")");
    }

    [Fact]
    public void a_wrong_typed_criterion_is_loud_even_over_an_empty_window()
    {
        //Arrange
        // Guile validates CHAR_PRED before its search loop, so an empty window still
        // rejects a wrong-typed criterion; per-character validation would silently
        // pass it. The error is the positioned wrong-type-arg.

        //Act & Assert
        Eval("(catch 'wrong-type-arg"
            + " (lambda () (string-index \"abc\" 42))"
            + " (lambda (key . args) (list key (car args))))")
            .Should().Be("(wrong-type-arg \"string-index\")");

        Eval("(catch 'wrong-type-arg"
            + " (lambda () (string-count \"abc\" 42 1 1))"
            + " (lambda (key . args) key))")
            .Should().Be("wrong-type-arg");

        // The position in the message is Guile's: char_pred is argument 1 of
        // string-any and argument 2 of string-index.
        Eval("(catch 'wrong-type-arg"
            + " (lambda () (string-any 42 \"abc\"))"
            + " (lambda (key . args) (cadr args)))")
            .Should().Be("\"Wrong type argument in position 1: ~S\"");
    }

    [Fact]
    public void every_criterion_taker_rejects_a_wrong_typed_criterion()
    {
        //Arrange / Act
        // The Matches family, swept with the same non-criterion, so no member can
        // drift back to silently never-matching.
        string result = Eval(
            "(map (lambda (thunk) (catch 'wrong-type-arg thunk (lambda (key . args) 'wta)))"
            + " (list (lambda () (string-index \"abc\" 42))"
            + "       (lambda () (string-rindex \"abc\" 42))"
            + "       (lambda () (string-count \"abc\" 42))"
            + "       (lambda () (string-delete 42 \"abc\"))"
            + "       (lambda () (string-filter 42 \"abc\"))"
            + "       (lambda () (string-split \"a b\" 42))"
            + "       (lambda () (string-trim \"abc\" 42))"
            + "       (lambda () (string-any 42 \"abc\"))"
            + "       (lambda () (string-every 42 \"abc\"))))");

        //Assert
        result.Should().Be("(wta wta wta wta wta wta wta wta wta)");
    }

    [Fact]
    public void an_out_of_range_bound_raises_the_catchable_out_of_range()
    {
        //Arrange
        // scm_i_get_substring_spec validates 0 <= start <= end <= length and raises
        // out-of-range naming the procedure -- a Scheme catch must see it, and the
        // failure must be loud rather than a clamped answer.

        //Act & Assert
        Eval("(catch 'out-of-range"
            + " (lambda () (string-index \"abc\" #\\a 0 4))"
            + " (lambda (key . args) (list key (car args))))")
            .Should().Be("(out-of-range \"string-index\")");

        // START beyond END is out of range too, as end's lower bound is start.
        Eval("(catch 'out-of-range"
            + " (lambda () (string-rindex \"abc\" #\\a 2 1))"
            + " (lambda (key . args) key))")
            .Should().Be("out-of-range");

        // A negative pad length travels through scm_to_size_t in Guile and raises the
        // same key; it used to escape as a host ArgumentOutOfRangeException.
        Eval("(catch 'out-of-range"
            + " (lambda () (string-pad \"abc\" -1))"
            + " (lambda (key . args) key))")
            .Should().Be("out-of-range");
    }

    [Fact]
    public void every_ranged_member_rejects_an_end_beyond_the_string()
    {
        //Arrange / Act
        // The whole family, swept with the same bad END, so no member can drift back
        // to accepting and ignoring its range.
        string result = Eval(
            "(map (lambda (thunk) (catch 'out-of-range thunk (lambda (key . args) 'oor)))"
            + " (list (lambda () (string-index \"abc\" #\\a 0 4))"
            + "       (lambda () (string-rindex \"abc\" #\\a 0 4))"
            + "       (lambda () (string-count \"abc\" #\\a 0 4))"
            + "       (lambda () (string-reverse \"abc\" 0 4))"
            + "       (lambda () (string-titlecase \"abc\" 0 4))"
            + "       (lambda () (string-delete #\\a \"abc\" 0 4))"
            + "       (lambda () (string-filter #\\a \"abc\" 0 4))"
            + "       (lambda () (string-pad \"abc\" 2 #\\* 0 4))"
            + "       (lambda () (string-pad-right \"abc\" 2 #\\* 0 4))"
            + "       (lambda () (string-trim \"abc\" #\\a 0 4))"
            + "       (lambda () (string-trim-right \"abc\" #\\a 0 4))"
            + "       (lambda () (string-trim-both \"abc\" #\\a 0 4))"
            + "       (lambda () (string-any #\\a \"abc\" 0 4))"
            + "       (lambda () (string-every #\\a \"abc\" 0 4))"
            + "       (lambda () (string-tokenize \"abc\" char-set:graphic 0 4))))");

        //Assert
        result.Should().Be("(oor oor oor oor oor oor oor oor oor oor oor oor oor oor oor)");
    }

    [Fact]
    public void a_non_character_pad_filler_raises_wrong_type_arg()
    {
        //Arrange
        // Guile validates CHR with SCM_VALIDATE_CHAR; the old code silently substituted
        // a space, which is an answer where an error belongs.

        //Act & Assert
        Eval("(catch 'wrong-type-arg"
            + " (lambda () (string-pad \"abc\" 5 42))"
            + " (lambda (key . args) key))")
            .Should().Be("wrong-type-arg");
    }
}
