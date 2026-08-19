// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Checked argument accessors for primitives — the Scheme-error counterpart of a
/// bare C# cast.
/// <para>
/// Guile's own primitives validate each argument and raise a catchable
/// <c>wrong-type-arg</c> naming the procedure and the argument's position
/// (libguile's <c>scm_wrong_type_arg</c>); Scheme code legitimately catches that
/// key. A bare cast in a primitive's body performs the same check the .NET way
/// and lets an <see cref="System.InvalidCastException"/> escape to the host,
/// which no <c>catch</c> can see. Each accessor here raises the positioned
/// Scheme error instead; <see cref="Primitive.Invoke"/> additionally carries a
/// last-resort net for any site still casting bare.
/// </para>
/// </summary>
public static class TypeChecks
{
    /// <summary>Returns the argument as a symbol, or raises <c>wrong-type-arg</c>.</summary>
    /// <param name="value">The argument value.</param>
    /// <param name="procedureName">The calling primitive, for the error.</param>
    /// <param name="position">The one-based argument position, for the error.</param>
    /// <returns>The symbol.</returns>
    public static Symbol AsSymbol(object value, string procedureName, int position)
        => value as Symbol ?? throw WrongType(value, procedureName, position);

    /// <summary>Returns the argument as a character, or raises <c>wrong-type-arg</c>.</summary>
    /// <param name="value">The argument value.</param>
    /// <param name="procedureName">The calling primitive, for the error.</param>
    /// <param name="position">The one-based argument position, for the error.</param>
    /// <returns>The character.</returns>
    public static SchemeChar AsChar(object value, string procedureName, int position)
        => value as SchemeChar ?? throw WrongType(value, procedureName, position);

    /// <summary>Returns the argument as a keyword, or raises <c>wrong-type-arg</c>.</summary>
    /// <param name="value">The argument value.</param>
    /// <param name="procedureName">The calling primitive, for the error.</param>
    /// <param name="position">The one-based argument position, for the error.</param>
    /// <returns>The keyword.</returns>
    public static Keyword AsKeyword(object value, string procedureName, int position)
        => value as Keyword ?? throw WrongType(value, procedureName, position);

    /// <summary>
    /// Returns the argument as a mutable string, or raises <c>wrong-type-arg</c> —
    /// the accessor for primitives that MUTATE, where a symbol or a character
    /// (which <see cref="StringPrimitives.Text"/> would accept for reading) is the
    /// wrong kind of thing entirely.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <param name="procedureName">The calling primitive, for the error.</param>
    /// <param name="position">The one-based argument position, for the error.</param>
    /// <returns>The string.</returns>
    public static MutableString AsMutableString(object value, string procedureName, int position)
        => value as MutableString ?? throw WrongType(value, procedureName, position);

    /// <summary>
    /// Builds the positioned <c>wrong-type-arg</c> throw, in the exact argument
    /// shape this interpreter's other type errors already use — the procedure
    /// name, a message whose <c>~S</c> is filled from the third element, the
    /// offending value, and <c>#f</c>.
    /// </summary>
    /// <param name="value">The offending value.</param>
    /// <param name="procedureName">The calling primitive.</param>
    /// <param name="position">The one-based argument position.</param>
    /// <returns>The throw, ready to raise.</returns>
    private static SchemeThrow WrongType(object value, string procedureName, int position)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString(
                    "Wrong type argument in position "
                    + position.ToString(CultureInfo.InvariantCulture) + ": ~S"),
                Pair.List(value),
                false));
}
