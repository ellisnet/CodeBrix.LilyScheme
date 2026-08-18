using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>list-set!</c>, added because LilyPort's tablature path reached
/// <c>scm/translation-functions.scm</c>'s <c>determine-frets</c> for the first time and
/// found the name unbound.
/// <para>
/// Every expected value here is <c>libguile/list.c</c>'s DOCUMENTED behaviour in the
/// pinned source, not what this implementation happened to answer. The return value is
/// the one worth stating: <c>scm_list_set_x</c> answers the VALUE, not the list and not
/// the unspecified object, and <c>ice-9/optargs.scm</c> is written against that.
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
}
