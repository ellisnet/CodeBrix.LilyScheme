// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace CodeBrix.LilyScheme.Unicode;

/// <summary>
/// The formal Unicode names, in both directions — what Guile's
/// <c>(ice-9 unicode)</c> answers from <c>char-&gt;formal-name</c> and
/// <c>formal-name-&gt;char</c>.
/// <para>
/// Guile implements both in <c>libguile/unicode.c</c> over GNU libunistring's
/// <c>uniname</c>. There is no managed equivalent — .NET exposes character
/// categories and casing but not names — so the table ships as an embedded
/// resource, derived from Unicode, Inc.'s <c>UnicodeData.txt</c> by
/// <c>tools/unicode-names/generate-unicode-names.py</c>, which also has a
/// <c>--check</c> mode fencing the committed asset against its source.
/// </para>
/// <para>
/// ONLY LITERALLY-NAMED CODE POINTS ARE IN THE TABLE, and that is a measurement
/// rather than a shortcut: Guile answers <c>#f</c> for a CJK ideograph rather
/// than deriving <c>CJK UNIFIED IDEOGRAPH-898B</c>, and Python's
/// <c>unicodedata</c>, which does derive the algorithmic names, would have been
/// the wrong authority to copy. Checked against 316 occurrences of a name
/// printed by GNU Guile through GNU LilyPond 2.27.2, across 79 distinct
/// characters: all 316 agree, that negative included.
/// </para>
/// <para>
/// Both directions are lazy and both are built from ONE read: nothing is
/// decompressed until a caller asks, and the reverse index is built only if
/// <see cref="Find"/> is called. A program that never asks for a character name
/// pays for the bytes on disk and nothing else.
/// </para>
/// </summary>
public static class UnicodeCharacterNames
{
    private const string ResourceName = "CodeBrix.LilyScheme.Unicode.unicode-names.deflate";

    private static readonly object Gate = new object();
    private static Dictionary<int, string> _byCodePoint;
    private static Dictionary<string, int> _byName;
    private static string _version = string.Empty;

    /// <summary>
    /// Gets the Unicode Character Database version the shipped table was built
    /// from, or the empty string when the table is absent.
    /// </summary>
    /// <remarks>
    /// Character names are stable once assigned, but every Unicode release adds
    /// thousands, so which release the table came from is part of what it says.
    /// It is recorded in the asset's own first line rather than only in the
    /// notices file, so it travels with the bytes.
    /// </remarks>
    public static string UnicodeVersion
    {
        get
        {
            Table();
            return _version;
        }
    }

    /// <summary>Gets how many names the table holds.</summary>
    public static int Count => Table().Count;

    /// <summary>
    /// Returns a code point's formal Unicode name, or <see langword="null"/> when
    /// it has none — which is the case <c>char-&gt;formal-name</c> answers
    /// <c>#f</c> for.
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string Of(int codePoint)
        => Table().TryGetValue(codePoint, out string name) ? name : null;

    /// <summary>
    /// Returns the code point of a formal Unicode name, or <c>-1</c> when no
    /// character has that name — <c>formal-name-&gt;char</c>'s <c>#f</c>.
    /// </summary>
    /// <remarks>
    /// Names are matched exactly, as libunistring matches them: they are
    /// upper-case ASCII with spaces and hyphens, and the lookup is ordinal.
    /// </remarks>
    /// <param name="name">The formal name.</param>
    /// <returns>The code point, or <c>-1</c>.</returns>
    public static int Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        return Reverse().TryGetValue(name, out int codePoint) ? codePoint : -1;
    }

    private static Dictionary<int, string> Table()
    {
        lock (Gate)
        {
            return _byCodePoint ??= Read();
        }
    }

    private static Dictionary<string, int> Reverse()
    {
        lock (Gate)
        {
            if (_byName != null)
            {
                return _byName;
            }

            Dictionary<int, string> forward = _byCodePoint ??= Read();
            _byName = new Dictionary<string, int>(forward.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<int, string> entry in forward)
            {
                // A name identifies at most one character, so the first wins and
                // nothing should collide; TryAdd rather than the indexer says so
                // without throwing on a table that somehow does.
                _byName.TryAdd(entry.Value, entry.Key);
            }

            return _byName;
        }
    }

    private static Dictionary<int, string> Read()
    {
        Assembly assembly = typeof(UnicodeCharacterNames).Assembly;
        using Stream compressed = assembly.GetManifestResourceStream(ResourceName);
        if (compressed == null)
        {
            return new Dictionary<int, string>();
        }

        // The asset is a raw zlib stream — deflate wrapped in RFC 1950's two-byte
        // header and Adler-32 tail — so ZLibStream, not DeflateStream.
        using ZLibStream inflating = new ZLibStream(compressed, CompressionMode.Decompress);
        using StreamReader reader = new StreamReader(inflating);

        Dictionary<int, string> names = new Dictionary<int, string>(40000);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                ReadVersion(line);
                continue;
            }

            int separator = line.IndexOf(';');
            if (separator <= 0)
            {
                continue;
            }

            names[Convert.ToInt32(line.Substring(0, separator), 16)]
                = line.Substring(separator + 1);
        }

        return names;
    }

    private static void ReadVersion(string comment)
    {
        // "# Unicode Character Database 15.1.0 -- code point and formal name only."
        const string marker = "Unicode Character Database ";
        int at = comment.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }

        int start = at + marker.Length;
        int end = comment.IndexOf(' ', start);
        _version = end < 0 ? comment.Substring(start) : comment.Substring(start, end - start);
    }
}
