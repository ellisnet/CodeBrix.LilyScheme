// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Numerics;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Pairs, lists, equality and type predicates. Measurement of LilyPond's C++ shows
/// these carry 77% of all Guile API traffic, so they are the first thing to get right.
/// </summary>
public static class CorePrimitives
{
    /// <summary>Installs the core primitives into an interpreter.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallPairs(interpreter);
        InstallAccessors(interpreter);
        InstallLists(interpreter);
        InstallSearching(interpreter);
        InstallPredicates(interpreter);
        InstallHigherOrder(interpreter);
    }

    private static void InstallPairs(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("cons", 2, 2, a => new Pair(a[0], a[1]));
        interpreter.DefinePrimitive("car", 1, 1, a => AsPair(a[0], "car").Car);
        interpreter.DefinePrimitive("cdr", 1, 1, a => AsPair(a[0], "cdr").Cdr);
        interpreter.DefinePrimitive("set-car!", 2, 2, a => { AsPair(a[0], "set-car!").Car = a[1]; return Unspecified.Instance; });
        interpreter.DefinePrimitive("set-cdr!", 2, 2, a => { AsPair(a[0], "set-cdr!").Cdr = a[1]; return Unspecified.Instance; });
        interpreter.DefinePrimitive("cons*", 1, -1, ConsStar);
        interpreter.DefinePrimitive("acons", 3, 3, a => new Pair(new Pair(a[0], a[1]), a[2]));
    }

    private static void InstallAccessors(Interpreter interpreter)
    {
        // Guile defines every caar/cadr/... combination up to four deep. Generating them
        // from the path string keeps the 28 definitions honest and identical in shape.
        string[] paths =
        {
            "aa", "ad", "da", "dd",
            "aaa", "aad", "ada", "add", "daa", "dad", "dda", "ddd",
            "aaaa", "aaad", "aada", "aadd", "adaa", "adad", "adda", "addd",
            "daaa", "daad", "dada", "dadd", "ddaa", "ddad", "ddda", "dddd",
        };

        foreach (string path in paths)
        {
            string name = "c" + path + "r";
            string capturedPath = path;
            interpreter.DefinePrimitive(name, 1, 1, a => Traverse(a[0], capturedPath, name));
        }
    }

    private static object Traverse(object value, string path, string name)
    {
        object cursor = value;

        // The path reads left to right but applies right to left: cadr is (car (cdr x)).
        for (int i = path.Length - 1; i >= 0; i--)
        {
            Pair pair = AsPair(cursor, name);
            cursor = path[i] == 'a' ? pair.Car : pair.Cdr;
        }

        return cursor;
    }

    private static void InstallLists(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("list", 0, -1, Pair.List);
        interpreter.DefinePrimitive("length", 1, 1, a => (long)CheckedLength(a[0]));
        interpreter.DefinePrimitive("append", 0, -1, Append);
        interpreter.DefinePrimitive("append!", 0, -1, AppendInPlace);
        interpreter.DefinePrimitive("reverse", 1, 1, a => Reverse(a[0]));
        // Guile's reverse! takes an optional new tail, which is consed onto the
        // reversed list rather than being reversed itself.
        interpreter.DefinePrimitive("reverse!", 1, 2, a =>
        {
            object result = a.Length > 1 ? a[1] : Nil.Instance;
            foreach (object item in Pair.ToList(a[0]))
            {
                result = new Pair(item, result);
            }

            return result;
        });
        // Guile's list-copy copies the spine and PRESERVES an improper tail:
        // (list-copy '(a . 450)) is (a . 450), and (list-copy 5) is 5. Dropping the
        // tail silently truncated every dotted pair run through list-copy — which is
        // how LilyPond's completize-grob-entry lost every atom-valued grob default.
        interpreter.DefinePrimitive("list-copy", 1, 1, a =>
        {
            List<object> items = Pair.ToList(a[0], out object tail);
            object result = tail;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                result = new Pair(items[i], result);
            }

            return result;
        });
        interpreter.DefinePrimitive("make-list", 1, 2, a =>
        {
            long count = a[0] is long n ? n : 0;
            object fill = a.Length > 1 ? a[1] : Unspecified.Instance;
            object result = Nil.Instance;
            for (long i = 0; i < count; i++)
            {
                result = new Pair(fill, result);
            }

            return result;
        });
        interpreter.DefinePrimitive("list-tail", 2, 2, a => ListTail(a[0], ToInt(a[1])));
        interpreter.DefinePrimitive("list-head", 2, 2, a => ListHead(a[0], ToInt(a[1])));

        // libguile/list.c distinguishes HOW the walk to index K fails: running off the
        // end of a PROPER list is out-of-range naming argument 2, while running into
        // an improper tail is wrong-type-arg naming argument 1 -- and a Scheme catch
        // on 'out-of-range stands on the difference. ListIndexPair carries both, with
        // shapes measured against the pinned oracle.
        interpreter.DefinePrimitive("list-ref", 2, 2, a => ListIndexPair(a[0], a[1], "list-ref").Car);

        // libguile/list.c's scm_list_set_x: set the kth car and ANSWER THE VALUE, not the
        // list. scm/translation-functions.scm's determine-frets relies on the mutation,
        // and optargs.scm on the return, so neither half is decorative.
        interpreter.DefinePrimitive("list-set!", 3, 3, a =>
        {
            ListIndexPair(a[0], a[1], "list-set!").Car = a[2];
            return a[2];
        });

        // libguile/list.c's scm_list_cdr_set_x: set the kth pair's CDR, splicing a new
        // tail into the list, and answer the value like list-set! does (measured:
        // (list-cdr-set! (list 1 2 3) 1 (list 4 5 6)) answers (4 5 6) and leaves
        // (1 2 4 5 6)). The manual's lists chapter teaches it as THE way to replace a
        // list's tail in place.
        interpreter.DefinePrimitive("list-cdr-set!", 3, 3, a =>
        {
            ListIndexPair(a[0], a[1], "list-cdr-set!").Cdr = a[2];
            return a[2];
        });
        interpreter.DefinePrimitive("last-pair", 1, 1, a => LastPair(a[0]));
        // Guile provides iota in core, not through SRFI-1: (iota count [start [step]]).
        interpreter.DefinePrimitive("iota", 1, 3, a =>
        {
            //was previously: long count = a[0] is long n ? n : 0;
            // A C# type pattern is not the numeric tower. Guile counts with ANY
            // integer-valued number, and LilyPond's bar-line.scm relies on it: both
            // make-dashed-bar-line and make-dotted-bar-line compute their count from a
            // real division through `round', which in Scheme answers a REAL (4.0, not 4),
            // so every call arrived here as a double and silently produced the EMPTY
            // list. Guile answers (4.0 2.0 0.0 -2.0 -4.0) for (iota 5.0 4.0 -2); the port
            // answered (). No dashed or dotted bar line has ever drawn.
            long count = SchemeNumber.IsInteger(a[0])
                ? (long)Numeric.SchemeNumber.ToDouble(a[0])
                : 0;
            object start = a.Length > 1 ? a[1] : 0L;
            object step = a.Length > 2 ? a[2] : 1L;
            List<object> items = new List<object>((int)Math.Max(0, count));
            object current = start;
            for (long i = 0; i < count; i++)
            {
                items.Add(current);
                current = Numeric.SchemeNumber.Add(current, step);
            }

            return Pair.ListFrom(items);
        });

        interpreter.DefinePrimitive("length+", 1, 1, a => (long)Pair.Length(a[0]));
    }

    private static void InstallSearching(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("memq", 2, 2, a => Member(a[0], a[1], Eq));
        interpreter.DefinePrimitive("memv", 2, 2, a => Member(a[0], a[1], Eqv));
        // Guile's member and assoc take an OPTIONAL equality predicate as a third
        // argument; without it they use equal?. LilyPond's default-auto-beam-check
        // relies on the three-argument assoc — it passes <= to find the first setting
        // at or above a duration — so a two-argument-only assoc makes every
        // autobeamed file die on wrong-number-of-args.
        interpreter.DefinePrimitive("member", 2, 3, a =>
            Member(a[0], a[1], a.Length > 2 ? CallerPredicate(interpreter, a[2]) : SchemeEqual));
        interpreter.DefinePrimitive("assq", 2, 2, a => Assoc(a[0], a[1], Eq));
        interpreter.DefinePrimitive("assv", 2, 2, a => Assoc(a[0], a[1], Eqv));
        interpreter.DefinePrimitive("assoc", 2, 3, a =>
            Assoc(a[0], a[1], a.Length > 2 ? CallerPredicate(interpreter, a[2]) : SchemeEqual));
        interpreter.DefinePrimitive("sloppy-assq", 2, 2, a => Assoc(a[0], a[1], Eq));
        interpreter.DefinePrimitive("sloppy-assv", 2, 2, a => Assoc(a[0], a[1], Eqv));
        interpreter.DefinePrimitive("sloppy-assoc", 2, 2, a => Assoc(a[0], a[1], SchemeEqual));

        interpreter.DefinePrimitive("assq-ref", 2, 2, a => AssocRef(a[0], a[1], Eq));
        interpreter.DefinePrimitive("assv-ref", 2, 2, a => AssocRef(a[0], a[1], Eqv));
        interpreter.DefinePrimitive("assoc-ref", 2, 2, a => AssocRef(a[0], a[1], SchemeEqual));

        interpreter.DefinePrimitive("assq-set!", 3, 3, a => AssocSet(a[0], a[1], a[2], Eq));
        interpreter.DefinePrimitive("assoc-set!", 3, 3, a => AssocSet(a[0], a[1], a[2], SchemeEqual));
        interpreter.DefinePrimitive("assq-remove!", 2, 2, a => AssocRemove(a[0], a[1], Eq));
        interpreter.DefinePrimitive("assoc-remove!", 2, 2, a => AssocRemove(a[0], a[1], SchemeEqual));

        // assv-remove! is the eqv? member of the same trio and was simply missing;
        // scm/ reaches it through the finger-glide spanner's property bookkeeping.
        interpreter.DefinePrimitive("assv-remove!", 2, 2, a => AssocRemove(a[0], a[1], Eqv));

        // Guile core. Copies pairs and vectors all the way down and shares everything
        // else, which is exactly what a markup command needs before it mutates a
        // property alist it was handed.
        interpreter.DefinePrimitive("copy-tree", 1, 1, a => CopyTree(a[0]));

        interpreter.DefinePrimitive("delq", 2, 2, a => Delete(a[0], a[1], Eq));
        interpreter.DefinePrimitive("delq!", 2, 2, a => Delete(a[0], a[1], Eq));
        interpreter.DefinePrimitive("delv", 2, 2, a => Delete(a[0], a[1], Eqv));
        interpreter.DefinePrimitive("delete", 2, 2, a => Delete(a[0], a[1], SchemeEqual));
        interpreter.DefinePrimitive("delete!", 2, 2, a => Delete(a[0], a[1], SchemeEqual));
    }

    private static void InstallPredicates(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("pair?", 1, 1, a => a[0] is Pair);
        interpreter.DefinePrimitive("null?", 1, 1, a => a[0] is Nil || a[0] is ElispNil);
        interpreter.DefinePrimitive("list?", 1, 1, a => IsProperList(a[0]));
        interpreter.DefinePrimitive("symbol?", 1, 1, a => a[0] is Symbol);
        interpreter.DefinePrimitive("string?", 1, 1, a => a[0] is MutableString);
        interpreter.DefinePrimitive("char?", 1, 1, a => a[0] is SchemeChar);
        interpreter.DefinePrimitive("boolean?", 1, 1, a => a[0] is bool);
        interpreter.DefinePrimitive("vector?", 1, 1, a => a[0] is object[]);
        interpreter.DefinePrimitive("keyword?", 1, 1, a => a[0] is Keyword);
        // An applicable smob answers procedure? in Guile too — scm_procedure_p accepts
        // anything the apply path accepts — so IApplicable counts here as well.
        interpreter.DefinePrimitive("procedure?", 1, 1, a => a[0] is Procedure || a[0] is IApplicable);
        interpreter.DefinePrimitive("bytevector?", 1, 1, a => a[0] is byte[]);
        interpreter.DefinePrimitive("eof-object?", 1, 1, a => a[0] is EofObject);
        interpreter.DefinePrimitive("eof-object", 0, 0, a => EofObject.Instance);
        interpreter.DefinePrimitive("unspecified?", 1, 1, a => a[0] is Unspecified);

        // Guile declares all three of these SCM_DEFINE (…, 0, 2, 1) — ZERO required
        // arguments, two optional, one rest — so they are N-ARY: fewer than two
        // arguments answer #t, and more than two compare ADJACENT pairs in a chain
        // (see libguile/eq.c). LilyPond depends on it: ly:beam::calc-knee decides
        // whether a beam is kneed with (apply eqv? <list of stem directions>), one
        // argument per stem, so a two-argument-only eqv? makes EVERY beam fail.
        interpreter.DefinePrimitive("eq?", 0, -1, a => Chained(a, Eq));
        interpreter.DefinePrimitive("eqv?", 0, -1, a => Chained(a, Eqv));
        interpreter.DefinePrimitive("equal?", 0, -1, a => Chained(a, SchemeEqual));
        interpreter.DefinePrimitive("not", 1, 1, a => !Evaluator.IsTrue(a[0]));

        // Guile's boot-9.scm defines this as (define (->bool x) (not (not x))): it
        // narrows Scheme truth, where everything but #f is true, to an actual boolean.
        interpreter.DefinePrimitive("->bool", 1, 1, a => Evaluator.IsTrue(a[0]));

        interpreter.DefinePrimitive("identity", 1, 1, a => a[0]);
        interpreter.DefinePrimitive("const", 1, 1, a =>
        {
            object captured = a[0];
            return new Primitive("const-result", 0, -1, _ => captured);
        });

        // boot-9.scm defines this immediately after const:
        //   (define (and=> value procedure) (and value (procedure value)))
        // "When VALUE is #f, return #f. Otherwise, return (PROC VALUE)." It is a plain
        // procedure, so both arguments are evaluated before the call and the short
        // circuit is only over whether PROCEDURE runs. LilyPond calls it exactly once,
        // in define-markup-commands.scm's \image (which is what \epsfile expands to),
        // to normalize a background-color that may legitimately be #f.
        interpreter.DefinePrimitive("and=>", 2, 2, a =>
            Evaluator.IsTrue(a[0]) ? interpreter.Evaluator.Apply(a[1], new[] { a[0] }) : a[0]);

        interpreter.DefinePrimitive(
            "self-evaluating?",
            1,
            1,
            a => !(a[0] is Symbol) && !(a[0] is Pair) && !(a[0] is Nil));
    }

    private static void InstallHigherOrder(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("map", 2, -1, a => MapLists(interpreter, a, true));
        interpreter.DefinePrimitive("for-each", 2, -1, a => MapLists(interpreter, a, false));
        interpreter.DefinePrimitive("map-in-order", 2, -1, a => MapLists(interpreter, a, true));

        interpreter.DefinePrimitive("and-map", 2, 2, a =>
        {
            object result = true;
            foreach (object item in Pair.ToList(a[1]))
            {
                result = interpreter.Evaluator.Apply(a[0], new[] { item });
                if (!Evaluator.IsTrue(result))
                {
                    return false;
                }
            }

            return result;
        });

        interpreter.DefinePrimitive("or-map", 2, 2, a =>
        {
            foreach (object item in Pair.ToList(a[1]))
            {
                object result = interpreter.Evaluator.Apply(a[0], new[] { item });
                if (Evaluator.IsTrue(result))
                {
                    return result;
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("filter", 2, 2, a =>
        {
            List<object> kept = new List<object>();
            foreach (object item in Pair.ToList(a[1]))
            {
                if (Evaluator.IsTrue(interpreter.Evaluator.Apply(a[0], new[] { item })))
                {
                    kept.Add(item);
                }
            }

            return Pair.ListFrom(kept);
        });

        // filter! is a CORE binding in Guile (libguile/list.c, scm_filter_x), not an
        // srfi-1 one: the vendored srfi/srfi-1.scm has the line
        //
        //     ;; filter!    <= in the core
        //
        // where its definition would otherwise be, and then re-exports the name. So a
        // core that does not define it leaves `filter!' unbound for everything that
        // imports (srfi srfi-1) — which is the whole of the (lily) module.
        //
        // "Linear update" is a LICENCE to reuse the argument's pairs, never an
        // obligation, so returning a fresh list is a conforming implementation and the
        // only safe one over a shared structure.
        interpreter.DefinePrimitive("filter!", 2, 2, a =>
        {
            List<object> kept = new List<object>();
            foreach (object item in Pair.ToList(a[1]))
            {
                if (Evaluator.IsTrue(interpreter.Evaluator.Apply(a[0], new[] { item })))
                {
                    kept.Add(item);
                }
            }

            return Pair.ListFrom(kept);
        });

        interpreter.DefinePrimitive("remove", 2, 2, a =>
        {
            List<object> kept = new List<object>();
            foreach (object item in Pair.ToList(a[1]))
            {
                if (!Evaluator.IsTrue(interpreter.Evaluator.Apply(a[0], new[] { item })))
                {
                    kept.Add(item);
                }
            }

            return Pair.ListFrom(kept);
        });

        // Guile's sort family, all over the same merge sort. That is not an arbitrary
        // choice of algorithm: List<T>.Sort is an introsort that VALIDATES its comparer
        // and throws "IComparer.Compare() returns inconsistent results" when the Scheme
        // predicate is not a strict weak ordering. LilyPond passes predicates that are
        // not -- alist<? compares only the keys it recognizes -- so a merge sort, which
        // asks only "does b come before a", is the correct engine here. It is also stable,
        // which is what stable-sort promises.
        DefineSort(interpreter, "sort");
        DefineSort(interpreter, "sort!");
        DefineSort(interpreter, "sort-list");
        DefineSort(interpreter, "sort-list!");
        DefineSort(interpreter, "stable-sort");
        DefineSort(interpreter, "stable-sort!");

        interpreter.DefinePrimitive("merge", 3, 3, a =>
            Pair.ListFrom(Merge(interpreter, Pair.ToList(a[0]), Pair.ToList(a[1]), a[2])));
        interpreter.DefinePrimitive("merge!", 3, 3, a =>
            Pair.ListFrom(Merge(interpreter, Pair.ToList(a[0]), Pair.ToList(a[1]), a[2])));
    }

    private static void DefineSort(Interpreter interpreter, string name)
        => interpreter.DefinePrimitive(name, 2, 2, a =>
        {
            // Guile accepts the sequence first or the predicate first; LilyPond's Scheme
            // uses both spellings.
            object sequence = a[0];
            object comparator = a[1];
            if (sequence is Procedure && !(comparator is Procedure))
            {
                object swap = sequence;
                sequence = comparator;
                comparator = swap;
            }

            List<object> items = sequence is object[] vector
                ? new List<object>(vector)
                : Pair.ToList(sequence);

            List<object> sorted = MergeSort(interpreter, items, comparator);
            return sequence is object[] ? (object)sorted.ToArray() : Pair.ListFrom(sorted);
        });

    private static List<object> MergeSort(Interpreter interpreter, List<object> items, object comparator)
    {
        if (items.Count < 2)
        {
            return items;
        }

        int middle = items.Count / 2;
        List<object> left = MergeSort(interpreter, items.GetRange(0, middle), comparator);
        List<object> right = MergeSort(interpreter, items.GetRange(middle, items.Count - middle), comparator);
        return Merge(interpreter, left, right, comparator);
    }

    private static List<object> Merge(
        Interpreter interpreter,
        List<object> left,
        List<object> right,
        object comparator)
    {
        List<object> merged = new List<object>(left.Count + right.Count);
        int i = 0;
        int j = 0;
        while (i < left.Count && j < right.Count)
        {
            // Take from the left unless the right element strictly precedes it: asking
            // the question in this direction is what makes the sort stable.
            bool rightFirst = Evaluator.IsTrue(
                interpreter.Evaluator.Apply(comparator, new[] { right[j], left[i] }));
            merged.Add(rightFirst ? right[j++] : left[i++]);
        }

        while (i < left.Count)
        {
            merged.Add(left[i++]);
        }

        while (j < right.Count)
        {
            merged.Add(right[j++]);
        }

        return merged;
    }

    private static object MapLists(Interpreter interpreter, object[] arguments, bool collect)
    {
        object procedure = arguments[0];
        int listCount = arguments.Length - 1;
        object[] cursors = new object[listCount];
        for (int i = 0; i < listCount; i++)
        {
            cursors[i] = arguments[i + 1];
        }

        List<object> results = collect ? new List<object>() : null;
        while (true)
        {
            object[] slice = new object[listCount];
            for (int i = 0; i < listCount; i++)
            {
                if (!(cursors[i] is Pair pair))
                {
                    return collect ? Pair.ListFrom(results) : (object)Unspecified.Instance;
                }

                slice[i] = pair.Car;
                cursors[i] = pair.Cdr;
            }

            object value = interpreter.Evaluator.Apply(procedure, slice);
            results?.Add(value);
        }
    }

    private static object ConsStar(object[] arguments)
    {
        if (arguments.Length == 1)
        {
            return arguments[0];
        }

        object result = arguments[arguments.Length - 1];
        for (int i = arguments.Length - 2; i >= 0; i--)
        {
            result = new Pair(arguments[i], result);
        }

        return result;
    }

    /// <summary>
    /// <c>append</c> — the copying concatenation. Every argument BEFORE the last must
    /// be a proper list; libguile validates them left to right and raises
    /// <c>wrong-type-arg</c> naming the argument's position, the words "empty list",
    /// and the offending TAIL, so an improper or non-list argument is LOUD. The last
    /// argument is attached as it stands and never walked — <c>(append '(1 2) x)</c>
    /// puts <c>x</c> itself in cdr position, whatever it is.
    /// </summary>
    /// <param name="arguments">The lists to concatenate.</param>
    /// <returns>The concatenation.</returns>
    private static object Append(object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return Nil.Instance;
        }

        // Validate left to right BEFORE building, so a call with two bad arguments
        // reports the leftmost one, as the oracle does.
        List<object>[] lists = new List<object>[arguments.Length - 1];
        for (int i = 0; i < arguments.Length - 1; i++)
        {
            lists[i] = ProperListItems(arguments[i], "append", i + 1);
        }

        object result = arguments[arguments.Length - 1];
        for (int i = lists.Length - 1; i >= 0; i--)
        {
            List<object> items = lists[i];
            for (int j = items.Count - 1; j >= 0; j--)
            {
                result = new Pair(items[j], result);
            }
        }

        return result;
    }

    // Reads one of append's non-last arguments the way libguile walks it: the final
    // cdr must be the empty list, and anything else -- an improper tail, or a
    // non-list argument (which is its own tail) -- raises the measured
    // "expecting empty list" wrong-type-arg naming the argument's position.
    private static List<object> ProperListItems(object list, string procedureName, int position)
    {
        List<object> items = new List<object>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            items.Add(pair.Car);
            cursor = pair.Cdr;
        }

        if (!(cursor is Nil || cursor is ElispNil))
        {
            throw ExpectingError(procedureName, position, "empty list", cursor);
        }

        return items;
    }

    /// <summary>
    /// <c>append!</c> — the same result as <c>append</c>, built by RE-LINKING the
    /// arguments instead of copying them.
    /// <para>
    /// The difference is identity, not speed, and callers depend on it: a list that
    /// was appended to keeps pointing at the extended list, because the pairs it is
    /// made of are the extended list's own. LilyPond's <c>add-to-tag-group</c> is
    /// written that way — it re-registers <c>(append! tag-group tags)</c> and lets the
    /// caller's own tag-group variable track the group — so aliasing this to
    /// <c>append</c> left every such variable holding the list as it was BEFORE the
    /// change, and the next lookup answered "tag group ... not found" quoting the
    /// stale contents.
    /// </para>
    /// <para>
    /// As in Guile the LAST argument is attached as it stands: it is never walked and
    /// need not be a list. Every EARLIER argument must be the empty list (skipped) or
    /// a proper list: a non-pair raises the measured "expecting pair" wrong-type-arg
    /// and an improper tail the "expecting empty list" one, each naming the
    /// argument's position. The re-linking is progressive, as libguile's is, so an
    /// argument validated as a pair is attached BEFORE its tail is walked.
    /// </para>
    /// </summary>
    /// <param name="arguments">The lists to concatenate.</param>
    /// <returns>The first non-empty argument, re-linked onto the rest.</returns>
    private static object AppendInPlace(object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return Nil.Instance;
        }

        object head = Nil.Instance;
        Pair tail = null;

        for (int i = 0; i < arguments.Length - 1; i++)
        {
            object argument = arguments[i];
            if (argument is Nil || argument is ElispNil)
            {
                // An empty argument contributes nothing.
                continue;
            }

            if (!(argument is Pair first))
            {
                throw ExpectingError("append!", i + 1, "pair", argument);
            }

            if (tail == null)
            {
                head = first;
            }
            else
            {
                tail.Cdr = first;
            }

            Pair last = first;
            while (last.Cdr is Pair next)
            {
                last = next;
            }

            if (!(last.Cdr is Nil || last.Cdr is ElispNil))
            {
                throw ExpectingError("append!", i + 1, "empty list", last.Cdr);
            }

            tail = last;
        }

        object final = arguments[arguments.Length - 1];
        if (tail == null)
        {
            return final;
        }

        tail.Cdr = final;
        return head;
    }

    private static object Reverse(object list)
    {
        object result = Nil.Instance;
        object cursor = list;
        while (cursor is Pair pair)
        {
            result = new Pair(pair.Car, result);
            cursor = pair.Cdr;
        }

        return result;
    }

    private static object ListTail(object list, int count)
    {
        object cursor = list;
        for (int i = 0; i < count; i++)
        {
            cursor = AsPair(cursor, "list-tail").Cdr;
        }

        return cursor;
    }

    private static object ListHead(object list, int count)
    {
        List<object> items = new List<object>();
        object cursor = list;
        for (int i = 0; i < count; i++)
        {
            Pair pair = AsPair(cursor, "list-head");
            items.Add(pair.Car);
            cursor = pair.Cdr;
        }

        return Pair.ListFrom(items);
    }

    private static object LastPair(object list)
    {
        object cursor = list;
        while (cursor is Pair pair && pair.Cdr is Pair)
        {
            cursor = pair.Cdr;
        }

        return cursor;
    }

    // libguile/list.c's index walk, shared by list-ref, list-set! and list-cdr-set!:
    // answer the pair at index K, or fail the way the oracle fails -- running off a
    // PROPER list is out-of-range naming argument 2, hitting an improper tail is
    // wrong-type-arg naming argument 1 and quoting the LIST, and a negative index
    // never reaches the walk at all (see NegativeIndexError).
    private static Pair ListIndexPair(object list, object index, string procedureName)
    {
        BigInteger k = SchemeNumber.ToBigInteger(index);
        if (k < 0)
        {
            throw NegativeIndexError(index);
        }

        object cursor = list;
        while (cursor is Pair pair)
        {
            if (k == 0)
            {
                return pair;
            }

            k -= 1;
            cursor = pair.Cdr;
        }

        if (cursor is Nil || cursor is ElispNil)
        {
            throw ArgumentOutOfRange(procedureName, 2, index);
        }

        throw WrongTypePositioned(procedureName, 1, list);
    }

    // The list family's error shapes, measured against the pinned oracle (LilyPond
    // 2.27.2, Guile 3.0) rather than recalled: the message keeps libguile's ~A/~S
    // placeholders, the args list fills them, and the DATA slot carries the
    // offending value in a one-element list. Older throws elsewhere in this
    // interpreter bake the position into the message text with #f data; these four
    // match the oracle exactly because reproducing its shapes is the point.
    private static SchemeThrow ExpectingError(string procedureName, int position, string expecting, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument in position ~A (expecting ~A): ~S"),
                Pair.List((long)position, new MutableString(expecting), value),
                Pair.List(value)));

    private static SchemeThrow WrongTypePositioned(string procedureName, int position, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument in position ~A: ~S"),
                Pair.List((long)position, value),
                Pair.List(value)));

    private static SchemeThrow ArgumentOutOfRange(string procedureName, int position, object value)
        => new SchemeThrow(
            Symbol.Intern("out-of-range"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Argument ~A out of range: ~S"),
                Pair.List((long)position, value),
                Pair.List(value)));

    // A negative index dies inside libguile's scm_to_size_t CONVERSION, before the
    // procedure's name enters the story: subr #f, and the range spelled out as
    // 0 to< SIZE_MAX. Measured verbatim from the oracle --
    // (out-of-range #f "Value out of range ~S to< ~S: ~S" (0 18446744073709551615 -1) (-1)).
    private static SchemeThrow NegativeIndexError(object value)
        => new SchemeThrow(
            Symbol.Intern("out-of-range"),
            Pair.List(
                false,
                new MutableString("Value out of range ~S to< ~S: ~S"),
                Pair.List(0L, (BigInteger)ulong.MaxValue, value),
                Pair.List(value)));

    /// <summary>
    /// Applies an equality predicate the way Guile's n-ary <c>eq?</c>, <c>eqv?</c> and
    /// <c>equal?</c> do: fewer than two arguments answer <see langword="true"/>, and
    /// longer argument lists must hold pairwise between ADJACENT elements.
    /// </summary>
    private static bool Chained(object[] arguments, Func<object, object, bool> predicate)
    {
        if (arguments.Length < 2)
        {
            return true;
        }

        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (!predicate(arguments[i], arguments[i + 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Wraps a caller-supplied Scheme predicate so <c>member</c> and <c>assoc</c> can
    /// use it in place of <c>equal?</c>. The argument order is the caller's own:
    /// <c>(pred key element)</c>, which is what lets <c>(assoc x alist &lt;=)</c> mean
    /// "the first entry whose key is at least x".
    /// </summary>
    private static Func<object, object, bool> CallerPredicate(
        Interpreter interpreter, object predicate)
        => (x, y) => Evaluator.IsTrue(interpreter.Evaluator.Apply(predicate, new[] { x, y }));

    private static object Member(object item, object list, Func<object, object, bool> predicate)
    {
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (predicate(item, pair.Car))
            {
                return cursor;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    private static object Assoc(object key, object alist, Func<object, object, bool> predicate)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && predicate(key, entry.Car))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    private static object AssocRef(object alist, object key, Func<object, object, bool> predicate)
    {
        object entry = Assoc(key, alist, predicate);
        return entry is Pair pair ? pair.Cdr : (object)false;
    }

    private static object AssocSet(object alist, object key, object value, Func<object, object, bool> predicate)
    {
        object entry = Assoc(key, alist, predicate);
        if (entry is Pair pair)
        {
            pair.Cdr = value;
            return alist;
        }

        return new Pair(new Pair(key, value), alist);
    }

    // scm_copy_tree: pairs and vectors are rebuilt, everything else is shared. Guile
    // guards against cycles with a hare-and-tortoise walk; the port keeps the simple
    // recursion, because every caller in scm/ hands it a freshly built alist.
    private static object CopyTree(object value)
    {
        if (value is Pair pair)
        {
            return new Pair(CopyTree(pair.Car), CopyTree(pair.Cdr));
        }

        if (value is object[] vector)
        {
            object[] copy = new object[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                copy[i] = CopyTree(vector[i]);
            }

            return copy;
        }

        return value;
    }

    // THE FIRST ENTRY ONLY, which is what the trio's own documentation says: "Delete the
    // first entry in alist associated with key, and return the resulting alist"
    // (libguile/alist.c:344). The implementation is scm_sloppy_assq to find ONE handle
    // and scm_delq1_x to unlink THAT PAIR — never a filter over the whole list. An alist
    // may legitimately carry the same key twice, and then the two readings differ:
    // removing all of them uncovers nothing, where removing one uncovers the entry the
    // shadowed binding was hiding. That is exactly what an alist used as a scoped chain
    // relies on.
    private static object AssocRemove(object alist, object key, Func<object, object, bool> predicate)
    {
        List<object> kept = new List<object>();
        bool removed = false;
        foreach (object entry in Pair.ToList(alist))
        {
            if (!removed && entry is Pair pair && predicate(key, pair.Car))
            {
                removed = true;
                continue;
            }

            kept.Add(entry);
        }

        return Pair.ListFrom(kept);
    }

    private static object Delete(object item, object list, Func<object, object, bool> predicate)
    {
        List<object> kept = new List<object>();
        foreach (object element in Pair.ToList(list))
        {
            if (!predicate(item, element))
            {
                kept.Add(element);
            }
        }

        return Pair.ListFrom(kept);
    }

    private static bool IsProperList(object value)
    {
        object slow = value;
        object fast = value;
        while (true)
        {
            if (fast is Nil)
            {
                return true;
            }

            if (!(fast is Pair fastPair))
            {
                return false;
            }

            fast = fastPair.Cdr;
            if (fast is Nil)
            {
                return true;
            }

            if (!(fast is Pair secondPair))
            {
                return false;
            }

            fast = secondPair.Cdr;
            slow = ((Pair)slow).Cdr;

            // Floyd's cycle detection: a circular list is not a proper list.
            if (ReferenceEquals(slow, fast))
            {
                return false;
            }
        }
    }

    private static int CheckedLength(object list)
    {
        int count = 0;
        object cursor = list;
        while (cursor is Pair pair)
        {
            count++;
            cursor = pair.Cdr;
        }

        if (!(cursor is Nil))
        {
            throw new SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(new MutableString("length"), new MutableString("Not a proper list"), Nil.Instance, false));
        }

        return count;
    }

    /// <summary>Guile's <c>eq?</c>: reference identity, with immediates compared by value.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns><see langword="true"/> when the values are identical.</returns>
    public static bool Eq(object x, object y) => ReferenceComparer.Instance.Equals(x, y);

    /// <summary>Guile's <c>eqv?</c>: <c>eq?</c> plus numeric and character equality.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns><see langword="true"/> when the values are equivalent.</returns>
    public static bool Eqv(object x, object y)
    {
        if (Eq(x, y))
        {
            return true;
        }

        if (SchemeNumber.IsNumber(x) && SchemeNumber.IsNumber(y))
        {
            // Exactness is part of eqv? equivalence: (eqv? 1 1.0) is false.
            return SchemeNumber.IsExact(x) == SchemeNumber.IsExact(y) && SchemeNumber.NumericEquals(x, y);
        }

        return false;
    }

    /// <summary>Guile's <c>equal?</c>: structural equality over pairs, vectors and strings.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns><see langword="true"/> when the structures match.</returns>
    public static bool SchemeEqual(object x, object y)
    {
        if (Eqv(x, y))
        {
            return true;
        }

        if (x is Pair px && y is Pair py)
        {
            return SchemeEqual(px.Car, py.Car) && SchemeEqual(px.Cdr, py.Cdr);
        }

        if (x is MutableString sx && y is MutableString sy)
        {
            return string.Equals(sx.ToString(), sy.ToString(), StringComparison.Ordinal);
        }

        if (x is object[] vx && y is object[] vy)
        {
            if (vx.Length != vy.Length)
            {
                return false;
            }

            for (int i = 0; i < vx.Length; i++)
            {
                if (!SchemeEqual(vx[i], vy[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // scm_equal_p ends by dispatching to the smob's own equality handler, which is
        // why (equal? (ly:make-moment 0 1) (ly:make-moment 0 1)) is #t in Guile even
        // though the two are distinct objects. Eight LilyPond types declare one --
        // Duration, Moment, Listener, Input, Pitch, Tuplet_description, Prob and Spring
        // -- and until this dispatch existed every one of them compared as #f here.
        // moment<=? is (or (equal? a b) (ly:moment<? a b)), so a Moment that could not
        // answer equal? made "is this moment at or before that one" answer NO at the one
        // moment they are the SAME, which is the case the guard exists for.
        if (x is ISchemeEqual ex && y is ISchemeEqual)
        {
            return ex.SchemeEquals(y);
        }

        return false;
    }

    private static Pair AsPair(object value, string procedureName)
    {
        if (value is Pair pair)
        {
            return pair;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument in position 1: ~S"),
                Pair.List(value),
                false));
    }

    private static int ToInt(object value) => (int)SchemeNumber.ToBigInteger(value);
}
