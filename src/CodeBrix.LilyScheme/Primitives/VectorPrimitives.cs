// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// A Scheme hash table. Guile distinguishes <c>hashq</c> (eq?), <c>hashv</c> (eqv?) and
/// <c>hash</c> (equal?) families; the comparer is fixed when the table is created.
/// </summary>
public sealed class SchemeHashTable
{
    private readonly Dictionary<object, Pair> _entries;

    /// <summary>Initializes a hash table.</summary>
    /// <param name="comparer">The equivalence used for keys.</param>
    public SchemeHashTable(IEqualityComparer<object> comparer)
    {
        _entries = new Dictionary<object, Pair>(comparer ?? ReferenceComparer.Instance);
    }

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the handle pairs, each a <c>(key . value)</c> cons.</summary>
    public IEnumerable<Pair> Handles => _entries.Values;

    /// <summary>Finds the handle for a key.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The <c>(key . value)</c> pair, or <see langword="null"/>.</returns>
    public Pair GetHandle(object key)
        => _entries.TryGetValue(key, out Pair handle) ? handle : null;

    /// <summary>Finds or creates the handle for a key.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="initialValue">The value stored when the key is absent.</param>
    /// <returns>The handle pair.</returns>
    public Pair CreateHandle(object key, object initialValue)
    {
        if (_entries.TryGetValue(key, out Pair existing))
        {
            return existing;
        }

        Pair handle = new Pair(key, initialValue);
        _entries[key] = handle;
        return handle;
    }

    /// <summary>Stores a value under a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set(object key, object value) => CreateHandle(key, value).Cdr = value;

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    public void Remove(object key) => _entries.Remove(key);

    /// <summary>Removes every entry.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the entry count.</returns>
    public override string ToString()
        => "#<hash-table " + Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ">";
}

