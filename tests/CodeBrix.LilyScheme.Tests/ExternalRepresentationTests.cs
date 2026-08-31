using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// What a value WRITES AS, for the three kinds whose external representation did not read
/// back: bytevectors, symbols needing Guile's <c>#{...}#</c> syntax, and arrays.
/// <para>
/// A bytevector printed as <c>System.Byte[]</c> — a .NET type name leaking into Scheme
/// output. A symbol printed its bare name, so the symbol <c>.</c> printed as <c>.</c> and
/// a symbol containing a space printed as though it were two. And arrays refused rank
/// zero, which upstream reads, printed a rank-1 array with a rank digit upstream omits,
/// and reported a ragged literal with the wrong condition KEY.
/// </para>
/// <para>
/// Every expected string was measured on the pinned 2.27.2 oracle, most of them from a
/// character-by-character table (<c>a&lt;c&gt;b</c> and <c>&lt;c&gt;</c> alone for each of
/// 33 punctuation characters), because the rules are not derivable: <c>(</c> forces the
/// extended syntax AND is hex-escaped inside it, <c>;</c> forces it but stays literal, and
/// <c>'</c> forces it only when it comes FIRST. Each case is paired with a control that
/// must come out differently.
/// </para>
/// </summary>
public class ExternalRepresentationTests
{
    /// <summary>Evaluates sources, returning the written form of the last result.</summary>
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

    // What a CHARACTER writes as. A graphic character writes as itself; everything
    // else takes a name, and this half of the table knew five where the reader knew
    // twelve and Guile knows fifty-one -- so a control character wrote as ITSELF, a
    // raw byte in the middle of Scheme output. Every row measured on the oracle.
    [Theory]
    [InlineData(0x01, "#\\soh")]
    [InlineData(0x07, "#\\alarm")]
    [InlineData(0x08, "#\\backspace")]
    [InlineData(0x0b, "#\\vtab")]
    [InlineData(0x0c, "#\\page")]
    [InlineData(0x0d, "#\\return")]
    [InlineData(0x0e, "#\\so")]
    [InlineData(0x1a, "#\\sub")]
    [InlineData(0x1b, "#\\esc")]
    [InlineData(0x1c, "#\\fs")]
    [InlineData(0x1f, "#\\us")]
    [InlineData(0x7f, "#\\delete")]
    [InlineData(0x00, "#\\nul")]
    [InlineData(0x0a, "#\\newline")]
    [InlineData(0x09, "#\\tab")]
    [InlineData(0x20, "#\\space")]
    [InlineData(0x41, "#\\A")]
    public void a_character_writes_with_the_name_the_reference_printer_uses(
        int codePoint, string expected)
    {
        //Arrange / Act
        string written = Eval("(integer->char " + codePoint + ")");

        //Assert
        written.Should().Be(expected);
    }

    [Fact]
    public void the_printed_name_is_the_first_one_its_code_point_answers_to()
    {
        //Arrange / Act / Assert
        // Several names read to one code point, so the WRITE side has to pick, and the
        // order is the reader's own table order. These four are where it shows.
        Eval("(integer->char 13)").Should().Be("#\\return");   // not cr
        Eval("(integer->char 10)").Should().Be("#\\newline");  // not linefeed or nl
        Eval("(integer->char 12)").Should().Be("#\\page");     // not ff or np
        Eval("(integer->char 127)").Should().Be("#\\delete");  // not del

        //Assert -- the CONTROL: each of those names still READS, so the round trip holds
        Eval("(char->integer #\\cr)").Should().Be("13");
        Eval("(char->integer #\\linefeed)").Should().Be("10");
        Eval("(char->integer #\\np)").Should().Be("12");
        Eval("(char->integer #\\del)").Should().Be("127");
    }

    [Fact]
    public void every_character_a_name_covers_reads_back_from_what_it_wrote()
    {
        //Arrange / Act / Assert
        // The property the whole table exists for, asserted as a RELATIONSHIP rather
        // than a literal: writing a character and reading the result must give the
        // character back, for every code point through 0xFF.
        Eval("(let loop ((n 0) (bad '()))"
             + "  (if (> n 255) (if (null? bad) 'ok bad)"
             + "      (loop (+ n 1)"
             + "            (if (eqv? (integer->char n)"
             + "                      (with-input-from-string"
             + "                        (call-with-output-string"
             + "                          (lambda (p) (write (integer->char n) p)))"
             + "                        read))"
             + "                bad (cons n bad)))))")
            .Should().Be("ok");
    }

    [Fact]
    public void a_bytevector_writes_as_its_own_literal()
    {
        //Arrange
        // It printed "System.Byte[]". CONTROL: the empty one and a nested one, so the
        // assertion is not satisfied by hardcoding a single literal.
        //Act
        string result = Eval(
            "(define (rd t) (read (open-input-string t)))",
            "(list (rd \"#vu8(1 2)\") (rd \"#vu8()\") (list 'a (rd \"#vu8(0 255 128)\") 'b))");

        //Assert
        result.Should().Be("(#vu8(1 2) #vu8() (a #vu8(0 255 128) b))");
    }

    [Fact]
    public void a_symbol_that_would_not_read_back_is_written_in_extended_syntax()
    {
        //Arrange
        // The first five need it: the lone dot, the empty name, a name with a space, a
        // name starting with a digit, and a name that reads as a NUMBER. The last five are
        // the CONTROL and must stay bare — including a.b and a'b, where the same
        // characters are harmless because they are not first.
        //Act
        string result = Eval(
            "(define (s t) (string->symbol t))",
            "(list (s \".\") (s \"\") (s \"a b\") (s \"1abc\") (s \"+1\")"
            + "      (s \"abc\") (s \"+\") (s \"...\") (s \"a.b\") (s \"a'b\"))");

        //Assert
        result.Should().Be("(#{.}# #{}# #{a b}# #{1abc}# #{+1}# abc + ... a.b a'b)");
    }

