using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// <c>use-modules</c> with <c>#:select</c>, which both RENAMES and RESTRICTS.
/// <para>
/// The renaming half landed with LS-FIX3; the restriction is EPG16's opening step. Every
/// expected value here is Guile's documented <c>resolve-interface</c> behaviour in the
/// pinned source: a <c>#:select</c> clause builds an interface module holding only the
/// selected bindings, and THAT is what goes on the importer's use list.
/// </para>
/// <para>
/// Each fact is fenced from BOTH sides — the selected name must arrive AND the unselected
/// name must not — because an implementation that imports the whole module passes every
/// test that only asks whether the selected name works, which is exactly the state this
/// change ends.
/// </para>
/// </summary>
public class SelectImportTests
{
    /// <summary>
    /// A module with two bindings and one import of its own, so a test can tell the
    /// difference between "selected", "not selected" and "reachable only through what the
    /// supplier itself imports".
    /// </summary>
    private const string Supplier = @"
(define-module (test supplier))
(define alpha 'alpha-value)
(define beta 'beta-value)
";

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
    public void a_selected_binding_is_imported_under_its_own_name()
    {
        //Arrange / Act
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select (alpha)))",
            "alpha");

        //Assert
        result.Should().Be("alpha-value");
    }

    [Fact]
    public void an_unselected_binding_is_not_imported()
    {
        //Arrange / Act
        // THE RESTRICTION. Before EPG16's opening step this answered #t: the whole module
        // went on the use list and #:select only ever ADDED renames on top of it.
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select (alpha)))",
            "(defined? 'beta)");

        //Assert
        result.Should().Be("#f");
    }

    [Fact]
    public void a_renaming_select_binds_the_local_name()
    {
        //Arrange / Act
        // scm/lily.scm's own clause: ((ice-9 format) #:select ((format . ice9-format))).
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select ((alpha . renamed-alpha))))",
            "renamed-alpha");

        //Assert
        result.Should().Be("alpha-value");
    }

    [Fact]
    public void a_renaming_select_does_not_also_bind_the_original_name()
    {
        //Arrange / Act
        // The other half of the rename, and the reason lily.scm writes the clause at all:
        // it wants ice-9's format reachable ONLY as ice9-format, leaving plain `format'
        // resolving to the core's simple one.
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select ((alpha . renamed-alpha))))",
            "(defined? 'alpha)");

        //Assert
        result.Should().Be("#f");
    }

    [Fact]
    public void a_selected_binding_shares_the_suppliers_variable()
    {
        //Arrange / Act
        // module-add! semantics, not module-define!: one cell, so a set! on the supplier's
        // side is seen through the selected name. A copy of the VALUE would pass the
        // import tests above and fail here.
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select (alpha)))",
            "(set-current-module (resolve-module '(test supplier)))",
            "(set! alpha 'changed)",
            "(set-current-module (resolve-module '(test consumer)))",
            "alpha");

        //Assert
        result.Should().Be("changed");
    }

    [Fact]
    public void a_clause_with_no_select_still_imports_the_whole_module()
    {
        //Arrange / Act
        // The control. Restricting a clause that never asked to be restricted would be its
        // own bug, and it is the shape almost every use-modules line in LilyPond has.
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules (test supplier))",
            "(list alpha beta)");

        //Assert
        result.Should().Be("(alpha-value beta-value)");
    }

    [Fact]
    public void two_clauses_selecting_different_names_from_one_module_both_arrive()
    {
        //Arrange / Act
        // The interface is built FRESH per clause. Caching one interface per module — the
        // obvious optimisation — would make the second clause reuse the first's narrower
        // view and lose beta.
        string result = Eval(
            Supplier,
            "(define-module (test consumer))",
            "(use-modules ((test supplier) #:select (alpha)))",
            "(use-modules ((test supplier) #:select (beta)))",
            "(list alpha beta)");

        //Assert
        result.Should().Be("(alpha-value beta-value)");
    }
}
