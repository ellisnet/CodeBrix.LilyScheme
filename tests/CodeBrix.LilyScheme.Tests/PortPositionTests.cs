using System;
using System.IO;
using System.Text;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The line and column every output port carries, and the one thing that made the gap
/// visible: <c>ice-9/pretty-print.scm</c> printing a form on a single unreadable line.
/// <para>
/// <c>port-column</c> used to answer for exactly two kinds of port — a soft port, which
/// keeps its own counters, and a string port, whose text could be re-read after the fact
/// — and returned a flat <c>0</c> for every other, the process's own output included.
/// <c>pretty-print</c>'s whole line-breaking decision is that number: <c>indent</c> emits
/// a newline when the target column is behind the current one and spaces otherwise, and
/// <c>pp-list</c> passes <c>(port-column port)</c> ITSELF as the target. Stuck at zero,
/// <c>indent</c> took neither branch — no newline, and <c>(spaces 0)</c> writes nothing —
/// so the separator between list items vanished and
/// <c>(make-music'SequentialMusic'elements(list …))</c> is what came out.
/// </para>
/// <para>
/// EVERY expected value below was read off the pinned 2.27.2 oracle FIRST, by running the
/// same expression through its Guile 3.0, and each is paired with a CONTROL that must come
/// out differently — a column assertion is otherwise satisfied by any implementation that
/// answers a plausible number. The tab, carriage-return, backspace, alarm and surrogate
/// rules are all measured facts, not readings of Guile's source: a tab advances to the
/// next multiple of eight, a carriage return returns the column without advancing the
/// line, a backspace retreats one but never past zero, an alarm advances nothing, and a
/// column counts CODE POINTS, so two astral characters make column two and not four.
/// </para>
/// </summary>
public class PortPositionTests
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

    /// <summary>
    /// Evaluates every source with the default output port redirected, returning what was
    /// WRITTEN rather than what was returned.
    /// </summary>
    /// <remarks>
    /// The sink is deliberately NOT a <see cref="StringWriter"/>. A string writer was one
    /// of the only two kinds the old <c>port-column</c> could answer for — it re-read the
    /// accumulated text — so capturing into one would reproduce none of the defect and
    /// every case here would pass with the fix reverted. A <see cref="StreamWriter"/> over
    /// a memory stream is what the process's own <c>Console.Out</c> looks like to this
    /// code: a writer whose text cannot be read back, which is why the column had to be
    /// TRACKED rather than recomputed.
    /// </remarks>
    private static string EvalCapturingOutput(params string[] sources)
    {
        MemoryStream stream = new MemoryStream();
        StreamWriter captured = new StreamWriter(stream) { AutoFlush = true };
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            interpreter.OutputWriter = captured;
            SchemeBootstrap.LoadCore(interpreter);
            foreach (string source in sources)
            {
                foreach (object form in SchemeReader.ReadAll(source, "<test>"))
                {
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
                }
            }
        });

        captured.Flush();
        return new UTF8Encoding(false)
            .GetString(stream.ToArray())
            .Replace("\r\n", "\n");
    }

    /// <summary>
    /// Evaluates every source with the default output port redirected away from the
    /// console, returning the written form of the last result.
    /// </summary>
    /// <remarks>
    /// Same sink choice, same reason, as <see cref="EvalCapturingOutput"/>; this one is
    /// for the cases that ASK the port its position instead of reading what it wrote.
    /// </remarks>
    private static string EvalOnDefaultPort(params string[] sources)
    {
        string result = null;
        StreamWriter sink = new StreamWriter(new MemoryStream()) { AutoFlush = true };
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            interpreter.OutputWriter = sink;
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
    public void pretty_print_breaks_a_long_form_across_indented_lines()
    {
        //Arrange
        // The exact nine lines the oracle prints for this form, captured from
        // (pretty-print '(make-music …)) under LilyPond 2.27.2's Guile 3.0. This is the
        // shape display-scheme-music produces, which is where the defect was seen.
        string expected =
            "(make-music\n"
            + " 'SequentialMusic\n"
            + " 'elements\n"
            + " (list (make-music\n"
            + "        'NoteEvent\n"
            + "        'duration\n"
            + "        (ly:make-duration 2)\n"
            + "        'pitch\n"
            + "        (ly:make-pitch 0 0 0))))\n";

        //Act
        string written = EvalCapturingOutput(
            "(use-modules (ice-9 pretty-print))",
            "(pretty-print '(make-music 'SequentialMusic 'elements"
            + " (list (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + " 'pitch (ly:make-pitch 0 0 0)))))");

        //Assert
        written.Should().Be(expected);
    }

    [Fact]
    public void pretty_print_leaves_a_form_that_fits_on_one_line()
    {
        //Arrange
        // THE CONTROL for the case above: the fix must not make pretty-print break
        // everything. A short form stays on its single line on the oracle too, so a
        // change that merely inserted newlines would fail here.
        //Act
        string written = EvalCapturingOutput(
            "(use-modules (ice-9 pretty-print))",
            "(pretty-print '(a b c))");

        //Assert
        written.Should().Be("(a b c)\n");
    }

    [Fact]
    public void port_column_tracks_the_default_output_port()
    {
        //Arrange
        // The port that answered a flat 0 before: the process's own output. The oracle
        // reports 4 here, and 0 once a newline has passed — the CONTROL, which a stuck
        // counter would also satisfy if only the first half were asserted.
        //Act
        string result = EvalOnDefaultPort(
            "(define p (current-output-port))",
            "(display \"abcd\" p)",
            "(let ((after-text (port-column p)))"
            + " (newline p)"
            + " (list after-text (port-column p)))");

        //Assert
        result.Should().Be("(4 0)");
    }

    [Fact]
    public void a_tab_advances_the_column_to_the_next_multiple_of_eight()
    {
        //Arrange
        // Measured on the oracle at five starting columns: 0, 1 and 7 all land on 8,
        // while 8 and 9 land on 16. The five together are their own control — a plain
        // "one character, one column" rule gives (1 2 8 9 10) and fails on every one.
        //Act
        string result = Eval(
            "(define (col s) (let ((p (open-output-string))) (display s p) (port-column p)))",
            "(list (col \"\\t\") (col \"a\\t\") (col \"1234567\\t\")"
            + " (col \"12345678\\t\") (col \"123456789\\t\"))");

        //Assert
        result.Should().Be("(8 8 8 16 16)");
    }

    [Fact]
    public void a_carriage_return_resets_the_column_without_advancing_the_line()
    {
        //Arrange
        // The oracle: "abc\r" is column 0 and line 0; a following character is at 1.
        // CONTROL: a newline in the same position advances the line to 1, so the two
        // cannot be conflated.
        //Act
        string result = Eval(
            "(define (pos s)"
            + " (let ((p (open-output-string)))"
            + "  (display s p) (list (port-column p) (port-line p))))",
            "(list (pos \"abc\\r\") (pos \"abc\\rx\") (pos \"abc\\n\"))");

        //Assert
        result.Should().Be("((0 0) (1 0) (0 1))");
    }

    [Fact]
    public void a_backspace_retreats_one_column_and_stops_at_zero()
    {
        //Arrange
        // Measured: "abc\b" is 2, a leading "\b" is 0 rather than -1, and "a\b\b" is 0.
        // CONTROLS in the same list: an alarm advances NOTHING ("abc\a" is 3) while a
        // form feed is an ordinary character ("abc\f" is 4) — three different rules that
        // a single "every character counts one" implementation gets wrong three ways.
        //Act
        string result = Eval(
            "(define (col s) (let ((p (open-output-string))) (display s p) (port-column p)))",
            "(list (col \"abc\\b\") (col \"\\b\") (col \"a\\b\\b\")"
            + " (col \"abc\\a\") (col \"abc\\f\"))");

        //Assert
        result.Should().Be("(2 0 0 3 4)");
    }

    [Fact]
    public void set_port_column_and_set_port_line_are_real_setters()
    {
        //Arrange
        // They were no-ops returning unspecified. On the oracle (set-port-column! p 42)
        // takes effect and the NEXT character lands at 43 — the second element is the
        // control that separates a real setter from one that merely records a number.
        //Act
        string result = Eval(
            "(define p (open-output-string))",
            "(display \"abc\" p)",
            "(set-port-column! p 42)",
            "(set-port-line! p 7)",
            "(let ((set-col (port-column p)) (set-line (port-line p)))"
            + " (display \"x\" p)"
            + " (list set-col set-line (port-column p)))");

        //Assert
        result.Should().Be("(42 7 43)");
    }

    [Fact]
    public void a_column_counts_code_points_and_not_utf16_units()
    {
        //Arrange
        // Two astral characters are column 2 on the oracle, not 4. CONTROL: two ordinary
        // BMP characters are also 2, and "a" between them makes 3 — so the assertion is
        // about the surrogate pair and not about the count happening to be small.
        //Act
        string result = Eval(
            "(define (col s) (let ((p (open-output-string))) (display s p) (port-column p)))",
            "(list (col \"\\U01D160\\U01D161\") (col \"ab\") (col \"a\\U01D160b\"))");

        //Assert
        result.Should().Be("(2 2 3)");
    }

    [Fact]
    public void a_file_port_tracks_its_position_like_a_string_port()
    {
        //Arrange
        // A file port is the other kind that answered 0. The oracle reports column 5 and
        // line 1 for this text. CONTROL: a string port fed the SAME text answers the same
        // pair, and a port with nothing written answers (0 0).
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-position-" + Guid.NewGuid().ToString("N") + ".txt");
        string quoted = Printer.WriteString(path);

        try
        {
            //Act
            string result = Eval(
                "(define f (open-output-file " + quoted + "))",
                "(display \"hello\\nworld\" f)",
                "(define measured (list (port-column f) (port-line f)))",
                "(close-port f)",
                "(define s (open-output-string))",
                "(display \"hello\\nworld\" s)",
                "(define empty (open-output-string))",
                "(list measured (list (port-column s) (port-line s))"
                + " (list (port-column empty) (port-line empty)))");

            //Assert
            result.Should().Be("((5 1) (5 1) (0 0))");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void an_input_port_tracks_its_line_and_column_as_characters_are_read()
    {
        //Arrange
        // The oracle, character by character over "abc\ndefgh": a fresh port is (0 0),
        // three characters make (0 3), the newline makes (1 0) and the next makes (1 1).
        // The fresh reading is the CONTROL — a port that answered its final position from
        // the start, or one stuck at zero, fails on one end or the other.
        //Act
        string result = Eval(
            "(define p (open-input-string \"abc\\ndefgh\"))",
            "(define (pos) (list (port-line p) (port-column p)))",
            "(define fresh (pos))",
            "(read-char p) (read-char p) (read-char p)",
            "(define after-abc (pos))",
            "(read-char p)",
            "(define after-newline (pos))",
            "(read-char p)",
            "(list fresh after-abc after-newline (pos))");

        //Assert
        result.Should().Be("((0 0) (0 3) (1 0) (1 1))");
    }

    [Fact]
    public void peek_char_does_not_advance_and_unread_char_retreats()
    {
        //Arrange
        // Measured: peek leaves the position alone, unread takes the column back one, and
        // unreading a NEWLINE takes the LINE back instead — Guile cannot know how long the
        // previous line was, so the column stays put. The read between them is the control.
        //Act
        string result = Eval(
            "(define p (open-input-string \"a\\nb\"))",
            "(read-char p) (read-char p)",
            "(define after-newline (list (port-line p) (port-column p)))",
            "(peek-char p)",
            "(define after-peek (list (port-line p) (port-column p)))",
            "(read-char p)",
            "(define after-b (list (port-line p) (port-column p)))",
            "(unread-char #\\b p)",
            "(define after-unread (list (port-line p) (port-column p)))",
            "(unread-char #\\newline p)",
            "(list after-newline after-peek after-b after-unread"
            + " (list (port-line p) (port-column p)))");

        //Assert
        result.Should().Be("((1 0) (1 0) (1 1) (1 0) (0 0))");
    }

    [Fact]
    public void an_input_port_takes_a_tab_to_the_next_tab_stop()
    {
        //Arrange
        // The same rule as an output port, measured on the oracle: reading "a" then a tab
        // leaves the column at 8. CONTROL: reading a carriage return returns it to 0
        // WITHOUT advancing the line, which a shared "any control character" rule would
        // get wrong.
        //Act
        string result = Eval(
            "(define t (open-input-string \"a\\tb\"))",
            "(read-char t) (read-char t)",
            "(define tabbed (port-column t))",
            "(define r (open-input-string \"abc\\rx\"))",
            "(read-char r) (read-char r) (read-char r) (read-char r)",
            "(list tabbed (port-line r) (port-column r))");

        //Assert
        result.Should().Be("(8 0 0)");
    }

    [Fact]
    public void a_datum_records_the_ports_position_as_its_source_location()
    {
        //Arrange
        // source-properties ARE the port's line and column at the datum's first character
        // — the oracle reports line 2 column 3 for a form after two blank lines and three
        // spaces. CONTROL: a datum at the very start records (0 0), so the assertion is
        // not satisfied by an implementation that reports a constant.
        //Act
        string result = Eval(
            "(define (where text)"
            + " (let ((props (source-properties (read (open-input-string text)))))"
            + "  (list (assq-ref props 'line) (assq-ref props 'column))))",
            "(list (where \"\\n\\n   (hello world)\") (where \"(hello world)\"))");

        //Assert
        result.Should().Be("((2 3) (0 0))");
    }

    [Fact]
    public void setting_an_input_ports_position_moves_where_the_next_datum_is_recorded()
    {
        //Arrange
        // THE CASE THAT MATTERS: LilyPond's parser-ly-from-scheme.scm syncs a second port
        // over the same text with set-port-line! / set-port-column! precisely so that
        // #{ ... #} embedded Scheme records the location of its real source. Both were
        // no-ops on an input port, so the sync did nothing at all. The oracle records
        // (40 7); the CONTROL is the same text read without the sync, which records (0 0).
        //Act
        string result = Eval(
            "(define (where-after-set text line column)"
            + " (let ((p (open-input-string text)))"
            + "  (set-port-line! p line)"
            + "  (set-port-column! p column)"
            + "  (let ((props (source-properties (read p))))"
            + "   (list (assq-ref props 'line) (assq-ref props 'column)))))",
            "(list (where-after-set \"(alpha beta)\" 40 7)"
            + " (where-after-set \"(alpha beta)\" 0 0))");

        //Assert
        result.Should().Be("((40 7) (0 0))");
    }

    [Fact]
    public void a_tab_shifts_a_recorded_source_column_to_the_tab_stop()
    {
        //Arrange
        // The reader used to advance one column per character, so a tab counted as 1 and
        // every source location on a tab-indented line was short. Measured on the oracle:
        // a leading tab records column 8, EXACTLY as eight spaces do — that pair is the
        // control — and a tab followed by two spaces records 10.
        //Act
        string result = Eval(
            "(define (col text)"
            + " (assq-ref (source-properties (read (open-input-string text))) 'column))",
            "(list (col \"\\t(x)\") (col \"        (x)\") (col \"\\t  (x)\"))");

        //Assert
        result.Should().Be("(8 8 10)");
    }

    [Fact]
    public void a_string_port_still_answers_get_output_string_through_the_wrapper()
    {
        //Arrange
        // The position wrapper sits between the port and its StringWriter, so anything
        // reading the concrete sink has to look THROUGH it. Without that, this returns
        // the empty string and every string port in the library goes silently blank.
        // CONTROL: a port with nothing written really is empty, so the assertion is not
        // satisfied by an implementation that always answers "".
        //Act
        string result = Eval(
            "(define p (open-output-string))",
            "(display \"abc\" p)",
            "(define q (open-output-string))",
            "(list (get-output-string p) (get-output-string q) (ftell p))");

        //Assert
        result.Should().Be("(\"abc\" \"\" 3)");
    }
}
