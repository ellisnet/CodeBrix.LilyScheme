// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace CodeBrix.LilyScheme.Values;

/// <summary>
/// A Guile multi-dimensional array: rank-<em>n</em>, row-major, with a per-dimension
/// lower bound — written <c>#1@1(…)</c> for a one-dimensional array indexed from 1,
/// or <c>#2((…) (…))</c> for a matrix. Written against the "Arrays" chapter of the
/// Guile manual; LilyPond's QR-code generator is the demanding consumer.
/// <para>
/// A regular array owns flat <see cref="Storage"/>. A SHARED array — the result of
/// <c>make-shared-array</c>, <c>transpose-array</c> or <c>array-cell-ref</c> — owns
/// no storage: it holds a <see cref="Target"/> plus an index <see cref="Mapper"/>,
/// and every element access goes through the mapper to the target. The mapper is
/// either a C# <c>Func&lt;long[], long[]&gt;</c> or a Scheme procedure; applying a
/// Scheme procedure needs the evaluator, which is why all element access lives in
/// the primitives layer rather than here.
/// </para>
/// </summary>
public sealed class SchemeArray
{
    /// <summary>Initializes a regular array that owns its storage.</summary>
    /// <param name="lowerBounds">The first valid index of each dimension.</param>
    /// <param name="lengths">The extent of each dimension.</param>
    /// <param name="storage">The row-major backing store, of the product length.</param>
    public SchemeArray(int[] lowerBounds, int[] lengths, object[] storage)
    {
        LowerBounds = lowerBounds ?? throw new ArgumentNullException(nameof(lowerBounds));
        Lengths = lengths ?? throw new ArgumentNullException(nameof(lengths));
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>Initializes a shared view over another array.</summary>
    /// <param name="lowerBounds">The first valid index of each dimension of the view.</param>
    /// <param name="lengths">The extent of each dimension of the view.</param>
    /// <param name="target">The array the view reads and writes.</param>
    /// <param name="mapper">The view-index-to-target-index mapping: either a
    /// <c>Func&lt;long[], long[]&gt;</c> or a Scheme procedure.</param>
    public SchemeArray(int[] lowerBounds, int[] lengths, SchemeArray target, object mapper)
    {
        LowerBounds = lowerBounds ?? throw new ArgumentNullException(nameof(lowerBounds));
        Lengths = lengths ?? throw new ArgumentNullException(nameof(lengths));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <summary>Gets the first valid index of each dimension.</summary>
    public int[] LowerBounds { get; }

    /// <summary>Gets the extent of each dimension.</summary>
    public int[] Lengths { get; }

    /// <summary>Gets the row-major backing store, or null for a shared view.</summary>
    public object[] Storage { get; }

    /// <summary>Gets the array a shared view maps into, or null for a regular array.</summary>
    public SchemeArray Target { get; }

    /// <summary>Gets the shared view's index mapper, or null for a regular array.</summary>
    public object Mapper { get; }

    /// <summary>Gets the number of dimensions.</summary>
    public int Rank => Lengths.Length;

    /// <summary>Gets a value indicating whether this is a shared view.</summary>
    public bool IsShared => Target != null;

    /// <summary>Gets the total number of elements.</summary>
    public int ElementCount
    {
        get
        {
            int count = 1;
            foreach (int length in Lengths)
            {
                count *= length;
            }

            return count;
        }
    }

    /// <summary>
    /// Converts absolute indices to the row-major offset into <see cref="Storage"/>.
    /// Only valid on a regular array.
    /// </summary>
    /// <param name="indices">One absolute index per dimension.</param>
    /// <returns>The flat offset.</returns>
    public int Offset(long[] indices)
    {
        if (indices == null)
        {
            throw new ArgumentNullException(nameof(indices));
        }

        if (indices.Length != Rank)
        {
            throw new ArgumentException(
                "expected " + Rank + " indices, got " + indices.Length, nameof(indices));
        }

        int offset = 0;
        for (int dimension = 0; dimension < Rank; dimension++)
        {
            long relative = indices[dimension] - LowerBounds[dimension];
            if (relative < 0 || relative >= Lengths[dimension])
            {
                throw new IndexOutOfRangeException(
                    "array index " + indices[dimension] + " out of range for dimension "
                    + dimension + " [" + LowerBounds[dimension] + ", "
                    + (LowerBounds[dimension] + Lengths[dimension] - 1) + "]");
            }

            offset = (offset * Lengths[dimension]) + (int)relative;
        }

        return offset;
    }

    /// <summary>Returns the external representation, without element contents.</summary>
    /// <returns>A short description; the printer renders full contents.</returns>
    public override string ToString() => "#<array rank " + Rank + ">";
}
