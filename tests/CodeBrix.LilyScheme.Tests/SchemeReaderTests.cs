using System.Collections.Generic;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

public class SchemeReaderTests
{
    private static object ReadOne(string text)
    {
        //Arrange / Act
        List<object> forms = SchemeReader.ReadAll(text, "<test>");

        //Assert
        forms.Count.Should().Be(1);
        return forms[0];
    }

    [Fact]
    public void reads_a_fixnum()
    {
        //Arrange / Act
        object value = ReadOne("42");

        //Assert
        value.Should().Be(42L);
    }

    [Fact]
    public void reads_a_negative_number()
        => ReadOne("-17").Should().Be(-17L);

    [Fact]
    public void reads_a_real()
        => ReadOne("3.5").Should().Be(3.5d);

    [Fact]
    public void reads_an_exact_ratio()
    {
        //Arrange / Act
        object value = ReadOne("3/6");

        //Assert -- the reader normalizes, so 3/6 collapses to 1/2
        Printer.Write(value).Should().Be("1/2");
    }

    [Fact]
    public void reads_a_hexadecimal_literal()
        => ReadOne("#xff").Should().Be(255L);

    [Fact]
    public void reads_a_bignum_beyond_long_range()
        => Printer.Write(ReadOne("123456789012345678901234567890"))
            .Should().Be("123456789012345678901234567890");

    [Fact]
    public void reads_a_symbol()
        => ((Symbol)ReadOne("hello")).Name.Should().Be("hello");

    [Fact]
    public void a_lone_dot_sequence_is_a_symbol_not_a_number()
        => ((Symbol)ReadOne("...")).Name.Should().Be("...");

    [Fact]
    public void quote_family_characters_are_constituents_inside_a_token()
    {
        //Arrange
        // Guile's reader treats ', ` and , as syntax only at DATUM START; once a token
        // is in progress they are ordinary symbol characters -- measured against the
        // pinned oracle: Hello', a'b, a`b, a,b and x'' are each ONE symbol, and 1' is
        // a symbol because its number parse fails. The apostrophe used to terminate
        // the token here, which read Hello' as a symbol plus a dangling quote and
        // died on "unexpected end of input".

        //Act & Assert
        ((Symbol)ReadOne("Hello'")).Name.Should().Be("Hello'");
        ((Symbol)ReadOne("a'b")).Name.Should().Be("a'b");
        ((Symbol)ReadOne("a`b")).Name.Should().Be("a`b");
        ((Symbol)ReadOne("a,b")).Name.Should().Be("a,b");
        ((Symbol)ReadOne("x''")).Name.Should().Be("x''");
        ((Symbol)ReadOne("1'")).Name.Should().Be("1'");
        Printer.Write(ReadOne("(a . b')")).Should().Be("(a . b')");
    }

    [Fact]
    public void quote_family_characters_still_read_as_syntax_at_datum_start()
    {
        //Arrange
        // The CONTROLS for the constituent rule above: at datum start -- after
        // whitespace, an opening paren, or another quote -- the same characters are
        // quote, quasiquote and unquote, and a hash literal ends before an adjacent
        // apostrophe (Guile reads #t by all-or-nothing character match, never by
        // token, so #t' is #t with the quote left for the NEXT datum).

        //Act & Assert
        Printer.Write(ReadOne("'Hello'")).Should().Be("(quote Hello')");
        Printer.Write(ReadOne("'a'b")).Should().Be("(quote a'b)");
        Printer.Write(ReadOne("(a 'b)")).Should().Be("(a (quote b))");
        Printer.Write(ReadOne("''x")).Should().Be("(quote (quote x))");
        Printer.Write(ReadOne("`(1 ,x)")).Should().Be("(quasiquote (1 (unquote x)))");

        List<object> forms = SchemeReader.ReadAll("#t'x", "<test>");
        forms.Count.Should().Be(2);
        forms[0].Should().Be(true);
        Printer.Write(forms[1]).Should().Be("(quote x)");
    }

