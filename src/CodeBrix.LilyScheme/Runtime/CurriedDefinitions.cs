// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// Rewrites Guile's curried definitions into plain ones, before the macro expander
/// sees them.
/// <para>
/// <c>(ice-9 curried-definitions)</c> lets <c>(define ((f a) b) body)</c> mean
/// <c>(define (f a) (lambda (b) body))</c>, nested to any depth. LilyPond uses it 45
/// times across 10 files in <c>scm/</c>.
/// </para>
/// <para>
/// This is done in C# rather than as a Scheme macro for a specific reason. Guile
/// implements it by shadowing <c>define</c>, which cannot work here: psyntax resolves
/// top-level identifiers at USE time, so a shadowing <c>define</c> whose expansion
/// mentions <c>define</c> finds itself and recurses until the process aborts. That was
/// tried and produced a SIGABRT. A source-to-source rewrite before expansion has no
/// such ambiguity.
/// </para>
/// </summary>
public static class CurriedDefinitions
{
    /// <summary>
    /// Rewrites every curried definition in a form, including definitions nested inside
    /// bodies.
    /// </summary>
    /// <param name="form">The form to rewrite.</param>
    /// <returns>An equivalent form with no curried definitions.</returns>
    public static object Expand(object form)
    {
        if (!(form is Pair pair))
        {
            return form;
        }

        // Do not descend into quoted data: (quote ((f a) b)) is a list, not a definition.
        if (pair.Car is Symbol head && ReferenceEquals(head, Symbol.Quote))
        {
            return form;
        }

        if (pair.Car is Symbol keyword && IsDefiningForm(keyword))
        {
            object rewritten = RewriteDefinition(pair);
            if (!ReferenceEquals(rewritten, pair))
            {
                // The rewrite introduced a lambda; keep going in case the body itself
                // contains curried definitions.
                return ExpandChildren(rewritten);
            }
        }

        return ExpandChildren(form);
    }

    private static bool IsDefiningForm(Symbol keyword)
        => ReferenceEquals(keyword, Symbol.Define)
           || string.Equals(keyword.Name, "define-public", System.StringComparison.Ordinal)
           || string.Equals(keyword.Name, "define*", System.StringComparison.Ordinal)
           || string.Equals(keyword.Name, "define*-public", System.StringComparison.Ordinal);

    private static object RewriteDefinition(Pair definition)
    {
        if (!(definition.Cdr is Pair rest) || !(rest.Car is Pair target))
        {
            return definition;
        }

        // A curried head is one whose CAR is itself a pair: ((f a) b).
        if (!(target.Car is Pair))
        {
            return definition;
        }

        object innerTarget = target.Car;
        object outerParameters = target.Cdr;
        object body = rest.Cdr;

        // A DOCSTRING TRAVELS OUTWARD, and Guile's own implementation says so: the
        // curried clause in ice-9/curried-definitions.scm carries the string up with
        // the comment "Keep moving docstring to outermost lambda". So
        //
        //     (define ((f a) b) "doc" body)
        //
        // is (define (f a) "doc" (lambda (b) body)), NOT
        // (define (f a) (lambda (b) "doc" body)).
        //
        // Leaving it on the inner lambda is invisible in every ordinary use — the
        // procedures behave identically — and wrong for exactly one reader:
        // procedure-documentation of the name that was defined. LilyPond's Internals
        // Reference documents twenty curried procedures, and each of them asks that
        // question about the OUTER procedure.
        object docstring = null;
        if (body is Pair firstForm
            && (firstForm.Car is MutableString || firstForm.Car is string)
            && firstForm.Cdr is Pair)
        {
            docstring = firstForm.Car;
            body = firstForm.Cdr;
        }

        // (define ((f a) b) body...) => (define (f a) (lambda (b) body...))
        object lambdaForm = new Pair(Symbol.Lambda, new Pair(outerParameters, body));
        object outerBody = new Pair(lambdaForm, Nil.Instance);
        if (docstring != null)
        {
            outerBody = new Pair(docstring, outerBody);
        }

        Pair rebuilt = new Pair(
            definition.Car,
            new Pair(innerTarget, outerBody));

        // The forms this rewrite INVENTS — the inner lambda above all — take their
        // location from the definition they came out of, which is what Guile does for
        // macro-introduced code (datum->syntax carries the macro use's source). It is
        // also what the oracle shows: a curried definition's inner procedure reports the
        // position of the whole `(define-public ((f a) b) ...)' form, column and all, as
        // in "#<procedure at lily/chord-name.scm:118:0 (pitch lowercase?)>". Forms
        // carried over from the original body keep their own locations, because only
        // pairs with none are stamped.
        SourceProperties.StampMissing(rebuilt, SourceProperties.Located(definition));

        // Nested currying: ((( f a) b) c) needs another pass.
        return RewriteDefinition(rebuilt);
    }

    private static object ExpandChildren(object form)
    {
        if (!(form is Pair pair))
        {
            return form;
        }

        List<object> items = new List<object>();
        List<Pair> originals = new List<Pair>();
        bool changed = false;
        object cursor = form;
        while (cursor is Pair current)
        {
            object expanded = Expand(current.Car);
            changed |= !ReferenceEquals(expanded, current.Car);
            items.Add(expanded);
            originals.Add(current);
            cursor = current.Cdr;
        }

        object tail = Expand(cursor);
        changed |= !ReferenceEquals(tail, cursor);

        // ⚠ RETURN THE ORIGINAL WHEN NOTHING CHANGED. This pass runs over EVERY form of
        // every file before psyntax sees it, and rebuilding a pair drops its source
        // properties — which live in a table keyed by object identity. Rebuilding
        // unconditionally therefore erased the location of every form in the whole layer,
        // silently: psyntax found no source to propagate, so every Tree-IL node carried
        // #f, every procedure printed as anonymous, and no error message could name a
        // file. Curried definitions are a handful of forms; identity is the answer for
        // all the rest.
        if (!changed)
        {
            return form;
        }

        object result = tail;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Pair rebuilt = new Pair(items[i], result);
            SourceProperties.CopyTo(originals[i], rebuilt);
            result = rebuilt;
        }

        return result;
    }
}
