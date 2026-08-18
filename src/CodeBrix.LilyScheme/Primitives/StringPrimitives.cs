// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>Strings, symbols, characters and keywords.</summary>
public static class StringPrimitives
{
    /// <summary>Installs the string, symbol, character and keyword primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallStrings(interpreter);
        InstallStringComparisons(interpreter);
        InstallSymbols(interpreter);
        InstallCharacters(interpreter);
    }

    private static void InstallStrings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("string-length", 1, 1, a => (long)Text(a[0], "string-length").Length);
        interpreter.DefinePrimitive("string-ref", 2, 2, a => SchemeChar.Get(Text(a[0], "string-ref")[Index(a[1])]));
        interpreter.DefinePrimitive("string-set!", 3, 3, a =>
        {
            ((MutableString)a[0])[Index(a[1])] = (char)((SchemeChar)a[2]).CodePoint;
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("substring", 2, 3, a =>
        {
            string text = Text(a[0], "substring");
            int start = Index(a[1]);
            int end = a.Length > 2 ? Index(a[2]) : text.Length;
            return new MutableString(text.Substring(start, end - start));
        });

        interpreter.DefinePrimitive("string-append", 0, -1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in a)
            {
                builder.Append(Text(item, "string-append"));
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-copy", 1, 3, a =>
        {
            string text = Text(a[0], "string-copy");
            int start = a.Length > 1 ? Index(a[1]) : 0;
            int end = a.Length > 2 ? Index(a[2]) : text.Length;
            return new MutableString(text.Substring(start, end - start));
        });

        interpreter.DefinePrimitive("make-string", 1, 2, a =>
        {
            int length = Index(a[0]);
            char fill = a.Length > 1 ? (char)((SchemeChar)a[1]).CodePoint : ' ';
            return new MutableString(length, fill);
        });

        interpreter.DefinePrimitive("string", 0, -1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in a)
            {
                builder.Append(char.ConvertFromUtf32(((SchemeChar)item).CodePoint));
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-null?", 1, 1, a => Text(a[0], "string-null?").Length == 0);
        interpreter.DefinePrimitive("string-upcase", 1, 1, a => new MutableString(Text(a[0], "string-upcase").ToUpperInvariant()));
        interpreter.DefinePrimitive("string-downcase", 1, 1, a => new MutableString(Text(a[0], "string-downcase").ToLowerInvariant()));

        // (string-capitalize s) -- Guile upcases the first character of every WORD and
        // downcases the rest of it, where a word is a run of alphanumerics. LilyPond's
        // chord-name.scm titles chord names with it.
        interpreter.DefinePrimitive("string-capitalize", 1, 1, a =>
        {
            char[] characters = Text(a[0], "string-capitalize").ToCharArray();
            bool startOfWord = true;
            for (int i = 0; i < characters.Length; i++)
            {
                bool isWordCharacter = char.IsLetterOrDigit(characters[i]);
                characters[i] = startOfWord && isWordCharacter
                    ? char.ToUpperInvariant(characters[i])
                    : char.ToLowerInvariant(characters[i]);
                startOfWord = !isWordCharacter;
            }

            return new MutableString(new string(characters));
        });

        // (substring/shared s start [end]) -- Guile's SRFI-13 variant is free to return a
        // string that SHARES storage with its argument. Sharing is an optimization the
        // standard permits, never a promise, so copying is a conforming implementation;
        // what matters is that the name exists and slices the same way substring does.
        interpreter.DefinePrimitive("substring/shared", 2, 3, a =>
        {
            string text = Text(a[0], "substring/shared");
            int start = Index(a[1]);
            int end = a.Length > 2 ? Index(a[2]) : text.Length;
            return new MutableString(text.Substring(start, end - start));
        });

        interpreter.DefinePrimitive("string->list", 1, 1, a =>
        {
            List<object> characters = new List<object>();
            foreach (char c in Text(a[0], "string->list"))
            {
                characters.Add(SchemeChar.Get(c));
            }

            return Pair.ListFrom(characters);
        });

        interpreter.DefinePrimitive("list->string", 1, 1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in Pair.ToList(a[0]))
            {
                builder.Append(char.ConvertFromUtf32(((SchemeChar)item).CodePoint));
            }

            return new MutableString(builder.ToString());
        });

        // SRFI-13. The list is reversed FIRST and then converted, which is what makes
        // it worth having as a primitive: scm/'s markup code builds strings by consing
        // characters on and would otherwise reverse an intermediate list itself.
        interpreter.DefinePrimitive("reverse-list->string", 1, 1, a =>
        {
            List<object> items = Pair.ToList(a[0]);
            StringBuilder builder = new StringBuilder(items.Count);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                builder.Append(char.ConvertFromUtf32(((SchemeChar)items[i]).CodePoint));
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-index", 2, 2, a =>
        {
            string text = Text(a[0], "string-index");
            if (a[1] is SchemeChar target)
            {
                int position = text.IndexOf((char)target.CodePoint);
                return position < 0 ? (object)false : (long)position;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (Evaluator.IsTrue(interpreter.Evaluator.Apply(a[1], new object[] { SchemeChar.Get(text[i]) })))
                {
                    return (long)i;
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("string-join", 1, 3, a =>
        {
            List<object> parts = Pair.ToList(a[0]);
            string separator = a.Length > 1 ? Text(a[1], "string-join") : " ";
            string[] pieces = new string[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                pieces[i] = Text(parts[i], "string-join");
            }

            return new MutableString(string.Join(separator, pieces));
        });

        interpreter.DefinePrimitive("string-prefix?", 2, 2, a =>
            Text(a[1], "string-prefix?").StartsWith(Text(a[0], "string-prefix?"), StringComparison.Ordinal));
        interpreter.DefinePrimitive("string-suffix?", 2, 2, a =>
            Text(a[1], "string-suffix?").EndsWith(Text(a[0], "string-suffix?"), StringComparison.Ordinal));
        interpreter.DefinePrimitive("string-contains", 2, 2, a =>
        {
            int position = Text(a[0], "string-contains").IndexOf(Text(a[1], "string-contains"), StringComparison.Ordinal);
            return position < 0 ? (object)false : (long)position;
        });
    }

    private static void InstallStringComparisons(Interpreter interpreter)
    {
        DefineStringComparison(interpreter, "string=?", (x, y) => string.CompareOrdinal(x, y) == 0);
        DefineStringComparison(interpreter, "string<?", (x, y) => string.CompareOrdinal(x, y) < 0);
        DefineStringComparison(interpreter, "string>?", (x, y) => string.CompareOrdinal(x, y) > 0);
        DefineStringComparison(interpreter, "string<=?", (x, y) => string.CompareOrdinal(x, y) <= 0);
        DefineStringComparison(interpreter, "string>=?", (x, y) => string.CompareOrdinal(x, y) >= 0);
        DefineStringComparison(
            interpreter,
            "string-ci=?",
            (x, y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase));

        // SRFI-13's string= (libguile/srfi-13.c): exactly two strings with
        // optional start/end ranges, answering a plain boolean -- distinct from
        // R7RS string=?, which is variadic and whole-string. ly/articulate.ly
        // compares tempo markup text with it.
        interpreter.DefinePrimitive("string=", 2, 6, a =>
        {
            string first = Text(a[0], "string=");
            string second = Text(a[1], "string=");
            int start1 = SubstringBound(a, 2, 0, first.Length);
            int end1 = SubstringBound(a, 3, first.Length, first.Length);
            int start2 = SubstringBound(a, 4, 0, second.Length);
            int end2 = SubstringBound(a, 5, second.Length, second.Length);
            if (end1 < start1 || end2 < start2)
            {
                throw SubstringRangeError(end1 < start1 ? a[3] : a[5]);
            }

            return end1 - start1 == end2 - start2
                && string.CompareOrdinal(first, start1, second, start2, end1 - start1) == 0;
        });
    }

    private static int SubstringBound(object[] arguments, int index, int fallback, int limit)
    {
        if (arguments.Length <= index)
        {
            return fallback;
        }

        int value = (int)SchemeNumber.ToBigInteger(arguments[index]);
        if (value < 0 || value > limit)
        {
            throw SubstringRangeError(arguments[index]);
        }

        return value;
    }

    private static SchemeThrow SubstringRangeError(object value)
        => new SchemeThrow(
            Symbol.Intern("out-of-range"),
            Pair.List(
                new MutableString("string="),
                new MutableString("Argument out of range: ~S"),
                Pair.List(value),
                false));

    private static void DefineStringComparison(Interpreter interpreter, string name, Func<string, string, bool> comparison)
    {
        interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (!comparison(Text(a[i], name), Text(a[i + 1], name)))
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static void InstallSymbols(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("symbol->string", 1, 1, a => new MutableString(((Symbol)a[0]).Name));
        interpreter.DefinePrimitive("string->symbol", 1, 1, a => Symbol.Intern(Text(a[0], "string->symbol")));
        interpreter.DefinePrimitive("string->uninterned-symbol", 1, 1, a => Symbol.Generate(Text(a[0], "string->uninterned-symbol")));

        interpreter.DefinePrimitive("symbol-append", 0, -1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in a)
            {
                builder.Append(((Symbol)item).Name);
            }

            return Symbol.Intern(builder.ToString());
        });

        // Guile's gensym takes an optional prefix and returns an uninterned symbol;
        // psyntax leans on this for hygiene, so the uninterned part is essential.
        interpreter.DefinePrimitive("gensym", 0, 1, a =>
            Symbol.Generate(a.Length > 0 ? Text(a[0], "gensym") : " g"));

        interpreter.DefinePrimitive("symbol->keyword", 1, 1, a => Keyword.Get((Symbol)a[0]));
        interpreter.DefinePrimitive("keyword->symbol", 1, 1, a => ((Keyword)a[0]).Name);
        interpreter.DefinePrimitive("symbol-interned?", 1, 1, a => !((Symbol)a[0]).IsUninterned);

        interpreter.DefinePrimitive("symbol", 0, -1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in a)
            {
                builder.Append(char.ConvertFromUtf32(((SchemeChar)item).CodePoint));
            }

            return Symbol.Intern(builder.ToString());
        });
    }

    private static void InstallCharacters(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("char->integer", 1, 1, a => (long)((SchemeChar)a[0]).CodePoint);
        interpreter.DefinePrimitive("integer->char", 1, 1, a => SchemeChar.Get(Index(a[0])));
        interpreter.DefinePrimitive("char-upcase", 1, 1, a =>
            SchemeChar.Get(char.ToUpperInvariant((char)((SchemeChar)a[0]).CodePoint)));
        interpreter.DefinePrimitive("char-downcase", 1, 1, a =>
            SchemeChar.Get(char.ToLowerInvariant((char)((SchemeChar)a[0]).CodePoint)));

        DefineCharComparison(interpreter, "char=?", (x, y) => x == y);
        DefineCharComparison(interpreter, "char<?", (x, y) => x < y);
        DefineCharComparison(interpreter, "char>?", (x, y) => x > y);
        DefineCharComparison(interpreter, "char<=?", (x, y) => x <= y);
        DefineCharComparison(interpreter, "char>=?", (x, y) => x >= y);

        interpreter.DefinePrimitive("char-alphabetic?", 1, 1, a => char.IsLetter((char)((SchemeChar)a[0]).CodePoint));
        interpreter.DefinePrimitive("char-numeric?", 1, 1, a => char.IsDigit((char)((SchemeChar)a[0]).CodePoint));
        interpreter.DefinePrimitive("char-whitespace?", 1, 1, a => char.IsWhiteSpace((char)((SchemeChar)a[0]).CodePoint));
        interpreter.DefinePrimitive("char-upper-case?", 1, 1, a => char.IsUpper((char)((SchemeChar)a[0]).CodePoint));
        interpreter.DefinePrimitive("char-lower-case?", 1, 1, a => char.IsLower((char)((SchemeChar)a[0]).CodePoint));
    }

    private static void DefineCharComparison(Interpreter interpreter, string name, Func<int, int, bool> comparison)
    {
        interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (!comparison(((SchemeChar)a[i]).CodePoint, ((SchemeChar)a[i + 1]).CodePoint))
                {
                    return false;
                }
            }

            return true;
        });
    }

    /// <summary>Extracts CLR text from a Scheme string, symbol or character.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="procedureName">The calling primitive, used in error messages.</param>
    /// <returns>The text.</returns>
    public static string Text(object value, string procedureName)
    {
        switch (value)
        {
            case MutableString mutable: return mutable.ToString();
            case Symbol symbol: return symbol.Name;
            case SchemeChar character: return character.ToString();
            case Keyword keyword: return keyword.Name.Name;
            default:
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString(procedureName),
                        new MutableString("Wrong type argument: ~S"),
                        Pair.List(value),
                        false));
        }
    }

    private static int Index(object value) => (int)SchemeNumber.ToBigInteger(value);
}
