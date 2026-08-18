using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The <c>assq-remove!</c> / <c>assv-remove!</c> / <c>assoc-remove!</c> trio, whose
/// contract is that exactly ONE entry goes.
/// <para>
/// <c>libguile/alist.c:344</c> — "Delete the FIRST entry in alist associated with key,
/// and return the resulting alist" — implemented as <c>scm_sloppy_assq</c> to find one
/// handle and <c>scm_delq1_x</c> to unlink that pair. Every case below is paired with a
/// control on a list whose key is UNIQUE, where removing one and removing all agree: a
/// fence that only looked at the unique case passes with either reading.
/// </para>
/// </summary>
public class AlistRemoveTests
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

    [Fact]
    public void assq_remove_deletes_only_the_first_entry_for_a_repeated_key()
    {
        //Arrange
        // Two entries under `a'. Guile removes the handle scm_sloppy_assq found, which is
        // the FIRST, so the SECOND is uncovered rather than erased.
        const string Duplicated = "(assq-remove! (list (cons 'a 1) (cons 'a 2) (cons 'b 3)) 'a)";

        //Act
        string result = Eval(Duplicated);

        //Assert
        result.Should().Be("((a . 2) (b . 3))");
    }

    [Fact]
    public void assq_remove_on_a_unique_key_is_the_control_that_both_readings_pass()
    {
        //Arrange
        // THE CONTROL. Remove-first and remove-all agree here, which is exactly why the
        // repeated-key case above is the one that fences the behaviour.
        const string Unique = "(assq-remove! (list (cons 'a 1) (cons 'b 3)) 'a)";

        //Act
        string result = Eval(Unique);

        //Assert
        result.Should().Be("((b . 3))");
    }

    [Fact]
    public void assq_remove_leaves_a_list_without_the_key_alone()
    {
        //Arrange
        const string Absent = "(assq-remove! (list (cons 'a 1) (cons 'b 2)) 'c)";

        //Act
        string result = Eval(Absent);

        //Assert
        result.Should().Be("((a . 1) (b . 2))");
    }

    [Fact]
    public void assv_remove_deletes_only_the_first_entry_for_a_repeated_key()
    {
        //Arrange
        // The eqv? member of the trio, on numeric keys, which is where eqv? and eq? part
        // company for anything but a fixnum.
        const string Duplicated = "(assv-remove! (list (cons 1 'x) (cons 1 'y) (cons 2 'z)) 1)";

        //Act
        string result = Eval(Duplicated);

        //Assert
        result.Should().Be("((1 . y) (2 . z))");
    }

    [Fact]
    public void assoc_remove_deletes_only_the_first_entry_for_a_repeated_key()
    {
        //Arrange
        // The equal? member, on STRING keys — two distinct string objects that are equal?
        // but not eq?, so only this member of the trio matches them at all.
        const string Duplicated =
            "(assoc-remove! (list (cons (string #\\k) 1) (cons (string #\\k) 2) (cons 'b 3))"
            + " (string #\\k))";

        //Act
        string result = Eval(Duplicated);

        //Assert
        result.Should().Be("((\"k\" . 2) (b . 3))");
    }

    [Fact]
    public void assq_remove_uncovers_a_shadowed_binding_where_removing_every_match_would_not()
    {
        //Arrange
        // The behaviour the markup tag machinery depends on: an alist used as a scoped
        // chain shadows an outer entry with an inner one, and removing the inner is what
        // puts the outer back in play. Removing both leaves the lookup with nothing.
        const string Scoped =
            "(assq-ref (assq-remove! (list (cons 'foo 'inner) (cons 'foo 'outer)) 'foo) 'foo)";

        //Act
        string result = Eval(Scoped);

        //Assert
        result.Should().Be("outer");
    }
}
