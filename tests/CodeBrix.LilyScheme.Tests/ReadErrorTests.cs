using System;
using System.IO;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The reader's ERROR surface: a syntax error is Guile's <c>read-error</c> condition,
/// catchable from Scheme, carrying upstream's own message text, position and arguments.
/// <para>
/// It used to be a plain .NET <see cref="Exception"/> that <c>(catch #t ...)</c> went
/// straight past, so no Scheme code could recover from a bad datum — and three places
/// let a raw .NET exception out of the reader entirely (<c>int.Parse</c> on whatever had
/// been collected). Worse than either, several malformed inputs were ACCEPTED: an unknown
/// string escape dropped its backslash, an unterminated block comment read as end of
/// input, a mismatched bracket closed the list anyway, and <c>#\nosuchchar</c> answered
/// <c>#\n</c> — a silently WRONG character rather than a refusal.
/// </para>
/// <para>
/// EVERY expected string here was read off the pinned 2.27.2 oracle FIRST, one input at a
/// time, and each case is paired with a CONTROL that must come out differently — usually
/// the well-formed input the malformed one is one character away from. The wording is
/// upstream's verbatim, INCLUDING its own inconsistencies: <c>#z</c> reports a capitalised
/// "Unknown # object" while <c>#d1x2</c> reports a lower-case "unknown # object", and
/// <c>unknown character name ~a</c> takes a lower-case directive where its neighbours take
/// <c>~S</c>. Those are reproduced, not tidied.
/// </para>
/// </summary>
public class ReadErrorTests
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

    /// <summary>Reads one datum from a string port, returning the caught condition.</summary>
    /// <param name="literal">The Scheme string literal to read from.</param>
    /// <returns>The written form of the caught throw, or <c>(ok VALUE)</c>.</returns>
    private static string ReadCatching(string literal)
        => Eval(
            "(catch #t"
            + " (lambda () (list 'ok (read (open-input-string " + literal + "))))"
            + " (lambda k k))");

    [Fact]
    public void a_read_error_is_catchable_by_its_own_key()
    {
        //Arrange
        // THE HEADLINE. Before this, the reader threw a .NET exception straight through
        // every Scheme handler. CONTROL: the same catch around a WELL-FORMED read runs the
        // thunk, not the handler, so the assertion is not satisfied by a catch that fires
        // unconditionally.
        //Act
        string result = Eval(
            "(list (catch 'read-error"
            + "        (lambda () (read (open-input-string \"(a\")))"
            + "        (lambda k (list 'caught (car k))))"
            + "      (catch 'read-error"
            + "        (lambda () (read (open-input-string \"(a)\")))"
            + "        (lambda k (list 'caught (car k)))))");

        //Assert
        result.Should().Be("((caught read-error) (a))");
    }

    [Fact]
    public void a_read_error_carries_guiles_own_condition_shape()
    {
        //Arrange
        // The whole condition, character for character off the oracle: key read-error, NO
        // subr, the position folded into the message text, the format ARGUMENTS kept
        // beside it rather than substituted in, and no rest.
        //Act
        string result = ReadCatching("\"(a b\"");

        //Assert
        result.Should().Be(
            "(read-error #f \"#<unknown port>:1:5: unexpected end of input"
            + " while searching for: ~A\" (#\\)) #f)");
    }

    [Fact]
    public void a_read_error_names_the_port_it_read_from()
    {
        //Arrange
        // A string port has no name and reports #<unknown port>; a FILE port names itself.
        // The pair is the control — a message that hardcoded either one passes half of it.
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-readerr-" + Guid.NewGuid().ToString("N") + ".scm");
        File.WriteAllText(path, "(a b");
        string quoted = Printer.WriteString(path);

        try
        {
            //Act
            string result = Eval(
                "(catch #t"
                + " (lambda () (read (open-input-file " + quoted + ")))"
                + " (lambda k (caddr k)))");

            //Assert
            result.Should().Be(
                "\"" + path + ":1:5: unexpected end of input while searching for: ~A\"");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void a_read_error_counts_line_and_column_from_one()
    {
        //Arrange
        // The message counts from ONE while the PORT counts from zero — measured: reading
        // ")" leaves the port at column 1 and the message says 1:2. Both halves are
        // asserted together, so a change that made them agree would fail.
        //Act
        string result = Eval(
            "(define p (open-input-string \")\"))",
            "(define message (catch #t (lambda () (read p)) (lambda k (caddr k))))",
            "(list message (port-line p) (port-column p))");

        //Assert
        result.Should().Be("(\"#<unknown port>:1:2: unexpected \\\")\\\"\" 0 1)");
    }

    [Fact]
    public void each_construct_names_itself_when_the_input_runs_out()
    {
        //Arrange
        // Not derivable, so all four were measured: a quote reads a "quoted expression"
        // but ,@ reads a "subexpression of ,@", and the syntax family spells its own. The
        // four together are their own control — one shared phrase fails three of them.
        //Act
        string result = Eval(
            "(define (why text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (caddr k))))",
            "(list (why \"'\") (why \",@\") (why \"#`\") (why \"(a .\"))");

        //Assert
        result.Should().Be(
            "(\"#<unknown port>:1:2: unexpected end of input while reading quoted expression\""
            + " \"#<unknown port>:1:3: unexpected end of input while reading subexpression of ,@\""
            + " \"#<unknown port>:1:3: unexpected end of input while reading quasisyntax expression\""
            + " \"#<unknown port>:1:5: unexpected end of input while reading tail of improper"
            + " list\")");
    }

    [Fact]
    public void the_two_unknown_hash_object_spellings_are_reproduced_as_upstream_spells_them()
    {
        //Arrange
        // Upstream capitalises one site and not the other. Asserting BOTH is the control:
        // a single tidied spelling passes one and fails the other, which is exactly the
        // "improvement" the faithfulness rule forbids.
        //Act
        string result = Eval(
            "(define (why text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (caddr k))))",
            "(list (why \"#z\") (why \"#d1x2\"))");

        //Assert
        result.Should().Be(
            "(\"#<unknown port>:1:3: Unknown # object: ~S\""
            + " \"#<unknown port>:1:6: unknown # object: ~S\")");
    }

    [Fact]
    public void an_invalid_string_escape_is_refused_rather_than_silently_unescaped()
    {
        //Arrange
        // "\q" used to read as "q": the backslash vanished and nothing said so. CONTROL:
        // "\n" is a REAL escape and still reads as a newline, so the refusal is aimed at
        // the invalid one and not at escapes generally.
        //Act
        string result = Eval(
            "(define (try text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (car k))))",
            "(list (try \"\\\"\\\\q\\\"\") (try \"\\\"a\\\\nb\\\"\"))");

        //Assert
        result.Should().Be("(read-error \"a\\nb\")");
    }

    [Fact]
    public void an_unterminated_block_comment_and_a_mismatched_bracket_are_refused()
    {
        //Arrange
        // Both used to be ACCEPTED — the comment read as end of input and "(a b]" closed
        // the list anyway. CONTROLS in the same list: a terminated comment is skipped, and
        // both bracket shapes read when they MATCH.
        //Act
        string result = Eval(
            "(define (try text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (car k))))",
            "(list (try \"#| abc\") (try \"(a b]\")"
            + " (try \"#| c |# 7\") (try \"(a b)\") (try \"[a b]\"))");

        //Assert
        result.Should().Be("(read-error read-error 7 (a b) (a b))");
    }

    [Fact]
    public void an_unknown_character_name_is_refused_rather_than_truncated()
    {
        //Arrange
        // #\nosuchchar answered #\n — the FIRST letter of the name, a silently wrong
        // character. CONTROLS: the one-letter form, a named form and the hex form all
        // still read, so the refusal is aimed at names that mean nothing.
        //Act
        string result = Eval(
            "(define (try text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (car k))))",
            "(list (try \"#\\\\nosuchchar\") (try \"#\\\\n\")"
            + " (try \"#\\\\space\") (try \"#\\\\x41\"))");

        //Assert
        result.Should().Be("(read-error #\\n #\\space #\\A)");
    }

    [Fact]
    public void a_malformed_hex_escape_raises_a_read_error_instead_of_a_dotnet_exception()
    {
        //Arrange
        // Two sites parsed whatever they had collected and let a FormatException out of
        // the reader — "\x" collected the closing QUOTE, and #\xzz collected "zz".
        // CONTROLS: the well-formed spellings of both still read.
        //Act
        string result = Eval(
            "(define (try text)"
            + " (catch #t (lambda () (read (open-input-string text))) (lambda k (car k))))",
            "(list (try \"\\\"\\\\x\\\"\") (try \"#\\\\xzz\")"
            + " (try \"\\\"\\\\x41;\\\"\") (try \"#\\\\x41\"))");

        //Assert
        result.Should().Be("(read-error read-error \"A\" #\\A)");
    }

    [Fact]
    public void a_string_port_has_no_name_at_all()
    {
        //Arrange
        // It reported "<string>". The oracle answers #f to port-filename AND puts #f in
        // the datum's source-properties. CONTROL: a file port answers its own path, so
        // the assertion is not satisfied by returning #f for everything.
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-named-" + Guid.NewGuid().ToString("N") + ".scm");
        File.WriteAllText(path, "(a)");
        string quoted = Printer.WriteString(path);

        try
        {
            //Act
            string result = Eval(
                "(define s (open-input-string \"(a)\"))",
                "(define datum (read s))",
                "(define f (open-input-file " + quoted + "))",
                "(list (port-filename s)"
                + " (assq-ref (source-properties datum) 'filename)"
                + " (port-filename f))");

            //Assert
            result.Should().Be("(#f #f \"" + path + "\")");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void the_reader_exception_is_still_the_type_a_csharp_host_catches()
    {
        //Arrange / Act / Assert
        // The C# surface must not have been traded away for the Scheme one: a host that
        // catches SchemeReaderException to report a syntax error goes on working, and the
        // position is still in the message it prints.
        SchemeReaderException failure = Assert.Throws<SchemeReaderException>(
            () => SchemeReader.ReadAll("(1 2", "<test>"));
        failure.ReaderMessage.Should().Be(
            "<test>:1:5: unexpected end of input while searching for: ~A");

        // And the CONTROL for the whole change: it is now a Scheme condition as well.
        Assert.IsAssignableFrom<SchemeThrow>(failure);
    }
}
