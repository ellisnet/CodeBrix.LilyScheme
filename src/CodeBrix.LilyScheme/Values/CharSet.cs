// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyScheme.Values;

/// <summary>
/// A SRFI-14 character set.
/// <para>
/// Membership is a predicate rather than a bit vector: the standard sets are defined by
/// Unicode categories, which cover far more of the code space than it is worth
/// materialising, and every operation SRFI-14 defines composes predicates cleanly.
/// </para>
/// </summary>
public sealed class CharSet
{
    private readonly Func<char, bool> _contains;

    /// <summary>Initializes a character set from a membership predicate.</summary>
    /// <param name="name">A name used in the external representation.</param>
    /// <param name="contains">The membership test.</param>
    public CharSet(string name, Func<char, bool> contains)
    {
        Name = name;
        _contains = contains ?? throw new ArgumentNullException(nameof(contains));
    }

    /// <summary>Gets the set's name, used in the external representation.</summary>
    public string Name { get; }

    /// <summary>The empty set.</summary>
    public static readonly CharSet Empty = new CharSet("char-set:empty", _ => false);

    /// <summary>Every character.</summary>
    public static readonly CharSet Full = new CharSet("char-set:full", _ => true);

    /// <summary>The alphabetic characters.</summary>
    public static readonly CharSet Letter = new CharSet("char-set:letter", char.IsLetter);

    /// <summary>The decimal digits.</summary>
    public static readonly CharSet Digit = new CharSet("char-set:digit", char.IsDigit);

    /// <summary>The alphabetic characters and the decimal digits.</summary>
    public static readonly CharSet LetterOrDigit = new CharSet("char-set:letter+digit", char.IsLetterOrDigit);

    /// <summary>The whitespace characters.</summary>
    public static readonly CharSet Whitespace = new CharSet("char-set:whitespace", char.IsWhiteSpace);

    /// <summary>The punctuation characters.</summary>
    public static readonly CharSet Punctuation = new CharSet("char-set:punctuation", char.IsPunctuation);

    /// <summary>The graphic characters -- everything except whitespace and controls.</summary>
    public static readonly CharSet Graphic = new CharSet(
        "char-set:graphic",
        c => !char.IsWhiteSpace(c) && !char.IsControl(c));

    /// <summary>The printing characters -- the graphic characters plus whitespace.</summary>
    public static readonly CharSet Printing = new CharSet("char-set:printing", c => !char.IsControl(c));

    /// <summary>The lower-case characters.</summary>
    public static readonly CharSet LowerCase = new CharSet("char-set:lower-case", char.IsLower);

    /// <summary>The upper-case characters.</summary>
    public static readonly CharSet UpperCase = new CharSet("char-set:upper-case", char.IsUpper);

    /// <summary>The blank characters -- space and tab.</summary>
    public static readonly CharSet Blank = new CharSet("char-set:blank", c => c == ' ' || c == '\t');

    /// <summary>Tests membership.</summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> when the character is in the set.</returns>
    public bool Contains(char value) => _contains(value);

    /// <summary>Builds a set holding exactly the given characters.</summary>
    /// <param name="characters">The members.</param>
    /// <returns>The set.</returns>
    public static CharSet Of(IEnumerable<char> characters)
    {
        HashSet<char> members = new HashSet<char>(characters);
        return new CharSet("char-set", members.Contains);
    }

    /// <summary>Builds the complement of a set.</summary>
    /// <param name="set">The set to complement.</param>
    /// <returns>The complement.</returns>
    public static CharSet Complement(CharSet set)
    {
        if (set == null)
        {
            throw new ArgumentNullException(nameof(set));
        }

        return new CharSet("char-set", c => !set.Contains(c));
    }

    /// <summary>Builds the union of several sets.</summary>
    /// <param name="sets">The sets to combine.</param>
    /// <returns>The union.</returns>
    public static CharSet Union(IReadOnlyList<CharSet> sets)
    {
        if (sets == null)
        {
            throw new ArgumentNullException(nameof(sets));
        }

        return new CharSet("char-set", c =>
        {
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].Contains(c))
                {
                    return true;
                }
            }

            return false;
        });
    }

    /// <summary>Builds the intersection of several sets.</summary>
    /// <param name="sets">The sets to combine.</param>
    /// <returns>The intersection.</returns>
    public static CharSet Intersection(IReadOnlyList<CharSet> sets)
    {
        if (sets == null)
        {
            throw new ArgumentNullException(nameof(sets));
        }

        return new CharSet("char-set", c =>
        {
            for (int i = 0; i < sets.Count; i++)
            {
                if (!sets[i].Contains(c))
                {
                    return false;
                }
            }

            return true;
        });
    }

    /// <summary>Builds the difference of a set and several others.</summary>
    /// <param name="first">The set to subtract from.</param>
    /// <param name="rest">The sets to subtract.</param>
    /// <returns>The difference.</returns>
    public static CharSet Difference(CharSet first, IReadOnlyList<CharSet> rest)
    {
        if (first == null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (rest == null)
        {
            throw new ArgumentNullException(nameof(rest));
        }

        return new CharSet("char-set", c =>
        {
            if (!first.Contains(c))
            {
                return false;
            }

            for (int i = 0; i < rest.Count; i++)
            {
                if (rest[i].Contains(c))
                {
                    return false;
                }
            }

            return true;
        });
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The set's name in angle brackets.</returns>
    public override string ToString() => "#<" + Name + ">";
}
