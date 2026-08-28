// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>eval</c>, <c>eval-string</c>, <c>Interpreter.Eval</c> and <c>Interpreter.EvalString</c>
/// MACRO-EXPAND once psyntax is loaded, as Guile's do (2026-08-28).
/// <para>
/// The symptom this closes: evaluating <c>(markup …)</c> from LilyPond's <c>(lily)</c>
/// module through <c>EvalString</c> failed as <c>Wrong type to apply:
/// #&lt;syntax-transformer markup&gt;</c> — a macro reached as a procedure on that one
/// path, while the same form loaded from a module file worked. MEASURED on the pinned
/// 2.27.2: an <c>eval-string</c> that defines a <c>syntax-rules</c> macro and uses it in
/// the next form answers the expansion, and a later <c>eval</c> of the macro's name in
/// the same module answers it too.
/// </para>
/// </summary>
public class EvalExpansionTests
{
    private static string Run(System.Func<Interpreter, object> action)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            result = Printer.Write(action(interpreter));
        });

        return result;
    }

    private static object Expanded(Interpreter interpreter, string source)
    {
        object result = Unspecified.Instance;
        foreach (object form in SchemeReader.ReadAll(source, "<test>"))
        {
            result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
        }

        return result;
    }

    [Fact]
    public void eval_string_defines_a_macro_and_uses_it_in_the_next_form()
    {
        //Arrange / Act
        string result = Run(i => Expanded(
            i, "(eval-string \"(define-syntax my-m (syntax-rules () ((_ x) (list 'm x)))) (my-m 3)\")"));

        //Assert
        result.Should().Be("(m 3)");
    }

    [Fact]
    public void eval_sees_a_macro_that_eval_string_defined()
    {
        //Arrange / Act
        string result = Run(i => Expanded(
            i,
            "(eval-string \"(define-syntax my-m (syntax-rules () ((_ x) (list 'm x))))\")"
            + "(list (eval '(my-m 4) (current-module)) (eval-string \"(my-m 5)\"))"));

        //Assert
        result.Should().Be("((m 4) (m 5))");
    }

    [Fact]
    public void the_host_eval_string_expands_too()
    {
        //Arrange / Act
        // The C# entry LilyPort reaches for: a macro defined in the current module, then
        // used through Interpreter.EvalString rather than through the Scheme primitive.
        string result = Run(i =>
        {
            Expanded(i, "(define-syntax twice (syntax-rules () ((_ x) (list x x))))");
            return i.EvalString("(twice 'a)", "<host>");
        });

        //Assert
        result.Should().Be("(a a)");
    }

    [Fact]
    public void the_host_eval_expands_a_macro_from_an_explicitly_current_module()
    {
        //Arrange / Act
        // The (lily)-module shape: the macro lives in a module that is made current, and
        // Interpreter.Eval is handed the bare form.
        string result = Run(i =>
        {
            Expanded(
                i,
                // Exported, because the default import is Guile's narrow one: an
                // unexported macro would be (correctly) invisible to (t user).
                "(define-module (t macros) #:export (wrap))"
                + "(define-syntax wrap (syntax-rules () ((_ x) (cons 'wrapped x))))"
                + "(define-module (t user) #:use-module (t macros))");
            return i.Eval(SchemeReader.ReadAll("(wrap 7)", "<host>")[0]);
        });

        //Assert
        result.Should().Be("(wrapped . 7)");
    }

    [Fact]
    public void a_plain_form_still_evaluates_through_the_same_entries()
    {
        //Arrange / Act
        string result = Run(i => i.EvalString("(define seven 7) (* seven 6)", "<host>"));

        //Assert
        result.Should().Be("42");
    }
}
