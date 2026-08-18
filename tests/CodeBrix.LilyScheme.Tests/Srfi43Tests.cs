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
/// <c>(srfi srfi-43)</c>, vendored VERBATIM. The load-bearing distinction fenced
/// here: SRFI-43's iteration procedures pass the INDEX as the first argument —
/// <c>(f i x)</c> — where R7RS's <c>vector-map</c> passes elements only. A port
/// that supplied the R7RS shape under this module's name would pass every
/// index-ignoring test and silently break real SRFI-43 callers.
/// </summary>
public class Srfi43Tests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            // The consumer is its OWN module, as any real importer is — evaluating
            // inside the root would find the root's local vector-copy first, which
            // is correct resolution and the wrong test.
            foreach (object form in SchemeReader.ReadAll(
                "(define-module (test srfi43-consumer)) (use-modules (srfi srfi-43)) " + source,
                "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void vector_map_passes_the_index_first()
    {
        //Arrange / Act
        // (f i x) with f = + makes the index VISIBLE in the answer: #(10 21 32),
        // where the R7RS elements-only shape would answer #(10 20 30).
        string result = Value("(vector-map + #(10 20 30))");

        //Assert
        result.Should().Be("#(10 21 32)");
    }

    [Fact]
    public void vector_for_each_walks_in_order_with_indices()
    {
        //Arrange / Act
        string result = Value(
            "(let ((acc '()))"
            + "  (vector-for-each (lambda (i x) (set! acc (cons (cons i x) acc))) #(a b))"
            + "  (reverse acc))");

        //Assert
        result.Should().Be("((0 . a) (1 . b))");
    }

    [Fact]
    public void vector_fold_threads_the_seed_through()
    {
        //Arrange / Act
        // SRFI-43 vector-fold: (kons i seed x). Sum with the seed first proves both
        // the argument order and the walk direction.
        string result = Value("(vector-fold (lambda (i seed x) (+ seed x)) 0 #(1 2 3 4))");

        //Assert
        result.Should().Be("10");
    }

    [Fact]
    public void vector_equal_compares_elementwise()
    {
        //Arrange / Act
        string result = Value(
            "(list (vector= equal? #(1 2) #(1 2)) (vector= equal? #(1 2) #(1 3)))");

        //Assert
        result.Should().Be("(#t #f)");
    }

    [Fact]
    public void srfi_43_vector_copy_takes_a_range()
    {
        //Arrange / Act
        // Under first-import-wins resolution (the recorded divergence on
        // SchemeModule.Lookup) this reaches the CORE vector-copy, whose
        // [start [end]] arity is libguile/vectors.c's own — either resolution
        // must answer the same slice.
        string result = Value("(vector-copy #(a b c d e) 1 4)");

        //Assert
        result.Should().Be("#(b c d)");
    }
}
