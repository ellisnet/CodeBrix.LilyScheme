// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, or (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Imported macros must resolve inside ANONYMOUS modules.
/// <para>
/// psyntax round-trips module identity by NAME: hygiene wraps carry
/// <c>(hygiene name…)</c> and resolve it back through the registry. Guile squares
/// that with anonymous modules by having <c>module-name</c> INVENT a name on first
/// ask and register the module under it (boot-9). Before LilyScheme did the same,
/// a macro imported into an anonymous module read as an ordinary variable — which
/// is how every <c>define-music-function</c> in LilyPond's init layer broke, and
/// why the LilyPort parser named its scopes as a workaround (LS-FIX, 2026-08-05).
/// </para>
/// </summary>
public class AnonymousModuleMacroTests
{
    private static object EvalIn(SchemeModule module, Interpreter interpreter, string source)
    {
        object result = null;
        foreach (object form in SchemeReader.ReadAll(source, "<anon-test>"))
        {
            result = interpreter.TreeIlEvaluator.ExpandAndEval(form, module);
        }

        return result;
    }

    [Fact]
    public void an_imported_macro_expands_inside_an_anonymous_module()
    {
        //Arrange
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);

            // A module exporting a MACRO, then an ANONYMOUS module using it — the
            // two-line reproduction from the LilyPort parser sessions, distilled.
            EvalIn(
                interpreter.CurrentModule,
                interpreter,
                "(define-module (macro-exporter))"
                + "(define-syntax-rule (twice x) (+ x x))"
                + "(export twice)");

            SchemeModule anonymous = new SchemeModule(null);
            anonymous.AddUse(interpreter.Modules.RootModule);
            anonymous.AddUse(interpreter.Modules.Resolve(
                Pair.List(Symbol.Intern("macro-exporter"))));

            //Act
            result = EvalIn(anonymous, interpreter, "(twice 21)");
        });

        //Assert
        result.Should().Be(42L);
    }

    [Fact]
    public void module_name_names_and_registers_an_anonymous_module_on_first_ask()
    {
        //Arrange
        object named = null;
        object resolvedBack = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeModule anonymous = new SchemeModule(null);

            //Act
            named = anonymous.EnsureName(interpreter.Modules);
            resolvedBack = interpreter.Modules.Resolve(named);
        });

        //Assert
        (named is Pair).Should().BeTrue();
        (resolvedBack is SchemeModule).Should().BeTrue();
    }

    [Fact]
    public void the_invented_name_is_stable_across_asks()
    {
        //Arrange
        object first = null;
        object second = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeModule anonymous = new SchemeModule(null);

            //Act
            first = anonymous.EnsureName(interpreter.Modules);
            second = anonymous.EnsureName(interpreter.Modules);
        });

        //Assert
        ReferenceEquals(first, second).Should().BeTrue();
    }
}
