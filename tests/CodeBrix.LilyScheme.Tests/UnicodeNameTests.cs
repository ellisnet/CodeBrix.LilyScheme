using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Unicode;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>(ice-9 unicode)</c> — <c>char-&gt;formal-name</c> and
/// <c>formal-name-&gt;char</c>, and the table behind them.
/// <para>
/// The expectations here are Guile's DOCUMENTED behaviour and the Unicode
/// Character Database's own contents, not values this implementation happened to
/// answer. Two are worth stating because the obvious authority is the wrong one:
/// Guile returns <c>#f</c> for a character with no name, NOT the empty string
/// (<c>libguile/unicode.c</c>'s docstring says so), and it does NOT derive the
/// algorithmic names for the CJK and Hangul ranges — Python's
/// <c>unicodedata</c> does derive them, and copying it would have put ~1.4
/// million names in the table that Guile never answers.
/// </para>
/// <para>
/// That negative was MEASURED rather than assumed: GNU LilyPond 2.27.2 prints
/// whatever <c>char-&gt;formal-name</c> returns in its "no glyph for character"
/// warning, and across a full reference corpus it prints U+898B with no name at
/// all while naming 78 other characters. All 316 occurrences agree with this
/// table.
/// </para>
/// </summary>
public class UnicodeNameTests
{
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
    public void char_to_formal_name_answers_the_unicode_name()
    {
        //Arrange
        //Act
        string result = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name #\\a)");

        //Assert
        result.Should().Be("\"LATIN SMALL LETTER A\"");
    }

    [Fact]
    public void char_to_formal_name_answers_a_different_name_for_a_different_character()
    {
        //Arrange
        // The control. A table answering one constant, or a lookup ignoring its
        // argument, would pass the case above on its own.
        //Act
        string result = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name #\\Z)");

        //Assert
        result.Should().Be("\"LATIN CAPITAL LETTER Z\"");
    }

    [Fact]
    public void char_to_formal_name_answers_false_for_a_cjk_ideograph()
    {
        //Arrange
        // libguile/unicode.c: "If the character has no name, return #f." Guile does
        // not DERIVE "CJK UNIFIED IDEOGRAPH-898B" the way Python's unicodedata
        // does, and the paired case below is what keeps this from passing on an
        // implementation that answers #f to everything.
        //Act
        string ideograph = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name (integer->char #x898B))");
        string radical = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name (integer->char #x2F92))");

        //Assert
        ideograph.Should().Be("#f");
        radical.Should().Be("\"KANGXI RADICAL SEE\"");
    }

    [Fact]
    public void char_to_formal_name_answers_false_for_a_hangul_syllable()
    {
        //Arrange
        // The other algorithmic range, stated as the RULE the CJK case is an
        // instance of rather than as a second copy of it.
        //Act
        string result = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name (integer->char #xAC00))");

        //Assert
        result.Should().Be("#f");
    }

    [Fact]
    public void formal_name_to_char_is_the_other_direction()
    {
        //Arrange
        // The module exports two procedures and both must work; a half-module
        // answers "unbound variable" to code Guile runs.
        //Act
        string found = Eval(
            "(use-modules (ice-9 unicode))",
            "(formal-name->char \"HEBREW LETTER ALEF\")");
        string roundTrip = Eval(
            "(use-modules (ice-9 unicode))",
            "(char->formal-name (formal-name->char \"MUSIC FLAT SIGN\"))");

        //Assert
        found.Should().Be("#\\א");
        roundTrip.Should().Be("\"MUSIC FLAT SIGN\"");
    }

    [Fact]
    public void formal_name_to_char_answers_false_for_a_name_no_character_has()
    {
        //Arrange
        // The control for the direction above, and it also fences the matching
        // rule: names are matched EXACTLY, so a lower-case spelling of a real name
        // is not that name.
        //Act
        string invented = Eval(
            "(use-modules (ice-9 unicode))",
            "(formal-name->char \"NOT A REAL CHARACTER NAME\")");
        string wrongCase = Eval(
            "(use-modules (ice-9 unicode))",
            "(formal-name->char \"latin small letter a\")");

        //Assert
        invented.Should().Be("#f");
        wrongCase.Should().Be("#f");
    }

    [Fact]
    public void the_table_records_which_unicode_release_it_came_from()
    {
        //Arrange
        // Character names are stable once assigned but every release adds
        // thousands, so the release is part of what the table says. It travels in
        // the asset's own first line, not only in the notices file.
        //Act
        string version = UnicodeCharacterNames.UnicodeVersion;

        //Assert
        version.Should().Be("15.1.0");
    }

    [Fact]
    public void the_table_covers_the_standard_rather_than_a_sample()
    {
        //Arrange
        // A count fence against a CONTROL rather than a bare number: an
        // implementation carrying only the characters some consumer happens to ask
        // about would pass every case above. Tens of thousands says the whole
        // standard went in; the upper bound says the ~1.4 million algorithmic names
        // did NOT.
        //Act
        int count = UnicodeCharacterNames.Count;

        //Assert
        count.Should().BeGreaterThan(30000);
        count.Should().BeLessThan(100000);
    }
}
