using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>list-set!</c>, added because LilyPort's tablature path reached
/// <c>scm/translation-functions.scm</c>'s <c>determine-frets</c> for the first time and
/// found the name unbound — and its sibling <c>list-cdr-set!</c>, plus the shared index
/// walk's error contract.
/// <para>
/// Every expected value here is <c>libguile/list.c</c>'s behaviour, DOCUMENTED or
/// MEASURED against the pinned oracle, not what this implementation happened to answer.
/// The return value is the one worth stating: <c>scm_list_set_x</c> answers the VALUE,
/// not the list and not the unspecified object, and <c>ice-9/optargs.scm</c> is written
/// against that; <c>list-cdr-set!</c> answers the value the same way (measured). The
/// walk's failures are part of the contract too: running off a PROPER list is
/// <c>out-of-range</c> naming argument 2, an improper tail is <c>wrong-type-arg</c>
/// naming argument 1, and a Scheme catch on <c>'out-of-range</c> stands on the
/// difference — <c>list-ref</c> once raised <c>wrong-type-arg</c> for a too-large index
/// and every such catch missed it.
/// </para>
/// </summary>
public class ListMutationTests
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
    public void list_set_replaces_the_element_at_the_index()
    {
        //Arrange / Act
        // scm_list_set_x walks k cdrs and sets that pair's CAR.
        string result = Eval("(define l (list 1 2 3))", "(list-set! l 1 'x)", "l");

        //Assert
        result.Should().Be("(1 x 3)");
    }

    [Fact]
    public void list_set_answers_the_value_rather_than_the_list()
    {
        //Arrange / Act
        // `return val;' in libguile/list.c. Answering the list, or *unspecified*, would
        // both look right in the mutation test above and be wrong here -- and
        // ice-9/optargs.scm reads the result.
        string result = Eval("(list-set! (list 1 2 3) 0 'first)");

        //Assert
        result.Should().Be("first");
    }

    [Fact]
    public void list_set_reaches_the_first_and_last_elements()
    {
        //Arrange / Act
        // The boundaries: index 0 sets without walking at all, and the last index walks
        // to the final pair. An off-by-one in either direction fails exactly one of these.
        string result = Eval(
            "(define l (list 'a 'b 'c))",
            "(list-set! l 0 'A)",
            "(list-set! l 2 'C)",
            "l");

        //Assert
        result.Should().Be("(A b C)");
    }

    [Fact]
    public void list_set_mutates_in_place_so_another_reference_sees_it()
    {
        //Arrange / Act
        // The control that separates a real mutation from a copy: determine-frets builds
        // its answer by mutating a list it hands out, so a list-set! that returned a fresh
        // list would pass the first test here and still produce no frets.
        string result = Eval(
            "(define l (list 1 2 3))",
            "(define alias l)",
            "(list-set! l 2 99)",
            "alias");

        //Assert
        result.Should().Be("(1 2 99)");
    }

    [Fact]
    public void list_cdr_set_splices_a_new_tail_and_answers_the_value()
    {
        //Arrange / Act & Assert
        // scm_list_cdr_set_x sets the kth pair's CDR -- the manual's lists chapter
        // teaches it as THE way to replace a list's tail in place. Measured: the
        // return is the VALUE, like list-set!'s.
        Eval("(define l (list 1 2 3))", "(list-cdr-set! l 1 (list 4 5 6))")
            .Should().Be("(4 5 6)");
        Eval("(define l (list 1 2 3))",
            "(define alias l)",
            "(list-cdr-set! l 1 (list 4 5 6))",
            "alias")
            .Should().Be("(1 2 4 5 6)");
    }

    [Fact]
    public void the_index_walk_fails_exactly_as_the_oracle_fails()
    {
        //Arrange
        // Three distinct failures, each measured against the pinned oracle. Running off
        // a PROPER list is out-of-range naming argument 2; an improper tail is
        // wrong-type-arg naming argument 1 and quoting the LIST; and a negative index
        // dies inside libguile's size_t conversion BEFORE the procedure's name enters
        // the story -- subr #f, range spelled 0 to< SIZE_MAX.

        //Act & Assert
        Eval("(catch #t (lambda () (list-ref '(a b c d) 4)) (lambda (k . a) (cons k a)))")
            .Should().Be("(out-of-range \"list-ref\" \"Argument ~A out of range: ~S\" (2 4) (4))");
        Eval("(catch #t (lambda () (list-set! (list 1 2 3) 9 'x)) (lambda (k . a) (cons k a)))")
            .Should().Be("(out-of-range \"list-set!\" \"Argument ~A out of range: ~S\" (2 9) (9))");
        Eval("(catch #t (lambda () (list-cdr-set! (list 1 2 3) 9 'x)) (lambda (k . a) (cons k a)))")
            .Should().Be("(out-of-range \"list-cdr-set!\" \"Argument ~A out of range: ~S\" (2 9) (9))");
        Eval("(catch #t (lambda () (list-ref '(a . b) 2)) (lambda (k . a) (cons k a)))")
            .Should().Be("(wrong-type-arg \"list-ref\" \"Wrong type argument in position ~A: ~S\" (1 (a . b)) ((a . b)))");
        Eval("(catch #t (lambda () (list-ref '(a b c) -1)) (lambda (k . a) (cons k a)))")
            .Should().Be("(out-of-range #f \"Value out of range ~S to< ~S: ~S\" (0 18446744073709551615 -1) (-1))");
    }
}
