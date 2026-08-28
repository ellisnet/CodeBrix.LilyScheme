// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Unicode;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// The <c>(ice-9 unicode)</c> shim — <c>char-&gt;formal-name</c> and
/// <c>formal-name-&gt;char</c>, the two procedures that module exports.
/// <para>
/// Guile implements them in <c>libguile/unicode.c</c> over GNU libunistring, so
/// they are C in Guile and have to be C# here, exactly like
/// <c>(ice-9 iconv)</c> and <c>(ice-9 soft-ports)</c>. The names themselves come
/// from <see cref="UnicodeCharacterNames"/>.
/// </para>
/// <para>
/// BOTH are installed, not just the one a consumer happens to need. A module
/// that exports two procedures and supplies one is a module that answers
/// "unbound variable" to code Guile runs, which is worse than not having it —
/// and the reverse index the second needs is built from the same bytes, lazily,
/// so the honest version costs nothing extra to ship.
/// </para>
/// </summary>
public static class UnicodePrimitives
{
    /// <summary>Installs the <c>(ice-9 unicode)</c> shim module.</summary>
    /// <param name="interpreter">The interpreter to install the shim into.</param>
    public static void InstallShim(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        SchemeModule module = interpreter.Modules.Resolve(Pair.ListFrom(new object[]
        {
            Symbol.Intern("ice-9"), Symbol.Intern("unicode"),
        }));

        module.DefinePublic(
            Symbol.Intern("char->formal-name"),
            new Primitive("char->formal-name", 1, 1, a =>
            {
                string name = UnicodeCharacterNames.Of(
                    CharacterOf(a[0], "char->formal-name"));
                // #f, not the empty string: upstream's docstring says "If the
                // character has no name, return #f", and a caller that tests the
                // result would be told a nameless character has an empty name.
                return name == null ? (object)false : new MutableString(name);
            }));

        module.DefinePublic(
            Symbol.Intern("formal-name->char"),
            new Primitive("formal-name->char", 1, 1, a =>
            {
                int codePoint = UnicodeCharacterNames.Find(
                    TextOf(a[0], "formal-name->char"));
                return codePoint < 0 ? (object)false : SchemeChar.Get(codePoint);
            }));
    }

    private static int CharacterOf(object value, string procedureName)
    {
        if (value is SchemeChar character)
        {
            return character.CodePoint;
        }

        throw WrongType(value, procedureName, "Wrong type argument: ~S");
    }

    private static string TextOf(object value, string procedureName)
    {
        switch (value)
        {
            case MutableString text:
                return text.ToString();
            case string text:
                return text;
            default:
                throw WrongType(value, procedureName, "Not a string: ~S");
        }
    }

    private static SchemeThrow WrongType(object value, string procedureName, string message)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString(message),
                Pair.List(value),
                false));
}