    [Fact]
    public void the_extended_syntax_escapes_only_the_bracketing_and_control_characters()
    {
        //Arrange
        // The split is upstream's and is not tidy: parens, brackets and braces become
        // \xN; with MINIMAL lower-case hex, and so do control characters — but a double
        // quote, a #, a semicolon, a space and even a BACKSLASH are written literally
        // inside the braces. The literal group is the control for the escaped one.
        //Act
        string result = Eval(
            "(define (s t) (string->symbol t))",
            "(list (s \"a(b\") (s \"a}#b\") (s \"a\\tb\") (s \"a\\nb\")"
            + "      (s \"a\\\"b\") (s \"a#b\") (s \"a;b\") (s \"a\\\\ b\"))");

        //Assert
        result.Should().Be(
            "(#{a\\x28;b}# #{a\\x7d;#b}# #{a\\x9;b}# #{a\\xa;b}#"
            + " #{a\"b}# #{a#b}# #{a;b}# #{a\\ b}#)");
    }

    [Fact]
    public void a_leading_quote_comma_or_backtick_needs_the_extended_syntax()
    {
        //Arrange
        // They are reader syntax only at the START of a token, so the same character in
        // the middle needs nothing — which is the control, and is why a blanket
        // "contains a quote" rule would be wrong.
        //Act
        string result = Eval(
            "(define (s t) (string->symbol t))",
            "(list (s \"'ab\") (s \",ab\") (s \"`ab\")"
            + "      (s \"a'b\") (s \"a,b\") (s \"a`b\") (s \".ab\"))");

        //Assert
        result.Should().Be("(#{'ab}# #{,ab}# #{`ab}# a'b a,b a`b .ab)");
    }

    [Fact]
    public void a_rank_zero_array_reads_writes_and_indexes()
    {
        //Arrange
        // #0(a) was REFUSED as needing "a positive rank"; upstream reads it, and it is the
        // one array indexed by nothing at all. CONTROL: a rank-2 literal still reads,
        // prints with its rank and answers rank 2.
        //Act
        string result = Eval(
            "(define zero (read (open-input-string \"#0(a)\")))",
            "(define two (read (open-input-string \"#2((a b) (c d))\")))",
            "(list zero (array-rank zero) (array-ref zero) (array? zero)"
            + " two (array-rank two))");

        //Assert
        result.Should().Be("(#0(a) 0 a #t #2((a b) (c d)) 2)");
    }

    [Fact]
    public void a_rank_one_array_writes_without_its_rank_digit()
    {
        //Arrange
        // Upstream writes an ordinary rank-1 array exactly like a vector. CONTROL: the
        // digit comes BACK as soon as the array carries a lower bound, so this is not
        // simply "never print the rank".
        //Act
        string result = Eval(
            "(define (rd t) (read (open-input-string t)))",
            "(list (rd \"#1(a b)\") (rd \"#1@1(a b)\") (rd \"#3(((a)))\"))");

        //Assert
        result.Should().Be("(#(a b) #1@1(a b) #3(((a))))");
    }

    [Fact]
    public void a_ragged_array_literal_is_a_misc_error_and_not_a_read_error()
    {
        //Arrange
        // Upstream finds this while BUILDING the array, not while reading it, so the key
        // is misc-error and the message names the dimension and the length it wanted.
        // CONTROL: a well-formed literal of the same shape reads.
        //Act
        string result = Eval(
            "(define (try t)"
            + " (catch #t (lambda () (read (open-input-string t))) (lambda k k)))",
            "(list (try \"#2((a b) (c))\") (try \"#2((a b) (c d))\"))");

        //Assert
        result.Should().Be(
            "((misc-error #f \"too few elements for array dimension ~a, need ~a\" (1 2) #f)"
            + " #2((a b) (c d)))");
    }

    [Fact]
    public void an_array_literal_that_runs_out_of_input_says_so()
    {
        //Arrange
        // "#2 a" reported the wrong thing — a missing paren rather than the end of input
        // upstream names. CONTROL: "#2@x(a)" really IS a missing-paren case and keeps that
        // message, so the two are not collapsed into one.
        //Act
        string result = Eval(
            "(define (why t)"
            + " (catch #t (lambda () (read (open-input-string t))) (lambda k (caddr k))))",
            "(list (why \"#2 a\") (why \"#2@x(a)\"))");

        //Assert
        result.Should().Be(
            "(\"#<unknown port>:1:5: unexpected end of input while reading array\""
            + " \"#<unknown port>:1:5: missing '(' in vector or array literal\")");
    }

    [Fact]
    public void an_extended_symbol_with_a_bad_hex_escape_raises_a_read_error()
    {
        //Arrange
        // The third of the reader's unvalidated int.Parse calls — the other two were the
        // string and character escapes. CONTROL: the well-formed escape still reads, and
        // reads back as the character it names.
        //Act
        string result = Eval(
            "(define (try t)"
            + " (catch #t (lambda () (read (open-input-string t))) (lambda k (car k))))",
            "(list (try \"#{a\\\\xzz;b}#\") (try \"#{a\\\\x28;b}#\"))");

        //Assert
        result.Should().Be("(read-error #{a\\x28;b}#)");
    }
}