    [Fact]
    public void reads_a_string_with_escapes()
        => ReadOne("\"a\\nb\"").ToString().Should().Be("a\nb");

    [Fact]
    public void reads_a_string_with_a_four_digit_u_escape()
        // U+203F UNDERTIE — the exact spelling define-markup-commands.scm uses
        // for \tied-lyric. Guile's \u takes exactly four hex digits, no terminator:
        // the trailing "a" is CONTENT, not part of the escape.
        => ReadOne("\"a\\u203fa\"").ToString().Should().Be("a\u203fa");

    [Fact]
    public void reads_a_string_with_a_six_digit_U_escape()
        // U+1D11E MUSICAL SYMBOL G CLEF — an astral code point, so the .NET string
        // holds a surrogate pair; ConvertFromUtf32 is what makes that correct.
        => ReadOne("\"\\U01D11E\"").ToString().Should().Be("\U0001D11E");

    [Fact]
    public void a_u_escape_with_a_non_hex_digit_is_a_reader_error()
    {
        System.Action act = () => ReadOne("\"\\u20g\"");
        act.Should().Throw<System.Exception>()
            .WithMessage("*invalid character in escape sequence*");
    }

    [Fact]
    public void reads_a_character_by_name()
        => ((SchemeChar)ReadOne("#\\space")).CodePoint.Should().Be(32);

    [Fact]
    public void reads_a_keyword()
        => ((Keyword)ReadOne("#:optional")).Name.Name.Should().Be("optional");

    [Fact]
    public void reads_a_proper_list()
        => Printer.Write(ReadOne("(1 2 3)")).Should().Be("(1 2 3)");

    [Fact]
    public void reads_a_dotted_pair()
        => Printer.Write(ReadOne("(a . b)")).Should().Be("(a . b)");

    [Fact]
    public void reads_a_vector()
        => ((object[])ReadOne("#(1 2 3)")).Length.Should().Be(3);

    [Fact]
    public void reads_square_brackets_as_parentheses()
        => Printer.Write(ReadOne("[1 2]")).Should().Be("(1 2)");

    [Fact]
    public void expands_the_quote_shorthand()
        => Printer.Write(ReadOne("'x")).Should().Be("(quote x)");

    [Fact]
    public void expands_the_syntax_shorthand()
        => Printer.Write(ReadOne("#'x")).Should().Be("(syntax x)");

    [Fact]
    public void reads_guile_extended_symbol_syntax()
    {
        //Arrange / Act -- #{...}# lets a symbol contain delimiters; psyntax uses it
        object value = ReadOne("#{1+}#");

        //Assert
        ((Symbol)value).Name.Should().Be("1+");
    }

    [Fact]
    public void reads_elisp_nil_as_its_own_value()
        => ReadOne("#nil").Should().BeSameAs(ElispNil.Instance);

    [Fact]
    public void skips_line_comments()
        => ReadOne("; ignored\n7").Should().Be(7L);

    [Fact]
    public void skips_nested_block_comments()
        => ReadOne("#| outer #| inner |# still |# 7").Should().Be(7L);

    [Fact]
    public void skips_datum_comments()
        => ReadOne("#;(ignored this) 7").Should().Be(7L);

    [Fact]
    public void reading_an_unterminated_list_reports_the_position()
    {
        //Arrange / Act / Assert
        SchemeReaderException failure = Assert.Throws<SchemeReaderException>(
            () => SchemeReader.ReadAll("(1 2", "<test>"));
        failure.Message.Should().Contain("<test>");
    }

    [Fact]
    public void parse_number_rejects_a_symbol()
        => SchemeReader.ParseNumber("hello").Should().BeNull();

    [Fact]
    public void parse_number_accepts_an_inexactness_prefix()
        => SchemeNumber.IsExact(SchemeReader.ParseNumber("#i5")).Should().BeFalse();

