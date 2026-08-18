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
/// The POSIX regular-expression surface: <c>make-regexp</c>'s flags and pattern
/// translation, <c>regexp-exec</c>'s match VECTOR (libguile/regex-posix.c's shape),
/// and the vendored <c>(ice-9 regex)</c> running verbatim on top.
/// <para>
/// The discriminating fences here are the places POSIX and .NET disagree and the
/// translation must decide: <c>[[:digit:]]</c> is ASCII where .NET's <c>\d</c> is
/// Unicode; a backslash INSIDE a bracket expression is a POSIX literal where .NET
/// reads an escape; <c>^</c> matches at a start offset (regex-posix.c searches the
/// substring) unless <c>regexp/notbol</c>; and an unmatched group answers
/// <see langword="false"/>, never an empty string — the case LilyPond's
/// output-svg.scm reads with <c>string?</c>. <c>regexp/basic</c> and
/// <c>regexp/noteol</c> are REFUSED loudly, fenced as such.
/// </para>
/// </summary>
public class RegexPosixTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(
                "(use-modules (ice-9 regex)) " + source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void an_unmatched_optional_group_answers_false_not_an_empty_string()
    {
        //Arrange / Act
        // output-svg.scm's own read: (string? (match:substring m 1)) on a group that
        // did not participate. Group 2 is the matched control.
        string result = Value(
            "(let ((m (regexp-exec (make-regexp \"(x)?(b+)\") \"abbc\")))"
            + "  (list (match:substring m 1) (match:substring m 2)))");

        //Assert
        result.Should().Be("(#f \"bb\")");
    }

    [Fact]
    public void the_match_is_a_vector_with_the_string_in_slot_zero()
    {
        //Arrange / Act
        // regexp-match? is ice-9/regex.scm's own structural check, and match:count
        // counts the groups — both read the regex-posix.c vector directly.
        string result = Value(
            "(let ((m (string-match \"(a)(b)?\" \"xa\")))"
            + "  (list (regexp-match? m) (match:count m) (match:string m)))");

        //Assert
        result.Should().Be("(#t 3 \"xa\")");
    }

    [Fact]
    public void posix_digit_is_ascii_only()
    {
        //Arrange / Act
        // U+0663 (ARABIC-INDIC DIGIT THREE) is Unicode Nd, so .NET's \d matches it —
        // POSIX [[:digit:]] must not. The ASCII 3 beside it is the control.
        string result = Value("(match:substring (string-match \"[[:digit:]]+\" \"a\\u0663" + "3b\"))");

        //Assert
        result.Should().Be("\"3\"");
    }

    [Fact]
    public void posix_alpha_reaches_beyond_ascii()
    {
        //Arrange / Act
        // Under a UTF-8 locale glibc's [[:alpha:]] takes Unicode letters, so é
        // (U+00E9) is in and the digits either side are the control. Compared with
        // string=? so the fence does not depend on the printer's escape spelling.
        string result = Value(
            "(string=? (match:substring (string-match \"[[:alpha:]]+\" \"1caf\\u00e92\"))"
            + "          \"caf\\u00e9\")");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void a_backslash_inside_a_bracket_expression_is_a_posix_literal()
    {
        //Arrange / Act
        // POSIX: [\d] is a two-member class, backslash and 'd'. Under .NET's reading
        // it would be the digit class — so the digit is the discriminating control.
        string result = Value(
            "(list (string-match \"[\\\\d]\" \"5\")"
            + "      (match:substring (string-match \"[\\\\d]\" \"d\")))");

        //Assert
        result.Should().Be("(#f \"d\")");
    }

    [Fact]
    public void a_close_bracket_in_first_position_is_literal()
    {
        //Arrange / Act
        string result = Value("(match:substring (string-match \"[]x]+\" \"a]x]b\"))");

        //Assert
        result.Should().Be("\"]x]\"");
    }

    [Fact]
    public void caret_matches_at_a_start_offset_unless_notbol()
    {
        //Arrange / Act
        // regex-posix.c hands regexec the substring from the offset, so ^ matches
        // THERE by default; regexp/notbol is the flag that turns exactly that off.
        string result = Value(
            "(let ((rx (make-regexp \"^b\")))"
            + "  (list (regexp-exec rx \"ab\" 1)"
            + "        (regexp-exec rx \"ab\" 1 regexp/notbol)))");

        //Assert
        result.Should().Be("(#(\"ab\" (1 . 2)) #f)");
    }

    [Fact]
    public void regexp_newline_lets_anchors_match_at_newlines()
    {
        //Arrange / Act
        // The flagless compile of the same pattern is the control.
        string result = Value(
            "(list (regexp-exec (make-regexp \"^b\" regexp/newline) \"a\\nb\")"
            + "      (regexp-exec (make-regexp \"^b\") \"a\\nb\"))");

        //Assert
        result.Should().Be("(#(\"a\\nb\" (2 . 3)) #f)");
    }

    [Fact]
    public void regexp_icase_folds_case()
    {
        //Arrange / Act
        string result = Value(
            "(list (match:substring (regexp-exec (make-regexp \"case\" regexp/icase) \"UPPERCASE\"))"
            + "      (regexp-exec (make-regexp \"case\") \"UPPERCASE\"))");

        //Assert
        result.Should().Be("(\"CASE\" #f)");
    }

    [Fact]
    public void regexp_basic_and_noteol_are_refused_loudly()
    {
        //Arrange / Act
        string result = Value(
            "(list (catch #t (lambda () (make-regexp \"a\" regexp/basic)) (lambda args 'refused))"
            + "      (catch #t (lambda () (regexp-exec (make-regexp \"a\") \"a\" 0 regexp/noteol))"
            + "             (lambda args 'refused)))");

        //Assert
        result.Should().Be("(refused refused)");
    }

    [Fact]
    public void fold_matches_and_substitute_run_from_the_vendored_module()
    {
        //Arrange / Act
        string result = Value(
            "(list (fold-matches \"[a-z]+\" \"ab cd ef\" 0 (lambda (m n) (+ n 1)))"
            + "      (regexp-substitute/global #f \"o\" \"foo bod\" 'pre \"0\" 'post))");

        //Assert
        result.Should().Be("(3 \"f00 b0d\")");
    }

    [Fact]
    public void regexp_quote_makes_a_pattern_match_itself()
    {
        //Arrange / Act
        // "1+1" unquoted matches "11"; quoted it must match only the literal text —
        // both halves asserted so the quote is proven load-bearing.
        string result = Value(
            "(list (match:substring (string-match \"1+1\" \"111=2\"))"
            + "      (match:substring (string-match (regexp-quote \"1+1\") \"1+1=2\")))");

        //Assert
        result.Should().Be("(\"111\" \"1+1\")");
    }
}
