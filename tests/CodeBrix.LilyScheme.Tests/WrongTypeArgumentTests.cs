using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Wrong-typed primitive arguments surface as Guile's catchable
/// <c>wrong-type-arg</c>, never as a raw .NET exception (2026-08-18; the class
/// was first sighted 2026-08-03 as an <c>InvalidCastException</c> escaping to the
/// host, where no Scheme <c>catch</c> can see it).
/// <para>
/// Two layers carry the contract: checked accessors
/// (<c>Primitives.TypeChecks</c>) raise the POSITIONED error at the sites that
/// used to cast bare, and <c>Primitive.Invoke</c> translates any
/// <c>InvalidCastException</c> a still-bare site lets out — so the contract holds
/// for every primitive, including ones a host registers, without depending on
/// each site remembering its check.
/// </para>
/// </summary>
public class WrongTypeArgumentTests
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

    private static SchemeThrow Raise(string source, System.Action<Interpreter> prepare = null)
    {
        SchemeThrow thrown = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            prepare?.Invoke(interpreter);
            try
            {
                foreach (object form in SchemeReader.ReadAll(source, "<test>"))
                {
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
                }
            }
            catch (SchemeThrow schemeThrow)
            {
                thrown = schemeThrow;
            }
        });

        return thrown;
    }

    [Fact]
    public void a_wrong_typed_argument_is_catchable_from_scheme()
    {
        //Arrange / Act
        // The whole point of the class: Guile code catches 'wrong-type-arg, and a
        // raw host exception would sail past this catch and kill the evaluation.
        string result = Eval(
            "(catch 'wrong-type-arg"
            + " (lambda () (symbol->string 5))"
            + " (lambda (key . args) 'caught))");

        //Assert
        result.Should().Be("caught");
    }

    [Fact]
    public void the_error_names_the_procedure_and_the_position()
    {
        //Arrange / Act
        SchemeThrow thrown = Raise("(keyword->symbol 5)");

        //Assert
        // Guile's scm_wrong_type_arg names both; the interpreter's own throw shape
        // is (subr message (args) #f), message text per its existing convention.
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-type-arg"));
        Pair arguments = (Pair)thrown.Arguments;
        arguments.Car.ToString().Should().Be("keyword->symbol");
        ((Pair)arguments.Cdr).Car.ToString().Should().Contain("position 1");
    }

    [Fact]
    public void a_bare_cast_in_a_primitive_becomes_wrong_type_arg_not_a_host_exception()
    {
        //Arrange
        // The NET. A host-registered primitive with a deliberate bare cast stands in
        // for any site the accessor sweep did not reach — including a consumer's own
        // primitives, registered through the same DefinePrimitive.
        SchemeThrow thrown = Raise(
            "(test-bare-cast 5)",
            interpreter => interpreter.DefinePrimitive(
                "test-bare-cast", 1, 1, a => ((Symbol)a[0]).Name));

        //Assert
        // Reaching this catch at all is the claim: an InvalidCastException would not
        // be a SchemeThrow and the helper would have let it escape the test.
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-type-arg"));
        ((Pair)thrown.Arguments).Car.ToString().Should().Be("test-bare-cast");
    }

    [Fact]
    public void a_scheme_throw_from_a_primitive_passes_the_net_untouched()
    {
        //Arrange
        // The CONTROL on the net's selectivity: only InvalidCastException is
        // translated. A primitive that raises its own condition — or a nested
        // primitive's positioned wrong-type-arg — keeps its own key and attribution.
        SchemeThrow thrown = Raise(
            "(test-own-throw)",
            interpreter => interpreter.DefinePrimitive(
                "test-own-throw", 0, 0, a => throw new SchemeThrow(
                    Symbol.Intern("my-own-key"), Nil.Instance)));

        //Assert
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("my-own-key"));
    }

    [Fact]
    public void the_checked_sites_still_answer_for_correct_arguments()
    {
        //Arrange / Act / Assert
        // The other control: the sweep from bare casts to checked accessors must
        // change nothing about the well-typed path, across each accessor family.
        Eval("(symbol->string 'foo)").Should().Be("\"foo\"");
        Eval("(char-upcase #\\a)").Should().Be("#\\A");
        Eval("(symbol<? 'apple 'banana)").Should().Be("#t");
        Eval("(let ((s (string-copy \"abc\"))) (string-set! s 1 #\\Z) s)")
            .Should().Be("\"aZc\"");
    }

    [Theory]
    [InlineData("(integer->char 55296)", "55296")]
    [InlineData("(integer->char 1114112)", "1114112")]
    [InlineData("(integer->char -1)", "-1")]
    public void integer_to_char_refuses_a_value_that_is_not_a_unicode_scalar(
        string source, string irritant)
    {
        //Arrange / Act
        // A code point outside 0..10FFFF, or inside the surrogate block, used to go
        // straight into a character and fail LATER and ELSEWHERE -- 55296 reached the
        // PRINTER before anything complained, and what came out was a .NET
        // ArgumentOutOfRangeException that no Scheme (catch #t ...) can see.
        SchemeThrow thrown = Raise(source);

        //Assert -- the oracle's own condition, measured:
        // (out-of-range "integer->char" "Value out of range: ~S" (55296) (55296))
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("out-of-range"));
        Printer.Write(thrown.Arguments).Should().Be(
            "(\"integer->char\" \"Value out of range: ~S\" (" + irritant + ") ("
            + irritant + "))");
    }

    [Fact]
    public void an_out_of_range_character_escape_raises_that_same_condition()
    {
        //Arrange / Act
        // The CONTROL for the row above, and a fidelity point in its own right: the
        // reference reader reaches the character through integer->char itself, so
        // #\xD800 is NOT a read-error -- measured, the oracle answers
        // "In procedure integer->char: Argument 1 out of range: 55296".
        SchemeThrow thrown = Raise("(read (open-input-string \"#\\\\xD800\"))");

        //Assert
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("out-of-range"));
        Printer.Write(thrown.Arguments).Should().Be(
            "(\"integer->char\" \"Value out of range: ~S\" (55296) (55296))");

        //Assert -- and the CONTROL: a hex escape inside the range still reads
        Eval("(read (open-input-string \"#\\\\x41\"))").Should().Be("#\\A");
    }

    [Fact]
    public void a_character_escape_too_large_for_a_machine_word_is_the_same_condition()
    {
        //Arrange / Act
        // Parsing the digits into a fixed width let an OverflowException out of the
        // reader; the oracle answers with the ordinary out-of-range condition and the
        // whole value as its irritant.
        SchemeThrow thrown = Raise("(read (open-input-string \"#\\\\xffffffffff\"))");

        //Assert
        thrown.Should().NotBeNull();
        Printer.Write(thrown.Arguments).Should().Be(
            "(\"integer->char\" \"Value out of range: ~S\" (1099511627775) (1099511627775))");
    }

    [Fact]
    public void a_loop_site_reports_the_offending_position()
    {
        //Arrange / Act
        // char<? checks each pair as it walks; the bad SECOND argument must be
        // reported at position 2, which is what the loop's i-based arithmetic owes.
        SchemeThrow thrown = Raise("(char<? #\\a 5)");

        //Assert
        thrown.Should().NotBeNull();
        thrown.Key.Should().Be(Symbol.Intern("wrong-type-arg"));
        Pair arguments = (Pair)thrown.Arguments;
        arguments.Car.ToString().Should().Be("char<?");
        ((Pair)arguments.Cdr).Car.ToString().Should().Contain("position 2");
    }
}
