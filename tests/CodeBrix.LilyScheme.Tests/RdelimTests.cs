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
/// <c>(ice-9 rdelim)</c> — vendored VERBATIM, running over the three C-side names
/// <c>%read-line</c>, <c>%read-delimited!</c> and <c>%init-rdelim-builtins</c>, plus
/// <c>unread-char</c>.
/// <para>
/// Every expected value is <c>libguile/rdelim.c</c>'s and the module's own DOCUMENTED
/// behaviour in the pinned Guile source, hand-computed — the four
/// <c>handle-delim</c> modes above all, whose distinctions (trailing newline kept,
/// pair split off, delimiter left in the stream) are exactly what a wrong
/// implementation collapses.
/// </para>
/// </summary>
public class RdelimTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(
                "(use-modules (ice-9 rdelim)) " + source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void read_line_trims_the_newline_by_default()
    {
        //Arrange / Act
        string result = Value("(read-line (open-input-string \"alpha\\nbeta\\n\"))");

        //Assert
        result.Should().Be("\"alpha\"");
    }

    [Fact]
    public void read_line_concat_keeps_the_newline()
    {
        //Arrange / Act
        // The control for the default: same input, 'concat, and the newline survives.
        string result = Value("(read-line (open-input-string \"alpha\\nbeta\\n\") 'concat)");

        //Assert
        result.Should().Be("\"alpha\\n\"");
    }

    [Fact]
    public void read_line_split_answers_the_pair()
    {
        //Arrange / Act
        string result = Value("(read-line (open-input-string \"alpha\\n\") 'split)");

        //Assert
        result.Should().Be("(\"alpha\" . #\\newline)");
    }

    [Fact]
    public void read_line_peek_leaves_the_newline_in_the_stream()
    {
        //Arrange / Act
        // 'peek reads the line but pushes the delimiter back, so the NEXT read-char
        // must answer the newline — the discriminating half of the fence.
        string result = Value(
            "(let* ((p (open-input-string \"ab\\ncd\"))"
            + "       (line (read-line p 'peek))"
            + "       (next (read-char p)))"
            + "  (list line next))");

        //Assert
        result.Should().Be("(\"ab\" #\\newline)");
    }

    [Fact]
    public void read_line_at_end_of_file_answers_the_eof_object()
    {
        //Arrange / Act
        string result = Value("(eof-object? (read-line (open-input-string \"\")))");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void read_delimited_stops_at_any_delimiter_and_gobbles_it()
    {
        //Arrange / Act
        // Two reads in sequence prove the delimiter was consumed, not left behind.
        string result = Value(
            "(let ((p (open-input-string \"a:b;c\")))"
            + "  (list (read-delimited \":;\" p) (read-delimited \":;\" p)))");

        //Assert
        result.Should().Be("(\"a\" \"b\")");
    }

    [Fact]
    public void read_delimited_split_pairs_the_text_with_its_delimiter()
    {
        //Arrange / Act
        string result = Value("(read-delimited \":\" (open-input-string \"ab:cd\") 'split)");

        //Assert
        result.Should().Be("(\"ab\" . #\\:)");
    }

    [Fact]
    public void read_string_reads_the_whole_rest_of_the_port()
    {
        //Arrange / Act
        // The port is advanced past 'x' first, so the answer proves read-string reads
        // the REMAINDER, not the whole underlying text.
        string result = Value(
            "(let ((p (open-input-string \"xabc\\ndef\")))"
            + "  (read-char p)"
            + "  (read-string p))");

        //Assert
        result.Should().Be("\"abc\\ndef\"");
    }

    [Fact]
    public void unread_char_stacks_most_recent_first()
    {
        //Arrange / Act
        // scm_ungetc's contract: two pushbacks come back in reverse order, then the
        // stream resumes — three reads spell the whole rule.
        string result = Value(
            "(let ((p (open-input-string \"z\")))"
            + "  (unread-char #\\a p)"
            + "  (unread-char #\\b p)"
            + "  (list (read-char p) (read-char p) (read-char p)))");

        //Assert
        result.Should().Be("(#\\b #\\a #\\z)");
    }

    [Fact]
    public void write_line_appends_exactly_one_newline()
    {
        //Arrange / Act
        string result = Value(
            "(call-with-output-string (lambda (p) (write-line \"row\" p)))");

        //Assert
        result.Should().Be("\"row\\n\"");
    }
}
