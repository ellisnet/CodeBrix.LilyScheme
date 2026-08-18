// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Guile's multi-dimensional array procedures over <see cref="SchemeArray"/>: the
/// subset LilyPond uses — <c>lily-library.scm</c>'s <c>array-copy/subarray!</c> and
/// <c>matrix-rotate-counterclockwise</c>, and the QR-code generator's tables and
/// matrix walks. Written against the "Arrays" chapter of the Guile manual.
/// </summary>
public static class ArrayPrimitives
{
    /// <summary>Registers the array primitives.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // A vector answers #t here for the same reason AsArray accepts one: scm_is_array
        // counts scm_tc7_vector. Answering #f while array-ref accepts the value would be
        // the two halves disagreeing about one question.
        interpreter.DefinePrimitive("array?", 1, 1, a => a[0] is SchemeArray || a[0] is object[]);

        interpreter.DefinePrimitive("array-rank", 1, 1, a =>
            (long)AsArray(a[0], "array-rank").Rank);

        interpreter.DefinePrimitive("make-array", 1, -1, a =>
        {
            ParseBounds(a, 1, "make-array", out int[] lowerBounds, out int[] lengths);
            int count = 1;
            foreach (int length in lengths)
            {
                count *= length;
            }

            object[] storage = new object[count];
            for (int i = 0; i < count; i++)
            {
                storage[i] = a[0];
            }

            return new SchemeArray(lowerBounds, lengths, storage);
        });

        interpreter.DefinePrimitive("array-ref", 2, -1, a =>
            Ref(interpreter, AsArray(a[0], "array-ref"), Indices(a, 1, "array-ref")));