    [Fact]
    public void a_boolean_literal_stops_before_a_brace()
    {
        //Arrange
        // Guile reads #t by prefix -- scm_read_boolean never requires a delimiter
        // after it -- so `#t}` is #t with the brace left for the caller. LilyPond
        // writes exactly that inside one-line \layout blocks: ragged-right = ##t}
        SchemeReader reader = new SchemeReader("#t}", "<test>");

        //Act
        object value = reader.Read();

        //Assert
        value.Should().Be(true);
        reader.Position.Should().Be(2);
    }

    [Fact]
    public void boolean_long_spellings_read_case_insensitively()
    {
        //Arrange / Act / Assert
        ReadOne("#true").Should().Be(true);
        ReadOne("#TRUE").Should().Be(true);
        ReadOne("#False").Should().Be(false);
        ReadOne("#T").Should().Be(true);
        ReadOne("#F").Should().Be(false);
    }

    [Fact]
    public void a_boolean_prefix_leaves_a_nonmatching_suffix_unread()
    {
        //Arrange / Act
        // Guile's suffix match is all-or-nothing: #trap is #t followed by the
        // symbol rap, not an error.
        List<object> forms = SchemeReader.ReadAll("#trap", "<test>");

        //Assert
        forms.Count.Should().Be(2);
        forms[0].Should().Be(true);
        ((Symbol)forms[1]).Name.Should().Be("rap");
    }

    [Fact]
    public void a_digit_after_hash_f_is_refused()
    {
        //Arrange / Act / Assert
        // #f32(...) is an SRFI-4 float vector in Guile; the type does not exist
        // here, so the literal must fail loudly rather than read as #f.
        SchemeReaderException failure = Assert.Throws<SchemeReaderException>(
            () => SchemeReader.ReadAll("#f32(1 2)", "<test>"));
        failure.Message.Should().Contain("SRFI-4");
    }

    [Theory]
    // The literal that started it: \U is Guile's six-digit hex escape, so a raw splice
    // dies on the 's' of Users.
    [InlineData(@"C:\Users\jerem\AppData\Local\Temp\out.txt")]
    // The SILENT half, and the reason hand-doubling backslashes at a call site is not a
    // fix. Each of these spells a VALID escape, so a raw splice reads clean and names a
    // different file: \t is a tab, \a an alarm, \b a backspace, \n a newline.
    [InlineData(@"C:\temp\alarm\backup\notes.txt")]
    [InlineData(@"C:\rig\vault\form\x")]
    // \x is the terminated hex escape, and \\ and " are the two the doubling trick and
    // the naive quote-wrap respectively get wrong.
    [InlineData(@"C:\xfeed\a\\b\path")]
    [InlineData(@"C:\dir\with ""quotes"" in it\f.txt")]
    // POSIX paths must be untouched by all of this -- the same fence has to hold on
    // Linux and macOS, where there is nothing to escape.
    [InlineData("/tmp/lilyscheme/plain-path.txt")]
    public void a_host_path_written_as_a_string_literal_reads_back_unchanged(string path)
    {
        //Arrange
        // Printer.WriteString is what a host must put a filesystem path through before
        // splicing it into source. The contract is a ROUND TRIP: whatever it emits must
        // read back as the very path that went in, on every platform.
        string literal = Printer.WriteString(path);

        //Act
        object read = ReadOne(literal);

        //Assert
        read.ToString().Should().Be(path);
    }

    [Fact]
    public void a_windows_path_spliced_in_raw_is_still_refused()
    {
        //Arrange / Act / Assert
        // The CONTROL for the round trip above, and the fence on the reader itself: \U
        // must go on consuming exactly six hex digits (libguile/read.c's
        // SCM_READ_HEX_ESCAPE). Making the reader lenient here would "fix" the splice by
        // diverging from Guile, so the refusal is the behaviour being protected.
        SchemeReaderException failure = Assert.Throws<SchemeReaderException>(
            () => SchemeReader.ReadAll("\"C:\\Users\\jerem\"", "<test>"));
        failure.Message.Should().Contain("invalid character in escape sequence");
    }
}
