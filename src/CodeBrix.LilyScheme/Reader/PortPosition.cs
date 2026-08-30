// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace CodeBrix.LilyScheme.Reader;

/// <summary>
/// The one place the line and column of a port advance, shared by everything that moves
/// characters: the reader, the tracking output writer and the soft port.
/// <para>
/// Guile keeps a line and a column in EVERY port and updates them with one function
/// regardless of direction, which is why a tab counts the same whether it is written or
/// read. The counters are not decoration — a datum's <c>source-properties</c> ARE the
/// port's line and column at the datum's first character, so these rules decide where
/// every diagnostic points.
/// </para>
/// <para>
/// EVERY RULE HERE WAS MEASURED on the pinned 2.27.2 oracle rather than read off Guile's
/// source. Reading five characters of <c>"abc\ndefgh"</c> leaves a port at line 1 column
/// 1; a tab puts it at the next multiple of eight, so <c>"\t(x)"</c> and eight spaces
/// both record column 8 and <c>"\t  (x)"</c> records 10; a carriage return returns the
/// column without advancing the line; a backspace retreats one but never past zero; an
/// alarm advances nothing; a form feed and a vertical tab are ordinary characters; and a
/// column counts CODE POINTS, so two astral characters make column 2 and not 4.
/// </para>
/// </summary>
public static class PortPosition
{
    /// <summary>The tab stop, in columns. Guile advances a tab to the next multiple.</summary>
    private const long TabWidth = 8;

    /// <summary>Advances a line and column over some text.</summary>
    /// <param name="text">The text passing through the port.</param>
    /// <param name="line">The line counter to advance.</param>
    /// <param name="column">The column counter to advance.</param>
    public static void Advance(ReadOnlySpan<char> text, ref long line, ref long column)
    {
        foreach (char c in text)
        {
            Advance(c, ref line, ref column);
        }
    }

    /// <summary>Advances a line and column over one character.</summary>
    /// <param name="value">The character passing through the port.</param>
    /// <param name="line">The line counter to advance.</param>
    /// <param name="column">The column counter to advance.</param>
    public static void Advance(char value, ref long line, ref long column)
    {
        switch (value)
        {
            case '\n':
                line++;
                column = 0;
                break;
            case '\r':
                column = 0;
                break;
            case '\t':
                column = column + TabWidth - (column % TabWidth);
                break;
            case '\b':
                if (column > 0)
                {
                    column--;
                }

                break;
            case '\a':
                break;
            default:
                // The trailing half of a surrogate pair is the same code point as the
                // leading half and must not advance the column a second time.
                if (!char.IsLowSurrogate(value))
                {
                    column++;
                }

                break;
        }
    }

    /// <summary>
    /// Retreats a line and column over one character that is being PUT BACK, as
    /// <c>unread-char</c> does.
    /// </summary>
    /// <param name="value">The character being pushed back.</param>
    /// <param name="line">The line counter to retreat.</param>
    /// <param name="column">The column counter to retreat.</param>
    /// <remarks>
    /// Retreating is NOT the inverse of <see cref="Advance(char, ref long, ref long)"/>
    /// and must not be written as one — MEASURED on the oracle: unreading a newline takes
    /// the LINE back one and leaves the column where it is (a port cannot know how long
    /// the previous line was), and unreading a TAB simply decrements the column, so a port
    /// at column 8 because of a tab reads 7 afterwards rather than returning to where the
    /// tab began. Both counters stop at zero.
    /// </remarks>
    public static void Retreat(char value, ref long line, ref long column)
    {
        if (value == '\n')
        {
            if (line > 0)
            {
                line--;
            }

            return;
        }

        if (!char.IsLowSurrogate(value) && column > 0)
        {
            column--;
        }
    }
}
