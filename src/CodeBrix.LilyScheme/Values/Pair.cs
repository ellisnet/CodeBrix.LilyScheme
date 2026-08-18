// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyScheme.Values;

/// <summary>
/// A mutable cons cell. Scheme pairs are mutable through <c>set-car!</c> and
/// <c>set-cdr!</c>, so these are fields rather than init-only properties.
/// </summary>
public sealed class Pair
{
    /// <summary>Initializes a new cons cell.</summary>
    /// <param name="car">The first element.</param>
    /// <param name="cdr">The rest of the list.</param>
    public Pair(object car, object cdr)
    {
        Car = car;
        Cdr = cdr;
    }

    /// <summary>Gets or sets the first element of the pair.</summary>
    public object Car { get; set; }

    /// <summary>Gets or sets the second element of the pair.</summary>
    public object Cdr { get; set; }

    /// <summary>Builds a proper list from the supplied items.</summary>
    /// <param name="items">The elements, in order.</param>
    /// <returns>A Scheme list, or the empty list when <paramref name="items"/> is empty.</returns>
    public static object List(params object[] items)
    {
        object result = Nil.Instance;
        if (items == null)
        {
            return result;
        }

        for (int i = items.Length - 1; i >= 0; i--)
        {
            result = new Pair(items[i], result);
        }

        return result;
    }

    /// <summary>Builds a proper list from an enumerable.</summary>
    /// <param name="items">The elements, in order.</param>
    /// <returns>A Scheme list.</returns>
    public static object ListFrom(IEnumerable<object> items)
    {
        List<object> buffer = new List<object>();
        if (items != null)
        {
            buffer.AddRange(items);
        }

        object result = Nil.Instance;
        for (int i = buffer.Count - 1; i >= 0; i--)
        {
            result = new Pair(buffer[i], result);
        }

        return result;
    }

    /// <summary>
    /// Walks a Scheme list into a CLR list. Stops at the first non-pair, so an improper
    /// list yields its proper prefix; <paramref name="tail"/> receives whatever terminated it.
    /// </summary>
    /// <param name="list">The Scheme list to walk.</param>
    /// <param name="tail">Receives the terminating value (<see cref="Nil"/> for a proper list).</param>
    /// <returns>The elements of the proper prefix.</returns>
    public static List<object> ToList(object list, out object tail)
    {
        List<object> result = new List<object>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            result.Add(pair.Car);
            cursor = pair.Cdr;
        }

        tail = cursor;
        return result;
    }

    /// <summary>Walks a proper Scheme list into a CLR list, ignoring any improper tail.</summary>
    /// <param name="list">The Scheme list to walk.</param>
    /// <returns>The elements of the proper prefix.</returns>
    public static List<object> ToList(object list) => ToList(list, out _);

    /// <summary>Counts the elements in the proper prefix of a list.</summary>
    /// <param name="list">The Scheme list to measure.</param>
    /// <returns>The number of pairs before the terminator.</returns>
    public static int Length(object list)
    {
        int count = 0;
        object cursor = list;
        while (cursor is Pair pair)
        {
            count++;
            cursor = pair.Cdr;
        }

        return count;
    }
}

/// <summary>
/// The empty list. Distinct from <c>#f</c> — Guile, like R7RS and unlike some older
/// Lisps, treats <c>'()</c> as a true value.
/// </summary>
public sealed class Nil
{
    private Nil()
    {
    }

    /// <summary>Gets the single empty-list instance.</summary>
    public static Nil Instance { get; } = new Nil();

    /// <summary>Returns the external representation of the empty list.</summary>
    /// <returns>The string <c>()</c>.</returns>
    public override string ToString() => "()";
}
