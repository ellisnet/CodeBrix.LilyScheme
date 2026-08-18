// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The <see cref="Interpreter.NarrowModuleImports"/> OPT-IN fidelity switch. Off, a
/// <c>use-modules</c> without <c>#:select</c> imports the WHOLE module — the
/// long-standing recorded divergence, fenced from the wide side by
/// <c>SelectImportTests</c>. On, it imports the module's public interface as Guile
/// documents: only exported names arrive, through a LIVE view that keeps growing with
/// the module's exports, while <c>#:select</c> clauses and the module's own bindings
/// behave identically in both settings.
/// </summary>
public class NarrowImportTests
{
    private static string Value(bool narrow, string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter { NarrowModuleImports = narrow };
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    private const string ProviderAndConsumer =
        "(define-module (t provider))"
        + "(define hidden 40)"
        + "(define-public shown 41)"
        + "(define-module (t consumer) #:use-module (t provider))";

    [Fact]
    public void narrow_lets_an_exported_name_arrive_and_keeps_a_private_one_out()
    {
        //Arrange / Act
        string result = Value(
            true, ProviderAndConsumer + "(list shown (defined? 'hidden))");

        //Assert
        result.Should().Be("(41 #f)");
    }

    [Fact]
    public void wide_stays_the_default_and_imports_the_private_name_too()
    {
        //Arrange / Act
        string result = Value(
            false, ProviderAndConsumer + "(list shown hidden)");

        //Assert
        result.Should().Be("(41 40)");
    }

    [Fact]
    public void the_narrow_view_is_live_so_a_later_export_arrives()
    {
        //Arrange / Act
        // Guile's public interface object grows with its module: an export made AFTER
        // the use-modules clause still reaches the importer.
        string result = Value(
            true,
            ProviderAndConsumer
            + "(module-export! (resolve-module '(t provider)) '(hidden))"
            + "(list shown hidden)");

        //Assert
        result.Should().Be("(41 40)");
    }

    [Fact]
    public void narrow_still_shares_the_variable_so_set_is_seen_by_both()
    {
        //Arrange / Act
        // The interface view answers the module's OWN variable cells (module-add!
        // semantics), so a set! through the importer is seen by the provider.
        string result = Value(
            true,
            ProviderAndConsumer
            + "(set! shown 99)"
            + "(module-ref (resolve-module '(t provider)) 'shown)");

        //Assert
        result.Should().Be("99");
    }

    [Fact]
    public void narrow_select_and_rename_behave_as_before()
    {
        //Arrange / Act
        string result = Value(
            true,
            ProviderAndConsumer
            + "(use-modules ((t provider) #:select ((shown . other-name))))"
            + "other-name");

        //Assert
        result.Should().Be("41");
    }

    [Fact]
    public void narrow_srfi_1_supplies_its_exports_and_leaves_core_names_to_the_core()
    {
        //Arrange / Act
        // fold is srfi-1's own export; filter! is one of the ";; in the core" names its
        // export list deliberately comments out, reaching importers through the core
        // import in Guile and here alike.
        string result = Value(
            true,
            "(use-modules (srfi srfi-1))"
            + "(list (fold + 0 '(1 2 3)) (filter! odd? (list 1 2 3 4)))");

        //Assert
        result.Should().Be("(6 (1 3))");
    }

    [Fact]
    public void narrow_ice_9_exceptions_still_exports_its_macros()
    {
        //Arrange / Act
        // guard is a macro binding; the live view must deliver macro variables for
        // psyntax to resolve, not just value bindings.
        string result = Value(
            true,
            "(use-modules (ice-9 exceptions))"
            + "(guard (e ((error? e) 'caught)) (raise-exception (make-error)))");

        //Assert
        result.Should().Be("caught");
    }
}
