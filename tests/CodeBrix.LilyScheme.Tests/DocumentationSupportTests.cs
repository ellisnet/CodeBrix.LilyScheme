// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The Guile surface LilyPond's documentation generator stands on, each case written
/// against Guile's own documented or measured behaviour rather than against this
/// implementation's output.
/// </summary>
public sealed class DocumentationSupportTests
{
    private static object Eval(string source)
    {
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            SchemeBootstrap.LoadExpanded(interpreter, source, "<test>");
            result = interpreter.EvalString("__result", "<test>");
        });

        return result;
    }

    private static string Display(object value) => Printer.Display(value);

    [Fact]
    public void slot_init_value_is_evaluated_not_quoted()
    {
        //Arrange -- GOOPS evaluates a slot option's value (oop/goops.scm's `class'
        //macro re-emits (kw arg . rest) with arg in place). The empty list is the case
        //that matters: quoted, the slot would hold the two-element list (quote ()).
        string source = @"
(use-modules (oop goops))
(define-class <node> ()
  (children #:init-value '() #:accessor node-children))
(define __result (list (node-children (make <node>))
                       (pair? (node-children (make <node>)))))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(() #f)");
    }

    [Fact]
    public void slot_init_value_still_survives_an_explicit_keyword()
    {
        //Arrange -- the control: evaluating the defaults must not break the keyword
        //initialiser that overrides them.
        string source = @"
(use-modules (oop goops))
(define-class <node> ()
  (name #:init-value ""default"" #:accessor node-name #:init-keyword #:name))
(define __result (list (node-name (make <node>))
                       (node-name (make <node> #:name ""given""))))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(default given)");
    }

    [Fact]
    public void char_ci_less_folds_upward_like_guile()
    {
        //Arrange -- libguile/chars.c compares scm_c_upcase of both operands, so a letter
        //sorts BELOW the punctuation between the two ASCII cases. Folding down would
        //answer the opposite for both of the first two, and identically for the third,
        //which is why the letter/backslash pair is the one that discriminates.
        string source = @"(define __result (list (char-ci<? #\a #\\)
                                                 (char-ci<? #\\ #\a)
                                                 (char-ci<? #\a #\B)))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(#t #f #t)");
    }

    [Fact]
    public void procedure_documentation_answers_the_docstring_and_not_the_name()
    {
        //Arrange -- a string as the first of SEVERAL body forms is a docstring; a lone
        //string body is the return value and leaves the procedure undocumented. The
        //named-but-undocumented case is the control: it used to answer the name.
        string source = @"
(define (documented x) ""the docstring"" x)
(define (undocumented x) x)
(define (returns-a-string x) ""not a docstring"")
(define __result (list (procedure-documentation documented)
                       (procedure-documentation undocumented)
                       (procedure-documentation returns-a-string)))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(the docstring #f #f)");
    }

    [Fact]
    public void a_curried_definition_carries_its_docstring_on_the_outermost_lambda()
    {
        //Arrange -- ice-9/curried-definitions.scm hoists it, with the comment "Keep
        //moving docstring to outermost lambda". Verified against the pinned oracle,
        //which answers ("the docstring" #f) for exactly this pair.
        string source = @"
(define ((curried a) b) ""the docstring"" (+ a b))
(define __result (list (procedure-documentation curried)
                       (procedure-documentation (curried 1))
                       ((curried 1) 2)))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(the docstring #f 3)");
    }

    [Fact]
    public void load_from_path_goes_through_the_current_primitive_load_path()
    {
        //Arrange -- boot-9's load-from-path is a call to primitive-load-path, resolved
        //when it runs. A host that replaces primitive-load-path must be honoured, which
        //is how CodeBrix.LilyPort serves LilyPond's layer from embedded resources.
        string source = @"
(define seen #f)
(define (primitive-load-path name) (set! seen name) 'loaded)
(define __result (list (load-from-path ""lily/documentation-generate.scm"") seen))";

        //Act
        object result = Eval(source);

        //Assert
        Display(result).Should().Be("(loaded lily/documentation-generate.scm)");
    }
}
