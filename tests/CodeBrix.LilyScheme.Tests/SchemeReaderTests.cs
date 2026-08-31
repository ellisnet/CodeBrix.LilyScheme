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

    // Guile's five character-name tables, in the order it searches them: R5RS, R6RS,
    // R7RS, the abbreviated C0 control names, and the leftover compatibility names.
    // EVERY row was measured on the pinned 2.27.2 oracle one input at a time, through
    // (char->integer (read (open-input-string "#\\NAME"))); the refusals below were
    // measured the same way. The 33 abbreviations were MISSING, and their absence was
    // silent rather than loud: the reader fell back to "the name's first character",
    // so #\cr answered #\c and #\lf answered #\l — which is what LilyPond's own
    // lily.scm and framework-ps.scm spell, so both files parsed to the wrong thing.
    [Theory]
    [InlineData("space", 0x20)]
    [InlineData("newline", 0x0a)]
    [InlineData("nul", 0x00)]
    [InlineData("alarm", 0x07)]
    [InlineData("backspace", 0x08)]
    [InlineData("tab", 0x09)]
    [InlineData("linefeed", 0x0a)]
    [InlineData("vtab", 0x0b)]
    [InlineData("page", 0x0c)]
    [InlineData("return", 0x0d)]
    [InlineData("esc", 0x1b)]
    [InlineData("delete", 0x7f)]
    [InlineData("escape", 0x1b)]
    [InlineData("soh", 0x01)]
    [InlineData("stx", 0x02)]
    [InlineData("etx", 0x03)]
    [InlineData("eot", 0x04)]
    [InlineData("enq", 0x05)]
    [InlineData("ack", 0x06)]
    [InlineData("bel", 0x07)]
    [InlineData("bs", 0x08)]
    [InlineData("ht", 0x09)]
    [InlineData("lf", 0x0a)]
    [InlineData("vt", 0x0b)]
    [InlineData("ff", 0x0c)]
    [InlineData("cr", 0x0d)]
    [InlineData("so", 0x0e)]
    [InlineData("si", 0x0f)]
    [InlineData("dle", 0x10)]
    [InlineData("dc1", 0x11)]
    [InlineData("dc2", 0x12)]
    [InlineData("dc3", 0x13)]
    [InlineData("dc4", 0x14)]
    [InlineData("nak", 0x15)]
    [InlineData("syn", 0x16)]
    [InlineData("etb", 0x17)]
    [InlineData("can", 0x18)]
    [InlineData("em", 0x19)]
    [InlineData("sub", 0x1a)]
    [InlineData("fs", 0x1c)]
    [InlineData("gs", 0x1d)]
    [InlineData("rs", 0x1e)]
    [InlineData("us", 0x1f)]
    [InlineData("sp", 0x20)]
    [InlineData("del", 0x7f)]
    [InlineData("null", 0x00)]
    [InlineData("nl", 0x0a)]
    [InlineData("np", 0x0c)]
    [InlineData("SPACE", 0x20)]
    [InlineData("Cr", 0x0d)]
    [InlineData("NUL", 0x00)]
    [InlineData("Nl", 0x0a)]
    public void reads_every_character_name_the_reference_reader_knows(string name, int codePoint)
    {
        //Arrange / Act
        object value = ReadOne("#\\" + name);

        //Assert
        ((SchemeChar)value).CodePoint.Should().Be(codePoint);
    }

    [Fact]
    public void a_control_abbreviation_is_the_control_character_not_its_first_letter()
    {
        //Arrange -- the two literals LilyPond's lily.scm spells at line 1055
        //Act
        int cr = ((SchemeChar)ReadOne("#\\cr")).CodePoint;
        int nl = ((SchemeChar)ReadOne("#\\nl")).CodePoint;

        //Assert
        cr.Should().Be('\r');
        nl.Should().Be('\n');

        //Assert -- the CONTROL: the one-letter literals they were once mistaken for,
        //which must keep answering the LETTERS
        ((SchemeChar)ReadOne("#\\c")).CodePoint.Should().Be('c');
        ((SchemeChar)ReadOne("#\\n")).CodePoint.Should().Be('n');
    }

    // The numeric escapes. Octal is written bare and hex takes a LOWER-CASE x; the
    // digits of either may be upper case. A leading digit that does not make a valid
    // octal number falls through to the name table rather than raising, which is why
    // #\19 is an unknown NAME and #\8 is simply the character 8.
    [Theory]
    [InlineData("x41", 0x41)]
    [InlineData("x7F", 0x7f)]
    [InlineData("x10FFFF", 0x10ffff)]
    [InlineData("101", 0x41)]
    [InlineData("0", '0')]
    [InlineData("7", '7')]
    [InlineData("8", '8')]
    public void reads_the_numeric_character_escapes(string token, int codePoint)
        => ((SchemeChar)ReadOne("#\\" + token)).CodePoint.Should().Be(codePoint);

    [Fact]
    public void reads_a_character_literal_that_needs_a_surrogate_pair()
        //Arrange / Act / Assert -- U+1D11E MUSICAL SYMBOL G CLEF; one Scheme character,
        //two UTF-16 units, and the oracle answers 119070
        => ((SchemeChar)ReadOne("#\\\U0001D11E")).CodePoint.Should().Be(0x1D11E);

    [Fact]
    public void a_dotted_circle_beside_a_combining_character_is_dropped()
    {
        //Arrange / Act -- U+0301 COMBINING ACUTE ACCENT written with U+25CC DOTTED
        //CIRCLE so that it does not combine with the backslash
        object value = ReadOne("#\\́◌");

        //Assert -- measured 769 on the oracle
        ((SchemeChar)value).CodePoint.Should().Be(0x0301);

        //Assert -- the CONTROL, and the reason this had to be measured rather than
        //read off upstream's C source: read.c tests the FIRST character and answers
        //the second, ice-9/read.scm tests the SECOND and answers the first, and it is
        //ice-9/read.scm that runs -- so the other order is REFUSED
        System.Action act = () => ReadOne("#\\◌́");
        act.Should().Throw<SchemeReaderException>()
            .WithMessage("*unknown character name*");
    }

    // Four forms this reader used to accept that the reference reader refuses, and one
    // shape of name it never had. All five measured as errors on the oracle.
    [Theory]
    [InlineData("rubout")]
    [InlineData("X41")]
    [InlineData("u41")]
    [InlineData("U41")]
    [InlineData("19")]
    [InlineData("nosuchname")]
    public void refuses_a_character_form_the_reference_reader_refuses(string token)
    {
        //Arrange / Act
        System.Action act = () => ReadOne("#\\" + token);

        //Assert
        act.Should().Throw<SchemeReaderException>()
            .WithMessage("*unknown character name*");
    }

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