/// <summary>Vectors, bytevectors and hash tables.</summary>
public static class VectorPrimitives
{
    /// <summary>Installs the vector and hash-table primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallVectors(interpreter);
        InstallHashTables(interpreter);
    }

    private static void InstallVectors(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("vector", 0, -1, a => (object[])a.Clone());
        interpreter.DefinePrimitive("make-vector", 1, 2, a =>
        {
            int length = Index(a[0]);
            object fill = a.Length > 1 ? a[1] : Unspecified.Instance;
            object[] vector = new object[length];
            for (int i = 0; i < length; i++)
            {
                vector[i] = fill;
            }

            return vector;
        });

        interpreter.DefinePrimitive("vector-ref", 2, 2, a => AsVector(a[0], "vector-ref")[Index(a[1])]);
        interpreter.DefinePrimitive("vector-set!", 3, 3, a =>
        {
            AsVector(a[0], "vector-set!")[Index(a[1])] = a[2];
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("vector-length", 1, 1, a => (long)AsVector(a[0], "vector-length").Length);
        interpreter.DefinePrimitive("vector->list", 1, 1, a => Pair.ListFrom(AsVector(a[0], "vector->list")));
        interpreter.DefinePrimitive("list->vector", 1, 1, a => Pair.ToList(a[0]).ToArray());
        // 1 + 2 optional, as libguile/vectors.c's scm_vector_copy_partial: the core
        // procedure takes [start [end]], and (srfi srfi-43)'s vector-copy DELEGATES
        // its one- and two-argument clauses straight here.
        interpreter.DefinePrimitive("vector-copy", 1, 3, a =>
        {
            object[] source = AsVector(a[0], "vector-copy");
            int start = a.Length > 1 ? (int)SchemeNumber.ToBigInteger(a[1]) : 0;
            int end = a.Length > 2 ? (int)SchemeNumber.ToBigInteger(a[2]) : source.Length;
            object[] copy = new object[end - start];
            Array.Copy(source, start, copy, 0, end - start);
            return copy;
        });

        interpreter.DefinePrimitive("vector-fill!", 2, 2, a =>
        {
            object[] vector = AsVector(a[0], "vector-fill!");
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = a[1];
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("bytevector-length", 1, 1, a => (long)((byte[])a[0]).Length);
        interpreter.DefinePrimitive("bytevector-u8-ref", 2, 2, a => (long)((byte[])a[0])[Index(a[1])]);
        interpreter.DefinePrimitive("bytevector-u8-set!", 3, 3, a =>
        {
            ((byte[])a[0])[Index(a[1])] = (byte)Index(a[2]);
            return Unspecified.Instance;
        });

        // (rnrs bytevectors) exports this name and then leaves it to load-extension to
        // supply, so the vendored module is not evidence that anything defines it.
        // The elements are EXACT integers, which is what makes arithmetic over the
        // result stay exact.
        interpreter.DefinePrimitive("bytevector->u8-list", 1, 1, a =>
        {
            byte[] bytes = (byte[])a[0];
            object result = Nil.Instance;
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                result = new Pair((long)bytes[i], result);
            }

            return result;
        });
    }

    private static void InstallHashTables(Interpreter interpreter)
    {
        // `make-hash-table' answers an EQUAL?-keyed table, which is what the hash-*
        // family that reads it uses. Guile's tables carry no comparer at all — the
        // ACCESSOR decides, so hash-ref/hash-set!/hash-get-handle compare with equal?
        // and hashq-* with eq?. This one carries a comparer (see the note below), so the
        // default has to be the family that matters: boot-9's own object-property shim
        // routes SYMBOL keys to hashq and everything else to hash, and for interned
        // symbols eq? and equal? agree, so equal? is right for both.
        //
        // It was reference equality, and every structured key silently missed.
        // `\addChordShape' stores under (cons key-symbol tuning) and `chord-shape' looks
        // up a FRESH cons of the same two things, so ly/predefined-guitar-*-fretboards.ly
        // answered '() for every shape it had just stored -- 322 "wrong type for
        // argument 4" errors in one sweep. file-cache.scm keys by FILE NAME and
        // musicQuotes by quote name, both strings, both never hitting.
        interpreter.DefinePrimitive("make-hash-table", 0, 1, a => new SchemeHashTable(new EqualComparer()));
        interpreter.DefinePrimitive("make-weak-key-hash-table", 0, 1, a => new SchemeHashTable(ReferenceComparer.Instance));
        interpreter.DefinePrimitive("make-weak-value-hash-table", 0, 1, a => new SchemeHashTable(ReferenceComparer.Instance));
        interpreter.DefinePrimitive("make-equal-hash-table", 0, 1, a => new SchemeHashTable(new EqualComparer()));
        interpreter.DefinePrimitive("hash-table?", 1, 1, a => a[0] is SchemeHashTable);

        // The hashq/hashv/hash families differ only in key equivalence. Our tables carry
        // their comparer, so the three families share one implementation here; that is a
        // simplification, but no LilyPond code mixes families on one table.
        foreach (string prefix in new[] { "hashq", "hashv", "hash" })
        {
            string captured = prefix;
            interpreter.DefinePrimitive(captured + "-ref", 2, 3, a =>
            {
                Pair handle = Table(a[0], captured).GetHandle(a[1]);
                return handle != null ? handle.Cdr : (a.Length > 2 ? a[2] : false);
            });

            interpreter.DefinePrimitive(captured + "-set!", 3, 3, a =>
            {
                Table(a[0], captured).Set(a[1], a[2]);
                return a[2];
            });

            interpreter.DefinePrimitive(captured + "-remove!", 2, 2, a =>
            {
                Table(a[0], captured).Remove(a[1]);
                return Unspecified.Instance;
            });

            interpreter.DefinePrimitive(captured + "-get-handle", 2, 2, a =>
            {
                Pair handle = Table(a[0], captured).GetHandle(a[1]);
                return handle != null ? handle : (object)false;
            });

            interpreter.DefinePrimitive(captured + "-create-handle!", 3, 3, a =>
                Table(a[0], captured).CreateHandle(a[1], a[2]));
        }

        interpreter.DefinePrimitive("hash-clear!", 1, 1, a =>
        {
            Table(a[0], "hash-clear!").Clear();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("hash-count", 1, 2, a => (long)Table(a[0], "hash-count").Count);

        interpreter.DefinePrimitive("hash-for-each", 2, 2, a =>
        {
            foreach (Pair handle in new List<Pair>(Table(a[1], "hash-for-each").Handles))
            {
                interpreter.Evaluator.Apply(a[0], new[] { handle.Car, handle.Cdr });
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("hash-map->list", 2, 2, a =>
        {
            List<object> results = new List<object>();
            foreach (Pair handle in new List<Pair>(Table(a[1], "hash-map->list").Handles))
            {
                results.Add(interpreter.Evaluator.Apply(a[0], new[] { handle.Car, handle.Cdr }));
            }

            return Pair.ListFrom(results);
        });

        interpreter.DefinePrimitive("hash-fold", 3, 3, a =>
        {
            object accumulator = a[1];
            foreach (Pair handle in new List<Pair>(Table(a[2], "hash-fold").Handles))
            {
                accumulator = interpreter.Evaluator.Apply(a[0], new[] { handle.Car, handle.Cdr, accumulator });
            }

            return accumulator;
        });

        interpreter.DefinePrimitive("hash-table->alist", 1, 1, a =>
        {
            List<object> entries = new List<object>();
            foreach (Pair handle in Table(a[0], "hash-table->alist").Handles)
            {
                entries.Add(new Pair(handle.Car, handle.Cdr));
            }

            return Pair.ListFrom(entries);
        });

        interpreter.DefinePrimitive("hash", 1, 2, a =>
        {
            long modulus = a.Length > 1 ? (long)SchemeNumber.ToBigInteger(a[1]) : long.MaxValue;
            long code = ReferenceComparer.Instance.GetHashCode(a[0]) & 0x7fffffff;
            return modulus <= 0 ? 0L : code % modulus;
        });

        interpreter.DefinePrimitive("hashq", 1, 2, a =>
        {
            long modulus = a.Length > 1 ? (long)SchemeNumber.ToBigInteger(a[1]) : long.MaxValue;
            long code = ReferenceComparer.Instance.GetHashCode(a[0]) & 0x7fffffff;
            return modulus <= 0 ? 0L : code % modulus;
        });
    }

    private sealed class EqualComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => CorePrimitives.SchemeEqual(x, y);

        public int GetHashCode(object obj)
        {
            switch (obj)
            {
                case null: return 0;
                case MutableString s: return s.ToString().GetHashCode(StringComparison.Ordinal);
                case Symbol sym: return sym.Name.GetHashCode(StringComparison.Ordinal);
                case long l: return l.GetHashCode();
                case bool b: return b ? 1 : 2;
                case Pair _: return 17;
                case object[] _: return 19;
                default: return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }

    private static SchemeHashTable Table(object value, string procedureName)
    {
        if (value is SchemeHashTable table)
        {
            return table;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Not a hash table: ~S"),
                Pair.List(value),
                false));
    }

    private static object[] AsVector(object value, string procedureName)
    {
        if (value is object[] vector)
        {
            return vector;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Not a vector: ~S"),
                Pair.List(value),
                false));
    }

    private static int Index(object value) => (int)SchemeNumber.ToBigInteger(value);
}
