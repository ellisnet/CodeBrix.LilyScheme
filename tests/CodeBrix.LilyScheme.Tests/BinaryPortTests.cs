using System.IO;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Writing BYTES from Scheme — <c>set-port-encoding!</c>, and the directory and
/// character primitives that go with it.
/// <para>
/// Scheme code produces binary output by setting an 8-bit codec on a port and writing
/// one character per octet. <c>set-port-encoding!</c> accepted that call and changed
/// nothing, so every octet above 0x7F left through a UTF-8 writer as TWO bytes and the
/// file on disk was the content's mojibake rather than the content. Nothing failed and
/// nothing warned; the only symptom was downstream, where whatever read the file could
/// not make sense of it.
/// </para>
/// <para>
/// EXPECTED VALUES ARE THE SPEC'S (rules 33/35a): ISO-8859-1 maps code points 0x00-0xFF
/// to the identical octet, by definition, so a run of characters written under it must
/// come back as exactly those bytes. Every case is paired with the UTF-8 control that
/// must come out DIFFERENTLY.
/// </para>
/// </summary>
public class BinaryPortTests
{
    private static void Eval(params string[] sources)
    {
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (string source in sources)
            {
                foreach (object form in SchemeReader.ReadAll(source, "<test>"))
                {
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
                }
            }
        });
    }

    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void an_eight_bit_encoding_writes_one_byte_per_character()
    {
        //Arrange
        // 0xC3 and 0xA9 are the two octets UTF-8 would spell "é" with, chosen so that a
        // writer that ignored the encoding produces a FOUR-byte file and a writer that
        // honoured it produces a two-byte one.
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-binary-" + Path.GetRandomFileName());
        string control = path + "-utf8";
        try
        {
            //Act
            Eval(
                $"(let ((p (open-output-file {Printer.WriteString(path)})))"
                + "  (set-port-encoding! p \"ISO-8859-1\")"
                + "  (write-char (integer->char 195) p)"
                + "  (write-char (integer->char 169) p)"
                + "  (close p))",
                $"(let ((p (open-output-file {Printer.WriteString(control)})))"
                + "  (write-char (integer->char 195) p)"
                + "  (write-char (integer->char 169) p)"
                + "  (close p))");

            //Assert
            File.ReadAllBytes(path).Should().Equal(new byte[] { 0xC3, 0xA9 });

            // THE CONTROL, which must come out DIFFERENTLY: the same two characters
            // through the default codec are FOUR bytes. A fence that checked only the
            // first case passes with the encoding still ignored, as long as the
            // characters happen to be ASCII.
            File.ReadAllBytes(control).Length.Should().Be(4);
        }
        finally
        {
            File.Delete(path);
            File.Delete(control);
        }
    }

    [Fact]
    public void read_char_walks_a_port_one_character_at_a_time()
    {
        //Arrange
        // read-char and peek-char were simply absent, and a decoder that walks a string
        // port cannot be written without them.

        //Act
        string read = Value(
            "(let ((p (open-input-string \"ab\")))"
            + "  (list (read-char p) (peek-char p) (read-char p) (eof-object? (read-char p))))");

        //Assert
        // peek does NOT consume, which is the whole difference between the two, so the
        // second read still answers #\\b.
        read.Should().Be("(#\\a #\\b #\\b #t)");
    }

    [Fact]
    public void a_directory_stream_yields_its_entries_and_then_end_of_file()
    {
        //Arrange
        // Guile yields "." and ".." as ordinary entries, and every caller written against
        // Guile filters them itself — a stream that omitted them would silently change
        // what such a loop counts.
        string directory = Path.Combine(
            Path.GetTempPath(), "lilyscheme-dir-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "one.txt"), "x");
            File.WriteAllText(Path.Combine(directory, "two.txt"), "y");

            //Act
            string counted = Value(
                $"(let ((d (opendir {Printer.WriteString(directory)})) (n 0) (dots 0))"
                + "  (do ((f (readdir d) (readdir d))) ((eof-object? f))"
                + "    (if (or (equal? f \".\") (equal? f \"..\"))"
                + "        (set! dots (+ dots 1))"
                + "        (set! n (+ n 1))))"
                + "  (closedir d)"
                + "  (list n dots))");

            //Assert
            counted.Should().Be("(2 2)");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void delete_file_and_rmdir_remove_what_they_name()
    {
        //Arrange
        string directory = Path.Combine(
            Path.GetTempPath(), "lilyscheme-rm-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "gone.txt");
        File.WriteAllText(file, "x");

        //Act
        Eval(
            $"(delete-file {Printer.WriteString(file)})",
            $"(rmdir {Printer.WriteString(directory)})");

        //Assert
        File.Exists(file).Should().BeFalse();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void rmdir_refuses_a_directory_that_is_not_empty()
    {
        //Arrange
        // rmdir(2) is NON-recursive, and that is the point: a caller that has not emptied
        // the directory must get the error rather than losing its contents.
        string directory = Path.Combine(
            Path.GetTempPath(), "lilyscheme-full-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "stays.txt"), "x");
        try
        {
            //Act
            string caught = Value(
                $"(catch 'system-error (lambda () (rmdir {Printer.WriteString(directory)}) 'no-error)"
                + "  (lambda (key . args) 'threw))");

            //Assert
            caught.Should().Be("threw");
            Directory.Exists(directory).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
