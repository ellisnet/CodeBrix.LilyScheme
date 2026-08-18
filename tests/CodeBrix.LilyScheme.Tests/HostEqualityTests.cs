using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>equal?</c> dispatches to a host object's own equality handler, the way
/// <c>scm_equal_p</c> ends by dispatching to a smob's <c>equal_p</c>.
/// <para>
/// Found through LilyPond's <c>moment&lt;=?</c>, which is
/// <c>(or (equal? a b) (ly:moment&lt;? a b))</c>: with no dispatch, "is this moment at
/// or before that one" answered NO at the one moment they are the SAME. The default is
/// still identity, because a Guile smob that declares no handler falls back to
/// <c>eq?</c> — value equality is opt-in on both sides.
/// </para>
/// </summary>
public class HostEqualityTests
{
    /// <summary>A host value that compares by value, like a smob with an equal_p.</summary>
    private sealed class Boxed : ISchemeEqual
    {
        public Boxed(int value) => Value = value;

        public int Value { get; }

        public bool SchemeEquals(object other) => other is Boxed b && b.Value == Value;
    }

    /// <summary>A host value that declares no handler, so identity is all it has.</summary>
    private sealed class Opaque
    {
        public Opaque(int value) => Value = value;

        public int Value { get; }
    }

    private static string Eval(Interpreter interpreter, string source)
    {
        string result = null;
        foreach (object form in SchemeReader.ReadAll(source, "<test>"))
        {
            result = Printer.Write(
                interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
        }

        return result;
    }

    [Fact]
    public void equal_compares_a_declaring_host_object_by_value()
    {
        //Arrange
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            interpreter.CurrentModule.Define(Symbol.Intern("a"), new Boxed(7));
            interpreter.CurrentModule.Define(Symbol.Intern("b"), new Boxed(7));

            //Act
            result = Eval(interpreter, "(list (equal? a b) (eq? a b) (eqv? a b))");
        });

        //Assert
        // equal? sees the handler; eq? and eqv? do not, because two distinct smobs are
        // not identical however equal their contents.
        result.Should().Be("(#t #f #f)");
    }

    [Fact]
    public void equal_leaves_a_non_declaring_host_object_on_identity()
    {
        //Arrange
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            Opaque first = new Opaque(7);
            interpreter.CurrentModule.Define(Symbol.Intern("a"), first);
            interpreter.CurrentModule.Define(Symbol.Intern("b"), new Opaque(7));
            interpreter.CurrentModule.Define(Symbol.Intern("c"), first);

            //Act
            result = Eval(interpreter, "(list (equal? a b) (equal? a c))");
        });

        //Assert
        // A type with no handler answers #f for two distinct objects and #t only for the
        // same one — exactly what a smob without an equal_p does.
        result.Should().Be("(#f #t)");
    }

    [Fact]
    public void equal_stays_false_when_only_one_side_declares_a_handler()
    {
        //Arrange
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            interpreter.CurrentModule.Define(Symbol.Intern("a"), new Boxed(7));
            interpreter.CurrentModule.Define(Symbol.Intern("b"), new Opaque(7));

            //Act
            result = Eval(interpreter, "(list (equal? a b) (equal? b a))");
        });

        //Assert
        // equal? is symmetric; asking one side about a value that cannot answer back is
        // how an asymmetric comparison creeps in, so the hook needs BOTH sides.
        result.Should().Be("(#f #f)");
    }
}