        // Guile's argument order: (array-set! array VALUE index ...).
        interpreter.DefinePrimitive("array-set!", 3, -1, a =>
        {
            Set(interpreter, AsArray(a[0], "array-set!"), Indices(a, 2, "array-set!"), a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("array-dimensions", 1, 1, a =>
        {
            SchemeArray array = AsArray(a[0], "array-dimensions");
            List<object> dimensions = new List<object>();
            for (int d = 0; d < array.Rank; d++)
            {
                dimensions.Add(array.LowerBounds[d] == 0
                    ? (object)(long)array.Lengths[d]
                    : Pair.List(
                        (long)array.LowerBounds[d],
                        (long)(array.LowerBounds[d] + array.Lengths[d] - 1)));
            }

            return Pair.ListFrom(dimensions);
        });

        interpreter.DefinePrimitive("array-shape", 1, 1, a =>
        {
            SchemeArray array = AsArray(a[0], "array-shape");
            List<object> shape = new List<object>();
            for (int d = 0; d < array.Rank; d++)
            {
                shape.Add(Pair.List(
                    (long)array.LowerBounds[d],
                    (long)(array.LowerBounds[d] + array.Lengths[d] - 1)));
            }

            return Pair.ListFrom(shape);
        });

        interpreter.DefinePrimitive("array->list", 1, 1, a =>
        {
            SchemeArray array = AsArray(a[0], "array->list");
            long[] indices = StartIndices(array);
            return NestedList(interpreter, array, indices, 0);
        });

        interpreter.DefinePrimitive("array-fill!", 2, 2, a =>
        {
            SchemeArray array = AsArray(a[0], "array-fill!");
            ForEachIndex(array, indices => Set(interpreter, array, indices, a[1]));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("array-copy", 1, 1, a =>
            Copy(interpreter, AsArray(a[0], "array-copy")));

        interpreter.DefinePrimitive("array-copy!", 2, 2, a =>
        {
            SchemeArray source = AsArray(a[0], "array-copy!");
            SchemeArray destination = AsArray(a[1], "array-copy!");
            if (source.Rank != destination.Rank)
            {
                throw WrongType("array-copy!", "Array rank mismatch: ~S", a[1]);
            }

            for (int d = 0; d < source.Rank; d++)
            {
                if (source.Lengths[d] != destination.Lengths[d])
                {
                    throw WrongType("array-copy!", "Array shape mismatch: ~S", a[1]);
                }
            }

            ForEachIndex(source, indices =>
            {
                long[] destinationIndices = new long[indices.Length];
                for (int d = 0; d < indices.Length; d++)
                {
                    destinationIndices[d] = indices[d] - source.LowerBounds[d]
                        + destination.LowerBounds[d];
                }

                Set(
                    interpreter,
                    destination,
                    destinationIndices,
                    Ref(interpreter, source, indices));
            });
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("array-for-each", 2, -1, a =>
        {
            SchemeArray[] arrays = new SchemeArray[a.Length - 1];
            for (int i = 1; i < a.Length; i++)
            {
                arrays[i - 1] = AsArray(a[i], "array-for-each");
            }

            ForEachIndex(arrays[0], indices =>
            {
                object[] arguments = new object[arrays.Length];
                for (int i = 0; i < arrays.Length; i++)
                {
                    arguments[i] = Ref(interpreter, arrays[i], indices);
                }

                interpreter.Evaluator.Apply(a[0], arguments);
            });
            return Unspecified.Instance;
        });

        // Guile's argument order: (array-index-map! array proc) — each element is set
        // to proc applied to its (absolute) indices.
        interpreter.DefinePrimitive("array-index-map!", 2, 2, a =>
        {
            SchemeArray array = AsArray(a[0], "array-index-map!");
            ForEachIndex(array, indices =>
            {
                object[] arguments = new object[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    arguments[i] = indices[i];
                }

                Set(interpreter, array, indices, interpreter.Evaluator.Apply(a[1], arguments));
            });
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("make-shared-array", 2, -1, a =>
        {
            SchemeArray target = AsArray(a[0], "make-shared-array");
            ParseBounds(a, 2, "make-shared-array", out int[] lowerBounds, out int[] lengths);
            return new SchemeArray(lowerBounds, lengths, target, a[1]);
        });

        interpreter.DefinePrimitive("transpose-array", 2, -1, a =>
        {
            SchemeArray array = AsArray(a[0], "transpose-array");
            if (a.Length - 1 != array.Rank)
            {
                throw WrongType("transpose-array", "Bad number of dimensions: ~S", a[0]);
            }

            // Argument k names the NEW axis that OLD axis k becomes.
            int[] permutation = new int[array.Rank];
            for (int k = 0; k < array.Rank; k++)
            {
                permutation[k] = (int)AsIndex(a[k + 1], "transpose-array");
            }

            int[] lowerBounds = new int[array.Rank];
            int[] lengths = new int[array.Rank];
            for (int k = 0; k < array.Rank; k++)
            {
                lowerBounds[permutation[k]] = array.LowerBounds[k];
                lengths[permutation[k]] = array.Lengths[k];
            }

            Func<long[], long[]> mapper = indices =>
            {
                long[] mapped = new long[array.Rank];
                for (int k = 0; k < array.Rank; k++)
                {
                    mapped[k] = indices[permutation[k]];
                }

                return mapped;
            };
            return new SchemeArray(lowerBounds, lengths, array, mapper);
        });

        // With fewer indices than the rank, returns a shared view of the remaining
        // dimensions; with a full set, the element itself.
        interpreter.DefinePrimitive("array-cell-ref", 1, -1, a =>
        {
            SchemeArray array = AsArray(a[0], "array-cell-ref");
            long[] prefix = Indices(a, 1, "array-cell-ref");
            if (prefix.Length == array.Rank)
            {
                return Ref(interpreter, array, prefix);
            }

            if (prefix.Length > array.Rank)
            {
                throw WrongType("array-cell-ref", "Too many indices: ~S", a[0]);
            }

            int remaining = array.Rank - prefix.Length;
            int[] lowerBounds = new int[remaining];
            int[] lengths = new int[remaining];
            for (int d = 0; d < remaining; d++)
            {
                lowerBounds[d] = array.LowerBounds[prefix.Length + d];
                lengths[d] = array.Lengths[prefix.Length + d];
            }

            Func<long[], long[]> mapper = indices =>
            {
                long[] mapped = new long[array.Rank];
                for (int d = 0; d < prefix.Length; d++)
                {
                    mapped[d] = prefix[d];
                }

                for (int d = 0; d < remaining; d++)
                {
                    mapped[prefix.Length + d] = indices[d];
                }

                return mapped;
            };
            return new SchemeArray(lowerBounds, lengths, array, mapper);
        });
    }

    private static SchemeArray AsArray(object value, string procedureName)
    {
        if (value is SchemeArray array)
        {
            return array;
        }

        // libguile/arrays.c's scm_is_array counts a VECTOR as an array -- an ordinary
        // vector IS the rank-1, zero-based case, and Guile's array procedures take one
        // without conversion. LilyPond's qr-code.scm relies on it: its format-information
        // tables are written as #(...) literals and read with array-ref. The wrapper
        // SHARES the vector's storage, so array-set! writes through to it.
        // (Guile also counts strings, bitvectors and bytevectors; nothing asks yet.)
        if (value is object[] vector)
        {
            return new SchemeArray(new[] { 0 }, new[] { vector.Length }, vector);
        }

        throw WrongType(procedureName, "Not an array: ~S", value);
    }

    private static SchemeThrow WrongType(string procedureName, string message, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString(message),
                Pair.List(value),
                false));

    private static long AsIndex(object value, string procedureName)
    {
        switch (value)
        {
            case long l:
                return l;
            case int i:
                return i;
            default:
                throw WrongType(procedureName, "Not an integer: ~S", value);
        }
    }

    private static long[] Indices(object[] arguments, int from, string procedureName)
    {
        long[] indices = new long[arguments.Length - from];
        for (int i = from; i < arguments.Length; i++)
        {
            indices[i - from] = AsIndex(arguments[i], procedureName);
        }

        return indices;
    }

    /// <summary>
    /// Parses <c>make-array</c>-style bounds: a plain integer is a length with lower
    /// bound 0; a two-element list <c>(lower upper)</c> is an INCLUSIVE index range.
    /// </summary>
    private static void ParseBounds(
        object[] arguments,
        int from,
        string procedureName,
        out int[] lowerBounds,
        out int[] lengths)
    {
        int rank = arguments.Length - from;
        lowerBounds = new int[rank];
        lengths = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            object bound = arguments[from + d];
            if (bound is Pair)
            {
                List<object> pair = Pair.ToList(bound);
                if (pair.Count != 2)
                {
                    throw WrongType(procedureName, "Bad array bound: ~S", bound);
                }

                long lower = AsIndex(pair[0], procedureName);
                long upper = AsIndex(pair[1], procedureName);
                lowerBounds[d] = (int)lower;
                lengths[d] = (int)(upper - lower + 1);
            }
            else
            {
                lowerBounds[d] = 0;
                lengths[d] = (int)AsIndex(bound, procedureName);
            }

            if (lengths[d] < 0)
            {
                throw WrongType(procedureName, "Bad array bound: ~S", bound);
            }
        }
    }

    private static void ValidateViewIndices(SchemeArray view, long[] indices, string procedureName)
    {
        if (indices.Length != view.Rank)
        {
            throw WrongType(procedureName, "Bad number of indices: ~S", Pair.ListFrom(IndexObjects(indices)));
        }

        for (int d = 0; d < view.Rank; d++)
        {
            long relative = indices[d] - view.LowerBounds[d];
            if (relative < 0 || relative >= view.Lengths[d])
            {
                throw WrongType(procedureName, "Index out of range: ~S", indices[d]);
            }
        }
    }

    private static object[] IndexObjects(long[] indices)
    {
        object[] boxed = new object[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            boxed[i] = indices[i];
        }

        return boxed;
    }

    private static long[] MapViewIndices(Interpreter interpreter, SchemeArray view, long[] indices)
    {
        if (view.Mapper is Func<long[], long[]> func)
        {
            return func(indices);
        }

        object result = interpreter.Evaluator.Apply(view.Mapper, IndexObjects(indices));
        List<object> mapped = Pair.ToList(result);
        long[] targetIndices = new long[mapped.Count];
        for (int i = 0; i < mapped.Count; i++)
        {
            targetIndices[i] = AsIndex(mapped[i], "make-shared-array mapper");
        }

        return targetIndices;
    }

    private static object Ref(Interpreter interpreter, SchemeArray array, long[] indices)
    {
        while (array.IsShared)
        {
            ValidateViewIndices(array, indices, "array-ref");
            indices = MapViewIndices(interpreter, array, indices);
            array = array.Target;
        }

        return array.Storage[array.Offset(indices)];
    }

    private static void Set(Interpreter interpreter, SchemeArray array, long[] indices, object value)
    {
        while (array.IsShared)
        {
            ValidateViewIndices(array, indices, "array-set!");
            indices = MapViewIndices(interpreter, array, indices);
            array = array.Target;
        }

        array.Storage[array.Offset(indices)] = value;
    }

    private static long[] StartIndices(SchemeArray array)
    {
        long[] indices = new long[array.Rank];
        for (int d = 0; d < array.Rank; d++)
        {
            indices[d] = array.LowerBounds[d];
        }

        return indices;
    }

    /// <summary>Walks every absolute index tuple of an array in row-major order.</summary>
    private static void ForEachIndex(SchemeArray array, Action<long[]> visit)
    {
        if (array.ElementCount == 0)
        {
            return;
        }

        long[] indices = StartIndices(array);
        while (true)
        {
            visit((long[])indices.Clone());

            int dimension = array.Rank - 1;
            while (dimension >= 0)
            {
                indices[dimension]++;
                if (indices[dimension] < array.LowerBounds[dimension] + array.Lengths[dimension])
                {
                    break;
                }

                indices[dimension] = array.LowerBounds[dimension];
                dimension--;
            }

            if (dimension < 0)
            {
                return;
            }
        }
    }

    private static SchemeArray Copy(Interpreter interpreter, SchemeArray source)
    {
        object[] storage = new object[source.ElementCount];
        int offset = 0;
        ForEachIndex(source, indices => storage[offset++] = Ref(interpreter, source, indices));
        return new SchemeArray(
            (int[])source.LowerBounds.Clone(),
            (int[])source.Lengths.Clone(),
            storage);
    }

    private static object NestedList(
        Interpreter interpreter,
        SchemeArray array,
        long[] indices,
        int dimension)
    {
        List<object> items = new List<object>();
        for (int i = 0; i < array.Lengths[dimension]; i++)
        {
            indices[dimension] = array.LowerBounds[dimension] + i;
            items.Add(dimension == array.Rank - 1
                ? Ref(interpreter, array, (long[])indices.Clone())
                : NestedList(interpreter, array, indices, dimension + 1));
        }

        return Pair.ListFrom(items);
    }
}
