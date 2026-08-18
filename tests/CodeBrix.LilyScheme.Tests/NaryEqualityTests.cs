using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Guile's <c>eq?</c>, <c>eqv?</c> and <c>equal?</c> are declared
/// <c>SCM_DEFINE (…, 0, 2, 1)</c> — zero required arguments, two optional and a rest —
/// so they are N-ARY, and <c>member</c> and <c>assoc</c> take an optional equality
/// predicate. Both gaps were found by EPG10: LilyPond's <c>ly:beam::calc-knee</c>
/// decides whether a beam is kneed with <c>(apply eqv? &lt;stem directions&gt;)</c>,
/// one argument per stem, and <c>default-auto-beam-check</c> finds a beat setting with
/// <c>(assoc type sorted-alist &lt;=)</c>.
/// </summary>
public class NaryEqualityTests
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
    public void eqv_with_fewer_than_two_arguments_is_true()
    {
        //Arrange
        // Guile returns #t when either optional argument is unbound. The zero-argument
        // case is the one a beam with no stems reaches through (apply eqv? '()).
        //Act
        string result = Eval("(list (eqv?) (eqv? 1) (eq?) (eq? 'a) (equal?) (equal? \"x\"))");

        //Assert
        result.Should().Be("(#t #t #t #t #t #t)");
    }

    [Fact]
    public void eqv_compares_adjacent_pairs_across_many_arguments()
    {
        //Arrange
        // THE regression this exists for: ly:beam::calc-knee applies eqv? to one stem
        // direction per stem, so a four-note beam calls it with four arguments.
        //Act
        string result = Eval(
            "(list (apply eqv? '(1 1 1 1))"
            + " (apply eqv? '(1 1 -1 1))"
            + " (apply eqv? '(-1 -1))"
            + " (eqv? 2 2 2))");

        //Assert
        result.Should().Be("(#t #f #t #t)");
    }

    [Fact]
    public void nary_equality_keeps_each_predicates_own_strictness()
    {
        //Arrange
        // eq? and eqv? still differ on numbers-by-value versus identity, and equal?
        // still recurses into structure; going n-ary changes the arity, not the test.
        //Act
        string result = Eval(
            "(list (eqv? 1.0 1.0 1.0)"
            + " (eqv? 1 1.0)"
            + " (equal? (list 1 2) (list 1 2) (list 1 2))"
            + " (eq? (list 1) (list 1)))");

        //Assert
        result.Should().Be("(#t #f #t #f)");
    }

    [Fact]
    public void assoc_takes_an_optional_equality_predicate()
    {
        //Arrange
        // default-auto-beam-check's larger-setting is (assoc type sorted-alist <=):
        // the first entry whose key is at or above the duration asked about. The
        // predicate is called (predicate key entry-key), in the caller's order.
        //Act
        string result = Eval(
            "(define settings '((1/8 . eighth) (1/4 . quarter) (1/2 . half)))",
            "(list (assoc 1/4 settings)"
            + " (assoc 1/4 settings <=)"
            + " (assoc 3/8 settings <=)"
            + " (assoc 3/4 settings <=))");

        //Assert
        result.Should().Be("((1/4 . quarter) (1/4 . quarter) (1/2 . half) #f)");
    }

    [Fact]
    public void member_takes_an_optional_equality_predicate()
    {
        //Arrange
        // The same optional-predicate rule Guile gives assoc, and the tail it returns
        // is the list from the match on, not #t.
        //Act
        string result = Eval(
            "(list (member 3 '(1 2 3 4))"
            + " (member 3 '(1 2 3 4) <=)"
            + " (member 9 '(1 2 3 4) <=))");

        //Assert
        result.Should().Be("((3 4) (3 4) #f)");
    }
}
