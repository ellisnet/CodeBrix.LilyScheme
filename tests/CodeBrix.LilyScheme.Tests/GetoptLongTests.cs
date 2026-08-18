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
/// <c>(ice-9 getopt-long)</c>, vendored VERBATIM — which makes it an end-to-end
/// fence for four other pieces at once, since the file imports
/// <c>(ice-9 common-list)</c> with a <c>#:select</c> rename, <c>(ice-9 match)</c>,
/// <c>(ice-9 regex)</c> and SRFI-9 records. Expected values are the module's own
/// documented behaviour in the pinned source.
/// </summary>
public class GetoptLongTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(
                "(use-modules (ice-9 getopt-long)) " + source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void a_long_option_with_a_value_parses()
    {
        //Arrange / Act
        // Both spellings of value passing — --length=5 and a separate token — must
        // answer the same string, which is getopt-long's documented equivalence.
        string result = Value(
            "(let ((spec '((length (value #t)))))"
            + "  (list (option-ref (getopt-long '(\"prog\" \"--length=5\") spec) 'length #f)"
            + "        (option-ref (getopt-long '(\"prog\" \"--length\" \"7\") spec) 'length #f)))");

        //Assert
        result.Should().Be("(\"5\" \"7\")");
    }

    [Fact]
    public void a_single_char_alias_answers_under_the_long_name()
    {
        //Arrange / Act
        string result = Value(
            "(option-ref (getopt-long '(\"prog\" \"-v\")"
            + "                        '((verbose (single-char #\\v) (value #f))))"
            + "            'verbose #f)");

        //Assert
        result.Should().Be("#t");
    }

    [Fact]
    public void non_option_arguments_collect_under_the_empty_list_key()
    {
        //Arrange / Act
        string result = Value(
            "(option-ref (getopt-long '(\"prog\" \"--flag\" \"a.txt\" \"b.txt\")"
            + "                        '((flag (value #f))))"
            + "            '() '())");

        //Assert
        result.Should().Be("(\"a.txt\" \"b.txt\")");
    }

    [Fact]
    public void option_ref_falls_back_to_its_default()
    {
        //Arrange / Act
        // The control for every fact above: an option that was never given answers
        // the caller's default, not #f-always and not an error.
        string result = Value(
            "(option-ref (getopt-long '(\"prog\") '((depth (value #t)))) 'depth \"8\")");

        //Assert
        result.Should().Be("\"8\"");
    }
}
