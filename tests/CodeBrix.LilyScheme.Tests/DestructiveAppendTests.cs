using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>append!</c> re-links its arguments instead of copying them, and
/// <c>eval-string</c> answers the last form's value.
/// <para>
/// Both were found by LilyPort: <c>add-to-tag-group</c> depends on <c>append!</c>'s
/// IDENTITY, not merely on its result, and <c>\key $to #(eval-string key)</c> needs a
/// name that did not exist at all. Every expectation below is R7RS/SRFI-1 and Guile's
/// documented behaviour for these two names, not a value recorded from this
/// implementation.
/// </para>
/// </summary>
public class DestructiveAppendTests
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
    public void append_bang_rewrites_the_first_list_so_the_caller_s_variable_follows_it()
    {
        //Arrange
        // THE FENCE, and the CONTROL is the second half: the copying `append' must NOT
        // move `a', so a test that passed for both would be measuring nothing.
        const string Destructive = "(define a (list 1 2)) (define b (list 3)) "
                                   + "(append! a b) a";
        const string Copying = "(define a (list 1 2)) (define b (list 3)) "
                               + "(append a b) a";

        //Act
        string afterDestructive = Eval(Destructive);
        string afterCopying = Eval(Copying);

        //Assert
        afterDestructive.Should().Be("(1 2 3)");
        afterCopying.Should().Be("(1 2)");
    }

    [Fact]
    public void append_bang_answers_the_first_non_empty_argument_itself()
    {
        //Arrange / Act
        // eq? on the result and the input is the whole difference between the two
        // procedures; `append' must answer #f here for the same reason.
        string destructive = Eval("(define a (list 1 2)) (eq? a (append! a (list 3)))");
        string copying = Eval("(define a (list 1 2)) (eq? a (append a (list 3)))");

        //Assert
        destructive.Should().Be("#t");
        copying.Should().Be("#f");
    }

    [Fact]
    public void append_bang_skips_empty_arguments_and_attaches_the_last_one_as_it_stands()
    {
        //Arrange / Act
        // Guile's append! never walks its LAST argument, so an improper tail is legal
        // there and only there; an empty argument in front contributes nothing.
        string skipped = Eval("(append! '() (list 1) '() (list 2))");
        string improperTail = Eval("(append! (list 1 2) 3)");
        string nothing = Eval("(append!)");

        //Assert
        skipped.Should().Be("(1 2)");
        improperTail.Should().Be("(1 2 . 3)");
        nothing.Should().Be("()");
    }

    [Fact]
    public void eval_string_answers_the_last_form_s_value()
    {
        //Arrange / Act
        // The CONTROL is the two-form case: a reader that stopped at the first form
        // would answer 3 for both.
        string single = Eval("(eval-string \"(+ 1 2)\")");
        string several = Eval("(eval-string \"(+ 1 2) (* 4 5)\")");
        string none = Eval("(eval-string \"\")");

        //Assert
        single.Should().Be("3");
        several.Should().Be("20");
        none.Should().Be("#<unspecified>");
    }

    [Fact]
    public void eval_string_sees_the_definitions_the_current_module_holds()
    {
        //Arrange / Act
        // The point of the module excursion: free identifiers in the text resolve
        // against the module that is current, both when it is expanded and when it runs.
        string result = Eval("(define seven 7)", "(eval-string \"(* seven 6)\")");

        //Assert
        result.Should().Be("42");
    }
}
