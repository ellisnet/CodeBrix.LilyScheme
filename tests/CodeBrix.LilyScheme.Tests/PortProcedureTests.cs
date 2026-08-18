using System;
using System.IO;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The file-reading layer LilyPond's <c>gulp-file-with-encoding</c> stands on, plus
/// <c>and=&gt;</c>, all found by one demand chain: <c>\markup \epsfile</c> expands to
/// <c>\image</c>, which normalizes a possibly-<c>#f</c> background colour with
/// <c>and=&gt;</c> and then reads the EPS with <c>ly:gulp-file</c>.
/// <para>
/// Every expected value here is Guile's DOCUMENTED behaviour, taken from
/// <c>ice-9/ports.scm</c>, <c>ice-9/textual-ports.scm</c> and <c>ice-9/rdelim.scm</c> in
/// the pinned source, not from what this implementation happened to answer. The two
/// end-of-file cases are the ones worth stating: Guile's <c>get-string-all</c> is
/// <c>(read-string port)</c> and read-string returns the EMPTY STRING at end of file,
/// while <c>get-string-n</c> returns the EOF OBJECT when it could not read a single
/// character. R6RS spells the first one the other way round, and copying R6RS here
/// would be a silent divergence from the module LilyPond actually imports.
/// </para>
/// <para>
/// The <c>open-file</c> and <c>file-port?</c> cases below came from a second demand
/// chain, one regression file at a time: <c>scm/backend-library.scm</c> writes a header
/// field through <c>(open-file name "w")</c> and closes it without ever flushing, and
/// <c>scm/graphviz.scm</c> asks <c>file-port?</c> of a port that is deliberately NOT a
/// file. Each expectation is read off Guile — <c>libguile/fports.c</c>'s
/// <c>scm_open_file</c> and <c>scm_file_port_p</c> — and is paired with a control that
/// must come out differently, because every one of them would otherwise pass against an
/// implementation that ignored the mode string or answered <c>#t</c> for any port.
/// </para>
/// </summary>
public class PortProcedureTests
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

    /// <summary>Writes bytes to a scratch file and returns its path.</summary>
    /// <remarks>
    /// The path comes back RAW. Splicing one into Scheme source goes through
    /// <see cref="Printer.WriteString"/>, which is the only thing that escapes a
    /// Windows path correctly -- this helper used to hand-double backslashes and
    /// return a source fragment under a name that promised a path.
    /// </remarks>
    private static string WriteScratch(byte[] bytes)
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-port-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void and_arrow_returns_false_without_calling_the_procedure()
    {
        //Arrange
        // boot-9.scm: (define (and=> value procedure) (and value (procedure value))).
        // The procedure must not run at all when the value is #f — \image relies on
        // that to leave an absent background colour absent rather than normalizing #f.
        //Act
        string result = Eval(
            "(define ran #f)",
            "(list (and=> #f (lambda (v) (set! ran #t) 'called)) ran)");

        //Assert
        result.Should().Be("(#f #f)");
    }

    [Fact]
    public void and_arrow_applies_the_procedure_to_a_true_value()
    {
        //Arrange
        // Every non-#f value is true in Scheme, so the empty list goes THROUGH.
        //Act
        string result = Eval("(list (and=> 4 1+) (and=> '() (lambda (v) 'reached)))");

        //Assert
        result.Should().Be("(5 reached)");
    }

    [Fact]
    public void get_string_all_reads_the_whole_file_through_call_with_input_file()
    {
        //Arrange
        // The shape lily-library.scm's gulp-file-with-encoding uses verbatim.
        string path = WriteScratch(new byte[] { 0x61, 0x62, 0x63 });

        //Act
        string result = Eval(
            "(call-with-input-file " + Printer.WriteString(path) + " get-string-all)");

        //Assert
        result.Should().Be("\"abc\"");
    }

    [Fact]
    public void get_string_all_answers_the_empty_string_at_end_of_file()
    {
        //Arrange
        // Guile's read-string returns "" once the port is exhausted — NOT the eof
        // object, which is what R6RS's get-string-all would answer.
        //Act
        string result = Eval(
            "(let ((p (open-input-string \"ab\")))"
            + "  (list (get-string-all p) (get-string-all p)))");

        //Assert
        result.Should().Be("(\"ab\" \"\")");
    }

    [Fact]
    public void get_string_n_answers_a_short_string_then_the_eof_object()
    {
        //Arrange
        // textual-ports.scm: a full read yields the string, a partial read yields the
        // SHORT string, and a read that got nothing yields the eof object.
        //Act
        string result = Eval(
            "(let ((p (open-input-string \"abcd\")))"
            + "  (list (get-string-n p 3) (get-string-n p 3)"
            + "        (eof-object? (get-string-n p 3))))");

        //Assert
        result.Should().Be("(\"abc\" \"d\" #t)");
    }

    [Fact]
    public void call_with_input_file_honours_the_encoding_keyword()
    {
        //Arrange
        // THE reason the keyword had to be implemented rather than accepted-and-ignored:
        // ly:gulp-file asks for latin1 and ly:gulp-file-utf8 asks for UTF-8, and byte
        // 0xE9 decodes to one character either way but a DIFFERENT one. Reading an EPS
        // as UTF-8 would corrupt every byte above 0x7F.
        string path = WriteScratch(new byte[] { 0xC3, 0xA9 });

        //Act
        string asLatin1 = Eval(
            "(string-length (call-with-input-file " + Printer.WriteString(path)
            + " get-string-all #:encoding \"latin1\"))");
        string asUtf8 = Eval(
            "(string-length (call-with-input-file " + Printer.WriteString(path)
            + " get-string-all #:encoding \"UTF-8\"))");

        //Assert
        asLatin1.Should().Be("2");
        asUtf8.Should().Be("1");
    }

    [Fact]
    public void call_with_input_file_closes_the_port_even_when_the_procedure_throws()
    {
        //Arrange
        // ice-9/ports.scm reaches call-with-port, whose contract is that the port is
        // closed on exit. A port left open would leak a file handle per gulped file,
        // and the batch runner gulps across a whole suite in ONE process.
        string path = WriteScratch(new byte[] { 0x78 });

        // Observed by reading the escaped port afterwards: the file holds "x", so an
        // OPEN port would still hand that back, and a closed one hands back nothing.
        //Act
        string result = Eval(
            "(define saved #f)",
            "(catch #t"
            + "  (lambda () (call-with-input-file " + Printer.WriteString(path)
            + "                (lambda (p) (set! saved p) (throw 'boom))))"
            + "  (lambda args 'caught))",
            "(get-string-all saved)");

        //Assert
        result.Should().Be("\"\"");
    }

    /// <summary>Returns a raw scratch path that does NOT exist yet.</summary>
    private static string ScratchPath()
        => Path.Combine(
            Path.GetTempPath(), "lilyscheme-openfile-" + Guid.NewGuid().ToString("N") + ".txt");

    [Fact]
    public void file_port_p_tells_a_file_from_the_ports_that_merely_have_names()
    {
        //Arrange
        // libguile/fports.c's scm_file_port_p asks whether the port's implementation is
        // the FILE one. The distinction is the whole of scm/graphviz.scm's graph-write:
        // it gates (port-filename out) on it, and its regression file writes the graph to
        // (current-error-port). Answering by "has a name" would take the wrong branch --
        // a string port carries the name <string> in Guile too.
        string path = WriteScratch(new byte[] { 0x78 });

        //Act
        string ofFile = Eval("(file-port? (open-input-file " + Printer.WriteString(path) + "))");
        string ofStringPort = Eval("(file-port? (open-input-string \"x\"))");
        string ofErrorPort = Eval("(file-port? (current-error-port))");
        string ofNotAPort = Eval("(file-port? 42)");

        //Assert
        ofFile.Should().Be("#t");
        ofStringPort.Should().Be("#f");
        ofErrorPort.Should().Be("#f");
        ofNotAPort.Should().Be("#f");
    }

    [Fact]
    public void open_file_round_trips_through_the_mode_string()
    {
        //Arrange
        // scm_open_file takes a MODE STRING, not the keywords open-input-file takes.
        // scm/backend-library.scm's output-scope writes a header field with
        // (open-file file-name "w") and reads nothing back; the read side is asserted
        // here because "w" and "r" must name the same file for that to be worth anything.
        string path = ScratchPath();

        //Act
        string written = Eval(
            "(let ((p (open-file " + Printer.WriteString(path) + " \"w\")))"
            + "  (display \"hello\" p) (close-port p))",
            "(call-with-input-file " + Printer.WriteString(path) + " get-string-all)");

        //Assert
        written.Should().Be("\"hello\"");
    }

    [Fact]
    public void open_file_append_mode_keeps_what_write_mode_would_truncate()
    {
        //Arrange
        // The control is the POINT: "a" and "w" differ only in whether the existing
        // contents survive, so asserting the append alone would pass against an
        // implementation that ignored the mode character entirely.
        string appended = ScratchPath();
        string truncated = ScratchPath();

        //Act
        string afterAppend = Eval(
            "(let ((p (open-file " + Printer.WriteString(appended) + " \"w\"))) (display \"aa\" p) (close-port p))",
            "(let ((p (open-file " + Printer.WriteString(appended) + " \"a\"))) (display \"bb\" p) (close-port p))",
            "(call-with-input-file " + Printer.WriteString(appended) + " get-string-all)");
        string afterSecondWrite = Eval(
            "(let ((p (open-file " + Printer.WriteString(truncated) + " \"w\"))) (display \"aa\" p) (close-port p))",
            "(let ((p (open-file " + Printer.WriteString(truncated) + " \"w\"))) (display \"bb\" p) (close-port p))",
            "(call-with-input-file " + Printer.WriteString(truncated) + " get-string-all)");

        //Assert
        afterAppend.Should().Be("\"aabb\"");
        afterSecondWrite.Should().Be("\"bb\"");
    }

    [Fact]
    public void open_file_binary_mode_reads_one_character_per_byte()
    {
        //Arrange
        // The mirror of the #:encoding test above, through the mode string this time:
        // scm/backend-library.scm copies an EPS with (open-file from-name "rb"), and a
        // "b" that decoded as UTF-8 would collapse this two-byte file to one character.
        string path = WriteScratch(new byte[] { 0xC3, 0xA9 });

        //Act
        string asBinary = Eval(
            "(string-length (get-string-all (open-file " + Printer.WriteString(path) + " \"rb\")))");
        string asText = Eval(
            "(string-length (get-string-all (open-file " + Printer.WriteString(path) + " \"r\")))");

        //Assert
        asBinary.Should().Be("2");
        asText.Should().Be("1");
    }

    [Fact]
    public void close_port_flushes_an_output_file_port()
    {
        //Arrange
        // scm/backend-library.scm's output-scope opens a file, displays to it and calls
        // close-port -- and never calls flush-all-ports. A close-port that handled only
        // the input side left the text in the writer's buffer, so the file existed and
        // was EMPTY. The control is the same write with no close at all.
        string closed = ScratchPath();
        string leftOpen = ScratchPath();

        //Act
        string afterClose = Eval(
            "(let ((p (open-file " + Printer.WriteString(closed) + " \"w\")))"
            + "  (display \"written\" p) (close-port p))",
            "(call-with-input-file " + Printer.WriteString(closed) + " get-string-all)");
        string withoutClose = Eval(
            "(let ((p (open-file " + Printer.WriteString(leftOpen) + " \"w\"))) (display \"written\" p))",
            "(call-with-input-file " + Printer.WriteString(leftOpen) + " get-string-all)");

        //Assert
        afterClose.Should().Be("\"written\"");
        withoutClose.Should().Be("\"\"");
    }

    [Fact]
    public void open_file_refuses_a_read_write_mode_rather_than_answering_half_of_it()
    {
        //Arrange
        // A port here is a reader or a writer, never both. Answering the read half of
        // "r+" would look like it worked until something wrote through it.
        string path = WriteScratch(new byte[] { 0x78 });

        //Act
        string result = Eval(
            "(catch #t"
            + "  (lambda () (open-file " + Printer.WriteString(path) + " \"r+\"))"
            + "  (lambda (key . args) key))");

        //Assert
        result.Should().Be("misc-error");
    }
}
