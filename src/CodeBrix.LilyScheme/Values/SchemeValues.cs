// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.LilyScheme.Values;

/// <summary>
/// The unspecified value, returned by expressions whose result is not defined —
/// <c>set!</c>, <c>define</c>, a one-armed <c>if</c> that takes the missing branch.
/// </summary>
public sealed class Unspecified
{
    private Unspecified()
    {
    }

    /// <summary>Gets the single unspecified instance.</summary>
    public static Unspecified Instance { get; } = new Unspecified();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;unspecified&gt;</c>.</returns>
    public override string ToString() => "#<unspecified>";
}

/// <summary>
/// The marker Guile passes for an omitted optional argument that has no default
/// expression. Distinct from <c>#f</c>, so a caller can pass <c>#f</c> explicitly.
/// </summary>
public sealed class DefaultArgument
{
    private DefaultArgument()
    {
    }

    /// <summary>Gets the single default-argument instance.</summary>
    public static DefaultArgument Instance { get; } = new DefaultArgument();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;default&gt;</c>.</returns>
    public override string ToString() => "#<default>";
}

/// <summary>
/// Guile's <c>#nil</c>: the Elisp nil, which exists so that Scheme and Elisp can share
/// one runtime. It is unusual in being BOTH false and null — <c>(if #nil 'a 'b)</c>
/// yields <c>b</c> and <c>(null? #nil)</c> is true — while still being <c>eq?</c>-distinct
/// from both <c>#f</c> and <c>'()</c>. psyntax compares against it directly.
/// </summary>
public sealed class ElispNil
{
    private ElispNil()
    {
    }

    /// <summary>Gets the single Elisp-nil instance.</summary>
    public static ElispNil Instance { get; } = new ElispNil();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#nil</c>.</returns>
    public override string ToString() => "#nil";
}

/// <summary>
/// A promise created by <c>delay</c> in the prelude, holding the thunk that computes
/// the value and caching the result once forced.
/// </summary>
public sealed class LazyPromise
{
    /// <summary>Initializes a promise over a thunk.</summary>
    /// <param name="thunk">The zero-argument procedure producing the value.</param>
    public LazyPromise(object thunk)
    {
        Thunk = thunk;
    }

    /// <summary>Gets the thunk producing the value.</summary>
    public object Thunk { get; }

    /// <summary>Gets or sets a value indicating whether the promise has been forced.</summary>
    public bool IsForced { get; set; }

    /// <summary>Gets or sets the cached value once forced.</summary>
    public object Value { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;promise&gt;</c>.</returns>
    public override string ToString() => "#<promise>";
}

/// <summary>
/// A Guile hook: an ordered list of procedures run for effect by <c>run-hook</c>.
/// </summary>
public sealed class SchemeHook
{
    /// <summary>Gets the procedures attached to this hook, in run order.</summary>
    public List<object> Procedures { get; } = new List<object>();

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the procedure count.</returns>
    public override string ToString()
        => "#<hook " + Procedures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ">";
}

/// <summary>The end-of-file object returned by the reader when input is exhausted.</summary>
public sealed class EofObject
{
    private EofObject()
    {
    }

    /// <summary>Gets the single end-of-file instance.</summary>
    public static EofObject Instance { get; } = new EofObject();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;eof&gt;</c>.</returns>
    public override string ToString() => "#<eof>";
}

/// <summary>
/// A Scheme character. Wrapped rather than using <see cref="char"/> so that astral-plane
/// code points survive: Scheme characters are Unicode scalar values, not UTF-16 units.
/// </summary>
public sealed class SchemeChar : IEquatable<SchemeChar>
{
    private static readonly SchemeChar[] AsciiCache = BuildAsciiCache();

    private SchemeChar(int codePoint)
    {
        CodePoint = codePoint;
    }

    /// <summary>Gets the Unicode scalar value.</summary>
    public int CodePoint { get; }

    /// <summary>Returns the character for a code point, using a cache for ASCII.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns>A <see cref="SchemeChar"/>.</returns>
    public static SchemeChar Get(int codePoint)
    {
        if (codePoint >= 0 && codePoint < AsciiCache.Length)
        {
            return AsciiCache[codePoint];
        }

        return new SchemeChar(codePoint);
    }

    private static SchemeChar[] BuildAsciiCache()
    {
        SchemeChar[] cache = new SchemeChar[128];
        for (int i = 0; i < cache.Length; i++)
        {
            cache[i] = new SchemeChar(i);
        }

        return cache;
    }

    /// <summary>Compares two characters by code point.</summary>
    /// <param name="other">The character to compare with.</param>
    /// <returns><see langword="true"/> when the code points match.</returns>
    public bool Equals(SchemeChar other) => other != null && other.CodePoint == CodePoint;

    /// <summary>Compares this character with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal character.</returns>
    public override bool Equals(object obj) => Equals(obj as SchemeChar);

    /// <summary>Returns a hash code derived from the code point.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => CodePoint;

    /// <summary>Returns the character as a CLR string.</summary>
    /// <returns>The code point rendered as text.</returns>
    public override string ToString() => char.ConvertFromUtf32(CodePoint);
}

/// <summary>
/// A Scheme keyword such as <c>#:optional</c>. Guile keywords are self-evaluating and
/// are distinct from the symbol of the same name.
/// </summary>
public sealed class Keyword
{
    private static readonly Dictionary<string, Keyword> Interned = new Dictionary<string, Keyword>(StringComparer.Ordinal);

    private Keyword(Symbol name)
    {
        Name = name;
    }

    /// <summary>Gets the symbol naming this keyword, without the <c>#:</c> prefix.</summary>
    public Symbol Name { get; }

    /// <summary>Returns the interned keyword for a name.</summary>
    /// <param name="name">The keyword name without its prefix.</param>
    /// <returns>The unique <see cref="Keyword"/> for that name.</returns>
    public static Keyword Get(string name)
    {
        lock (Interned)
        {
            if (!Interned.TryGetValue(name, out Keyword existing))
            {
                existing = new Keyword(Symbol.Intern(name));
                Interned.Add(name, existing);
            }

            return existing;
        }
    }

    /// <summary>Returns the interned keyword for a symbol.</summary>
    /// <param name="name">The symbol naming the keyword.</param>
    /// <returns>The unique <see cref="Keyword"/>.</returns>
    public static Keyword Get(Symbol name) => Get(name.Name);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The keyword written as <c>#:name</c>.</returns>
    public override string ToString() => "#:" + Name.Name;
}

/// <summary>
/// A mutable Scheme string. Scheme strings support <c>string-set!</c>, so a CLR
/// <see cref="string"/> cannot represent them faithfully.
/// </summary>
public sealed class MutableString
{
    private readonly StringBuilder _builder;

    /// <summary>Initializes a string from CLR text.</summary>
    /// <param name="value">The initial contents.</param>
    public MutableString(string value)
    {
        _builder = new StringBuilder(value ?? string.Empty);
    }

    /// <summary>Initializes a string of a given length filled with one character.</summary>
    /// <param name="length">The number of characters.</param>
    /// <param name="fill">The fill character.</param>
    public MutableString(int length, char fill)
    {
        _builder = new StringBuilder(new string(fill, length));
    }

    /// <summary>Gets the number of UTF-16 units in the string.</summary>
    public int Length => _builder.Length;

    /// <summary>Gets or sets a character by index.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The character at that position.</returns>
    public char this[int index]
    {
        get => _builder[index];
        set => _builder[index] = value;
    }

    /// <summary>Returns the string contents as CLR text.</summary>
    /// <returns>The current contents.</returns>
    public override string ToString() => _builder.ToString();
}

/// <summary>
/// A multiple-values package produced by <c>values</c> when the count is not exactly one.
/// A single value is represented by itself rather than by a wrapper, matching Guile.
/// </summary>
public sealed class MultipleValues
{
    /// <summary>Initializes a multiple-values package.</summary>
    /// <param name="items">The values being returned.</param>
    public MultipleValues(object[] items)
    {
        Items = items ?? Array.Empty<object>();
    }

    /// <summary>Gets the values carried by this package.</summary>
    public object[] Items { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the value count.</returns>
    public override string ToString() => "#<values " + Items.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ">";
}

/// <summary>
/// A mutable box holding a module-level binding. Guile exposes these to Scheme as
/// first-class <c>variable</c> objects, and psyntax uses them to resolve identifiers.
/// </summary>
public sealed class Variable
{
    private object _value;

    /// <summary>Initializes an unbound variable.</summary>
    public Variable()
    {
        _value = null;
        IsBound = false;
    }

    /// <summary>Initializes a bound variable.</summary>
    /// <param name="value">The initial value.</param>
    public Variable(object value)
    {
        _value = value;
        IsBound = true;
    }

    /// <summary>Gets a value indicating whether the variable currently holds a value.</summary>
    public bool IsBound { get; private set; }

    /// <summary>Gets the current value.</summary>
    /// <returns>The stored value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the variable is unbound.</exception>
    public object GetValue()
    {
        if (!IsBound)
        {
            throw new InvalidOperationException("Unbound variable.");
        }

        return _value;
    }

    /// <summary>Sets the value, binding the variable if it was unbound.</summary>
    /// <param name="value">The new value.</param>
    public void SetValue(object value)
    {
        _value = value;
        IsBound = true;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the variable's bound state.</returns>
    public override string ToString() => IsBound ? "#<variable bound>" : "#<variable unbound>";
}

/// <summary>
/// A dynamically scoped cell. <c>with-fluid*</c> rebinds one for the duration of a call;
/// psyntax uses fluids to carry expansion context.
/// </summary>
public sealed class Fluid
{
    /// <summary>Initializes a fluid with its default value.</summary>
    /// <param name="defaultValue">The value seen outside any rebinding.</param>
    public Fluid(object defaultValue)
    {
        Value = defaultValue;
    }

    /// <summary>Gets or sets the fluid's current value.</summary>
    public object Value { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;fluid&gt;</c>.</returns>
    public override string ToString() => "#<fluid>";
}
