// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Reader;

/// <summary>
/// The Scheme reader. Turns source text into Scheme data, following Guile's dialect —
/// which is R7RS plus keywords (<c>#:foo</c>), block and datum comments, and square
/// brackets as parentheses.
/// </summary>
public sealed class SchemeReader
{
    private readonly string _text;
    private readonly string _fileName;
    private int _position;
    private int _line;
    private int _column;

    /// <summary>Initializes a reader over source text.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="fileName">A name used in error messages and source locations.</param>
    public SchemeReader(string text, string fileName)
    {
        _text = text ?? string.Empty;
        _fileName = fileName ?? "<unknown>";
        _position = 0;
        _line = 1;
        _column = 0;
    }

    private static readonly Dictionary<char, HashExtension> HashExtensions
        = new Dictionary<char, HashExtension>();

    /// <summary>
    /// Reads one datum introduced by a <c>#</c> dispatch character the core reader does
    /// not handle itself.
    /// </summary>
    /// <param name="reader">The reader, positioned ON the dispatch character.</param>
    /// <returns>The datum read.</returns>
    public delegate object HashExtension(SchemeReader reader);

    /// <summary>
    /// Registers a reader extension for a <c>#</c> dispatch character, the way Guile's
    /// <c>read-hash-extend</c> does.
    /// <para>
    /// A registered handler takes precedence over the built-in syntax for that character.
    /// That is deliberate and matches Guile: LilyPond registers <c>#{</c> for embedded
    /// music, which has to win over Guile's <c>#{extended symbol}#</c>.
    /// </para>
    /// </summary>
    /// <param name="dispatchCharacter">The character following <c>#</c>.</param>
    /// <param name="extension">The handler, or null to remove one.</param>
    public static void RegisterHashExtension(char dispatchCharacter, HashExtension extension)
    {
        if (extension == null)
        {
            HashExtensions.Remove(dispatchCharacter);
            return;
        }

        HashExtensions[dispatchCharacter] = extension;
    }

    /// <summary>
    /// Removes every registered extension and returns what was removed, so the caller can
    /// put it back.
    /// <para>
    /// The bootstrap needs this. Guile's own <c>psyntax-pp.scm</c> contains extended
    /// symbols such as <c>#{ $sc-ellipsis }#</c>, so if a host has registered a handler
    /// for <c>{</c> -- LilyPond registers one for embedded music -- reading Guile's source
    /// with it active corrupts the expander before anything else can go wrong.
    /// </para>
    /// </summary>
    /// <returns>The extensions that were registered.</returns>
    public static IReadOnlyDictionary<char, HashExtension> SuspendHashExtensions()
    {
        Dictionary<char, HashExtension> saved = new Dictionary<char, HashExtension>(HashExtensions);
        HashExtensions.Clear();
        return saved;
    }

    /// <summary>Restores extensions removed by <see cref="SuspendHashExtensions"/>.</summary>
    /// <param name="extensions">The extensions to reinstate.</param>
    public static void RestoreHashExtensions(IReadOnlyDictionary<char, HashExtension> extensions)
    {
        if (extensions == null)
        {
            return;
        }

        foreach (KeyValuePair<char, HashExtension> entry in extensions)
        {
            HashExtensions[entry.Key] = entry.Value;
        }
    }

    /// <summary>Gets a value indicating whether the reader is at end of input.</summary>
    public bool IsAtEnd => AtEnd;

    /// <summary>Gets the current read position.</summary>
    public int Position => _position;

    /// <summary>Gets the source text being read.</summary>
    public string SourceText => _text;

    /// <summary>Gets the file name used in error messages.</summary>
    public string SourceFileName => _fileName;

    /// <summary>Gets the current line number, counting from one.</summary>
    public int CurrentLine => _line;

    /// <summary>Returns the character at the read position without consuming it.</summary>
    /// <returns>The character.</returns>
    public char PeekCharacter() => Peek();

    /// <summary>Returns a character ahead of the read position without consuming it.</summary>
    /// <param name="offset">How far ahead to look.</param>
    /// <returns>The character, or NUL past the end of input.</returns>
    public char PeekCharacter(int offset) => PeekAt(offset);

    /// <summary>Consumes and returns the character at the read position.</summary>
    /// <returns>The character.</returns>
    public char ReadCharacterRaw()
    {
        char value = Peek();
        Advance();
        return value;
    }

    /// <summary>Reads one complete datum, raising when input runs out first.</summary>
    /// <returns>The datum.</returns>
    public object ReadDatum() => ReadRequired();

    /// <summary>Reads every datum in the source.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="fileName">A name used in error messages.</param>
    /// <returns>The data, in source order.</returns>
    public static List<object> ReadAll(string text, string fileName)
    {
        SchemeReader reader = new SchemeReader(text, fileName);
        List<object> forms = new List<object>();
        while (true)
        {
            object form = reader.Read();
            if (ReferenceEquals(form, EofObject.Instance))
            {
                break;
            }

            forms.Add(form);
        }

        return forms;
    }

    /// <summary>Reads the next datum.</summary>
    /// <returns>The datum, or <see cref="EofObject.Instance"/> when input is exhausted.</returns>
    public object Read()
    {
        SkipAtmosphere();
        if (AtEnd)
        {
            return EofObject.Instance;
        }

        // The position is captured AFTER the atmosphere and BEFORE the datum, so a form
        // is located at its own opening character rather than at the whitespace or
        // comment in front of it. Guile records the same point, which is what makes
        // ",(lambda (m) ...)" locate the lambda one column right of the unquote.
        int line = _line;
        int column = _column;
        object datum = ReadDatumAtPosition();
        if (SourceProperties.Supports(datum))
        {
            SourceProperties.Record(datum, new SourceLocation(_fileName, line - 1, column));
        }

        return datum;
    }

    private object ReadDatumAtPosition()
    {
        char c = Peek();
        switch (c)
        {
            case '(':
            case '[':
                Advance();
                return ReadListTail(c == '(' ? ')' : ']');

            case ')':
            case ']':
                throw Error("unexpected close paren");

            case '\'':
                Advance();
                return Pair.List(Symbol.Quote, ReadRequired());

            case '`':
                Advance();
                return Pair.List(Symbol.Quasiquote, ReadRequired());

            case ',':
                Advance();
                if (!AtEnd && Peek() == '@')
                {
                    Advance();
                    return Pair.List(Symbol.UnquoteSplicing, ReadRequired());
                }

                return Pair.List(Symbol.Unquote, ReadRequired());

            case '"':
                return ReadString();

            case '#':
                return ReadHash();

            default:
                return ReadAtom();
        }
    }

    private bool AtEnd => _position >= _text.Length;

    private char Peek() => _text[_position];

    private char PeekAt(int offset)
        => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private char Advance()
    {
        char c = _text[_position++];
        if (c == '\n')
        {
            _line++;
            _column = 0;
        }
        else
        {
            _column++;
        }

        return c;
    }

    private object ReadRequired()
    {
        object form = Read();
        if (ReferenceEquals(form, EofObject.Instance))
        {
            throw Error("unexpected end of input");
        }

        return form;
    }

    private void SkipAtmosphere()
    {
        while (!AtEnd)
        {
            char c = Peek();
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }

            if (c == ';')
            {
                while (!AtEnd && Peek() != '\n')
                {
                    Advance();
                }

                continue;
            }

            if (c == '#' && PeekAt(1) == '|')
            {
                SkipBlockComment();
                continue;
            }

            if (c == '#' && PeekAt(1) == ';')
            {
                Advance();
                Advance();
                ReadRequired();
                continue;
            }

            return;
        }
    }

    private void SkipBlockComment()
    {
        Advance();
        Advance();
        int depth = 1;
        while (!AtEnd && depth > 0)
        {
            if (Peek() == '#' && PeekAt(1) == '|')
            {
                Advance();
                Advance();
                depth++;
            }
            else if (Peek() == '|' && PeekAt(1) == '#')
            {
                Advance();
                Advance();
                depth--;
            }
            else
            {
                Advance();
            }
        }
    }

    private object ReadListTail(char closer)
    {
        List<object> items = new List<object>();
        object tail = Nil.Instance;

        while (true)
        {
            SkipAtmosphere();
            if (AtEnd)
            {
                throw Error("unterminated list");
            }

            char c = Peek();
            if (c == ')' || c == ']')
            {
                Advance();
                break;
            }

            // A dot introduces an improper tail, but only when it stands alone —
            // ".5" and "..." are ordinary atoms.
            if (c == '.' && IsDelimiter(PeekAt(1)))
            {
                Advance();
                tail = ReadRequired();
                SkipAtmosphere();
                if (AtEnd || (Peek() != ')' && Peek() != ']'))
                {
                    throw Error("bad dotted list");
                }

                Advance();
                break;
            }

            items.Add(ReadRequired());
        }

        object result = tail;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            result = new Pair(items[i], result);
        }

        return result;
    }

    private static bool IsDelimiter(char c)
        => c == '\0' || char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '[' || c == ']'
           || c == '"' || c == ';';

    private object ReadString()
    {
        Advance();
        StringBuilder builder = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("unterminated string");
            }

            char c = Advance();
            if (c == '"')
            {
                break;
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (AtEnd)
            {
                throw Error("unterminated string escape");
            }

            char escape = Advance();
            switch (escape)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'v': builder.Append('\v'); break;
                case '0': builder.Append('\0'); break;
                case '\\': builder.Append('\\'); break;
                case '"': builder.Append('"'); break;
                case 'x':
                {
                    StringBuilder hex = new StringBuilder();
                    while (!AtEnd && Peek() != ';')
                    {
                        hex.Append(Advance());
                    }

                    if (!AtEnd)
                    {
                        Advance();
                    }

                    builder.Append(char.ConvertFromUtf32(
                        int.Parse(hex.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    break;
                }

                // Guile's fixed-width hex escapes: \uXXXX takes EXACTLY four hex digits
                // and \UXXXXXX exactly six, with no terminator — libguile/read.c's
                // SCM_READ_HEX_ESCAPE(4, '\0') and (6, '\0'). LilyPond's
                // define-markup-commands.scm spells the tied-lyric undertie "‿",
                // and a reader without this case appended the letter u and the digits
                // literally, with no diagnostic.
                case 'u':
                    builder.Append(char.ConvertFromUtf32(ReadFixedHexEscape(4)));
                    break;

                case 'U':
                    builder.Append(char.ConvertFromUtf32(ReadFixedHexEscape(6)));
                    break;

                case '\n':
                    // A backslash-newline continues the string, skipping leading blanks
                    // on the next line.
                    while (!AtEnd && (Peek() == ' ' || Peek() == '\t'))
                    {
                        Advance();
                    }

                    break;

                default:
                    builder.Append(escape);
                    break;
            }
        }

        return new MutableString(builder.ToString());
    }

    private int ReadFixedHexEscape(int digits)
    {
        int code = 0;
        for (int i = 0; i < digits; i++)
        {
            if (AtEnd)
            {
                throw Error("unterminated string escape");
            }

            char a = Advance();
            int nibble;
            if (a >= '0' && a <= '9')
            {
                nibble = a - '0';
            }
            else if (a >= 'A' && a <= 'F')
            {
                nibble = a - 'A' + 10;
            }
            else if (a >= 'a' && a <= 'f')
            {
                nibble = a - 'a' + 10;
            }
            else
            {
                // Guile raises here rather than stopping short: an escape of the
                // wrong width is a reader error, not a shorter character.
                throw Error("invalid character in escape sequence: " + a);
            }

            code = (code * 16) + nibble;
        }

        return code;
    }

    private object ReadHash()
    {
        Advance();
        if (AtEnd)
        {
            throw Error("unexpected end of input after '#'");
        }

        char c = Peek();

        // A registered extension wins over the built-in syntax; see RegisterHashExtension.
        if (HashExtensions.TryGetValue(c, out HashExtension extension))
        {
            return extension(this);
        }

        switch (c)
        {
            case '(':
            {
                Advance();
                object list = ReadListTail(')');
                return Pair.ToList(list).ToArray();
            }

            case 'n':
            {
                string nilToken = ReadToken();
                if (nilToken == "nil")
                {
                    return ElispNil.Instance;
                }

                throw Error("unknown '#" + nilToken + "' syntax");
            }

            case 't':
            case 'T':
            case 'f':
            case 'F':
            {
                // Guile's scm_read_boolean (libguile/read.c): the dispatch character
                // alone decides the value, and the long spelling is consumed only as
                // an all-or-nothing case-insensitive match -- never a token read to a
                // delimiter. `#t}` is therefore #t with the '}' left unread, which
                // LilyPond's `##t}` inside a one-line \layout block depends on.
                bool value = c == 't' || c == 'T';
                Advance();
                if (value)
                {
                    TryReadCiChars("rue");
                    return true;
                }

                if (!TryReadCiChars("alse") && c == 'f' && !AtEnd && char.IsDigit(Peek()))
                {
                    // Guile's lowercase-f dispatch tries an SRFI-4 float vector
                    // (#f32(...), #f64(...)) before settling on the boolean. Those
                    // vectors do not exist here, so a digit after #f is refused
                    // loudly rather than silently read as #f.
                    throw Error("unsupported SRFI-4 vector literal #f" + Peek());
                }

                return false;
            }

            case '\\':
                Advance();
                return ReadCharacter();

            case ':':
                Advance();
                return Keyword.Get(ReadToken());

            case '\'':
                // #'x is (syntax x) -- the syntax-case analogue of quote.
                Advance();
                return Pair.List(Symbol.Intern("syntax"), ReadRequired());

            case '`':
                Advance();
                return Pair.List(Symbol.Intern("quasisyntax"), ReadRequired());

            case ',':
            {
                Advance();
                if (!AtEnd && Peek() == '@')
                {
                    Advance();
                    return Pair.List(Symbol.Intern("unsyntax-splicing"), ReadRequired());
                }

                return Pair.List(Symbol.Intern("unsyntax"), ReadRequired());
            }

            case '{':
                // Guile's extended symbol syntax: #{name with spaces}# lets a symbol
                // contain characters the ordinary reader would treat as delimiters.
                // psyntax uses it for #{1+}# and #{ $sc-ellipsis }#.
                return ReadExtendedSymbol();

            case 'v':
            {
                // #vu8(...) bytevector
                string token = ReadToken();
                if (token != "vu8")
                {
                    throw Error("unknown '#" + token + "' syntax");
                }

                SkipAtmosphere();
                if (AtEnd || Peek() != '(')
                {
                    throw Error("expected '(' after #vu8");
                }

                Advance();
                List<object> items = Pair.ToList(ReadListTail(')'));
                byte[] bytes = new byte[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    bytes[i] = (byte)Convert.ToInt64(items[i], CultureInfo.InvariantCulture);
                }

                return bytes;
            }

            case 'x':
            case 'X':
            case 'o':
            case 'O':
            case 'b':
            case 'B':
            case 'd':
            case 'D':
            case 'e':
            case 'E':
            case 'i':
            case 'I':
            {
                string token = "#" + ReadToken();
                object number = ParseNumber(token);
                if (number == null)
                {
                    throw Error("bad number literal " + token);
                }

                return number;
            }

            case '0':
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
                return ReadArrayLiteral();

            default:
                throw Error("unsupported '#" + c + "' syntax");
        }
    }

    private object ReadArrayLiteral()
    {
        // Guile array literals — "Array Syntax" in the manual: #<rank>[@<lower>]…(…),
        // e.g. #1@1(17 32 53) for a one-dimensional array indexed from 1, or
        // #2((a b) (c d)) for a matrix. LilyPond's qr-code.scm tables use these.
        int rank = 0;
        while (!AtEnd && Peek() >= '0' && Peek() <= '9')
        {
            rank = (rank * 10) + (Peek() - '0');
            Advance();
        }

        if (rank <= 0)
        {
            throw Error("array literal needs a positive rank");
        }

        int[] lowerBounds = new int[rank];
        int dimension = 0;
        while (!AtEnd && Peek() == '@')
        {
            Advance();
            bool negative = false;
            if (!AtEnd && Peek() == '-')
            {
                negative = true;
                Advance();
            }

            int value = 0;
            bool sawDigit = false;
            while (!AtEnd && Peek() >= '0' && Peek() <= '9')
            {
                value = (value * 10) + (Peek() - '0');
                Advance();
                sawDigit = true;
            }

            if (!sawDigit || dimension >= rank)
            {
                throw Error("bad lower bound in array literal");
            }

            lowerBounds[dimension++] = negative ? -value : value;
        }

        if (AtEnd || Peek() != '(')
        {
            throw Error("expected '(' in array literal");
        }

        Advance();
        object nested = ReadListTail(')');

        int[] lengths = new int[rank];
        bool[] measured = new bool[rank];
        List<object> flat = new List<object>();
        FlattenArrayLiteral(nested, 0, rank, lengths, measured, flat);
        return new SchemeArray(lowerBounds, lengths, flat.ToArray());
    }

    private void FlattenArrayLiteral(
        object level,
        int dimension,
        int rank,
        int[] lengths,
        bool[] measured,
        List<object> flat)
    {
        List<object> items = Pair.ToList(level);
        if (!measured[dimension])
        {
            lengths[dimension] = items.Count;
            measured[dimension] = true;
        }
        else if (lengths[dimension] != items.Count)
        {
            throw Error("ragged array literal");
        }

        if (dimension == rank - 1)
        {
            flat.AddRange(items);
            return;
        }

        foreach (object item in items)
        {
            FlattenArrayLiteral(item, dimension + 1, rank, lengths, measured, flat);
        }
    }

    private object ReadExtendedSymbol()
    {
        Advance();
        StringBuilder builder = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("unterminated #{...}# symbol");
            }

            if (Peek() == '}' && PeekAt(1) == '#')
            {
                Advance();
                Advance();
                break;
            }

            char c = Advance();
            if (c == '\\' && !AtEnd && Peek() == 'x')
            {
                Advance();
                StringBuilder hex = new StringBuilder();
                while (!AtEnd && Peek() != ';')
                {
                    hex.Append(Advance());
                }

                if (!AtEnd)
                {
                    Advance();
                }

                builder.Append(char.ConvertFromUtf32(
                    int.Parse(hex.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                continue;
            }

            builder.Append(c);
        }

        return Symbol.Intern(builder.ToString());
    }

    private object ReadCharacter()
    {
        if (AtEnd)
        {
            throw Error("unexpected end of input in character literal");
        }

        // The first character is taken literally, so #\( and #\space both work.
        char first = Advance();
        StringBuilder builder = new StringBuilder();
        builder.Append(first);
        while (!AtEnd && !IsDelimiter(Peek()))
        {
            builder.Append(Advance());
        }

        string name = builder.ToString();
        if (name.Length == 1)
        {
            return SchemeChar.Get(name[0]);
        }

        switch (name.ToLowerInvariant())
        {
            case "space": return SchemeChar.Get(' ');
            case "newline": case "nl": case "linefeed": return SchemeChar.Get('\n');
            case "tab": return SchemeChar.Get('\t');
            case "return": return SchemeChar.Get('\r');
            case "null": case "nul": return SchemeChar.Get(0);
            case "alarm": return SchemeChar.Get(7);
            case "backspace": return SchemeChar.Get(8);
            case "delete": case "del": case "rubout": return SchemeChar.Get(127);
            case "escape": case "esc": return SchemeChar.Get(27);
            case "page": return SchemeChar.Get(12);
            default:
                if ((name[0] == 'x' || name[0] == 'X' || name[0] == 'u' || name[0] == 'U') && name.Length > 1)
                {
                    return SchemeChar.Get(int.Parse(
                        name.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }

                return SchemeChar.Get(char.ConvertToUtf32(name, 0));
        }
    }

    private string ReadToken()
    {
        StringBuilder builder = new StringBuilder();
        while (!AtEnd && !IsDelimiter(Peek()) && Peek() != '\'')
        {
            builder.Append(Advance());
        }

        return builder.ToString();
    }

    private bool TryReadCiChars(string expected)
    {
        // Guile's try_read_ci_chars (libguile/read.c): consume the expected
        // characters only if every one of them matches case-insensitively;
        // on any mismatch consume nothing at all.
        int position = _position;
        int line = _line;
        int column = _column;
        foreach (char expectedChar in expected)
        {
            if (AtEnd || char.ToLowerInvariant(Peek()) != expectedChar)
            {
                _position = position;
                _line = line;
                _column = column;
                return false;
            }

            Advance();
        }

        return true;
    }

    private object ReadAtom()
    {
        if (Peek() == '|')
        {
            Advance();
            StringBuilder quoted = new StringBuilder();
            while (!AtEnd && Peek() != '|')
            {
                quoted.Append(Advance());
            }

            if (!AtEnd)
            {
                Advance();
            }

            return Symbol.Intern(quoted.ToString());
        }

        string token = ReadToken();
        if (token.Length == 0)
        {
            throw Error("empty token");
        }

        object number = ParseNumber(token);
        return number ?? Symbol.Intern(token);
    }

    /// <summary>
    /// Attempts to parse a token as a number, returning <see langword="null"/> when the
    /// token is a symbol instead. Handles radix and exactness prefixes, rationals, and
    /// the usual integer and real syntaxes.
    /// </summary>
    /// <param name="token">The token text.</param>
    /// <returns>The parsed number, or <see langword="null"/>.</returns>
    public static object ParseNumber(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        int radix = 10;
        bool forceExact = false;
        bool forceInexact = false;
        int index = 0;

        while (index + 1 < token.Length && token[index] == '#')
        {
            char prefix = char.ToLowerInvariant(token[index + 1]);
            switch (prefix)
            {
                case 'x': radix = 16; break;
                case 'o': radix = 8; break;
                case 'b': radix = 2; break;
                case 'd': radix = 10; break;
                case 'e': forceExact = true; break;
                case 'i': forceInexact = true; break;
                default: return null;
            }

            index += 2;
        }

        string body = token.Substring(index);
        if (body.Length == 0)
        {
            return null;
        }

        // A real first: `1e5' and `-inf.0' must not be mistaken for the rectangular form,
        // and every ordinary number in the suite takes this branch.
        object value = ParseReal(body, radix) ?? ParseComplex(body, radix);
        if (value == null)
        {
            return null;
        }

        if (forceInexact)
        {
            return Numeric.SchemeNumber.ToInexact(value);
        }

        if (forceExact)
        {
            return Numeric.SchemeNumber.ToExact(value);
        }

        return value;
    }

    /// <summary>
    /// Reads Guile's rectangular and polar complex literals — <c>1+0i</c>, <c>0+1i</c>,
    /// <c>-1-0.25i</c>, <c>+i</c>, <c>2@1.57</c>.
    /// <para>
    /// AN EXACT ZERO IMAGINARY PART COLLAPSES TO THE REAL, which is Guile's own
    /// normalization and not a shortcut: <c>1+0i</c> IS the exact integer 1 there, while
    /// <c>1.0+0.0i</c> stays complex. <c>scm/stencil.scm</c>'s arrow maker opens by
    /// binding <c>e_x</c> to <c>1+0i</c> and multiplying by it, so a version that kept
    /// the zero would turn every arrow coordinate inexact.
    /// </para>
    /// </summary>
    /// <param name="body">The token text, after any radix or exactness prefix.</param>
    /// <param name="radix">The radix in force.</param>
    /// <returns>The number, or <see langword="null"/> when this is not a complex literal.</returns>
    private static object ParseComplex(string body, int radix)
    {
        // Both sides must EXIST before either is parsed: psyntax's source contains symbols
        // that end in '@', and handing ParseReal the empty string indexes past its end.
        int at = body.IndexOf('@');
        if (at > 0 && at + 1 < body.Length)
        {
            object magnitude = ParseReal(body.Substring(0, at), radix);
            object angle = ParseReal(body.Substring(at + 1), radix);
            if (magnitude == null || angle == null)
            {
                return null;
            }

            double m = Numeric.SchemeNumber.ToDouble(magnitude);
            double a = Numeric.SchemeNumber.ToDouble(angle);
            return new Numeric.ComplexNumber(m * Math.Cos(a), m * Math.Sin(a));
        }

        if (body.Length < 2 || (body[body.Length - 1] != 'i' && body[body.Length - 1] != 'I'))
        {
            return null;
        }

        string rectangular = body.Substring(0, body.Length - 1);

        // The sign that opens the IMAGINARY part is the last one that is neither the
        // token's own leading sign nor an exponent's — `1e-5+2i' splits at the '+', and
        // `-1-0.25i' at the second '-'.
        int split = -1;
        for (int i = rectangular.Length - 1; i > 0; i--)
        {
            char c = rectangular[i];
            if ((c == '+' || c == '-') && !IsExponentMarker(rectangular[i - 1]))
            {
                split = i;
                break;
            }
        }

        string realText = split < 0 ? "0" : rectangular.Substring(0, split);
        string imaginaryText = split < 0 ? rectangular : rectangular.Substring(split);

        // `+i' and `-i' name the unit imaginary; the sign alone is the whole part.
        if (imaginaryText == "+" || imaginaryText == "-")
        {
            imaginaryText += "1";
        }

        if (imaginaryText.Length == 0
            || (imaginaryText[0] != '+' && imaginaryText[0] != '-'))
        {
            return null;
        }

        object realPart = realText.Length == 0 ? 0L : ParseReal(realText, radix);
        object imaginaryPart = ParseReal(imaginaryText, radix);
        if (realPart == null || imaginaryPart == null)
        {
            return null;
        }

        if (Numeric.SchemeNumber.IsExact(imaginaryPart)
            && Numeric.SchemeNumber.IsZero(imaginaryPart))
        {
            return realPart;
        }

        return new Numeric.ComplexNumber(
            Numeric.SchemeNumber.ToDouble(realPart),
            Numeric.SchemeNumber.ToDouble(imaginaryPart));
    }

    private static bool IsExponentMarker(char c)
        => c == 'e' || c == 'E' || c == 's' || c == 'S' || c == 'f' || c == 'F'
           || c == 'd' || c == 'D' || c == 'l' || c == 'L';

    private static object ParseReal(string body, int radix)
    {
        // Callers used to guarantee a non-empty body; ParseComplex splits a token and can
        // hand over an empty side, so the guard belongs here rather than at each call.
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        // R7RS names the non-finite reals literally. LilyPond writes +inf.0 for an
        // unbounded extent, so failing to read it turns a legitimate number into an
        // unbound-variable error a long way from the actual cause.
        switch (body)
        {
            case "+inf.0":
                return double.PositiveInfinity;
            case "-inf.0":
                return double.NegativeInfinity;
            case "+nan.0":
            case "-nan.0":
                return double.NaN;
        }

        int slash = body.IndexOf('/');
        if (slash > 0)
        {
            object numerator = ParseInteger(body.Substring(0, slash), radix);
            object denominator = ParseInteger(body.Substring(slash + 1), radix);
            if (numerator == null || denominator == null)
            {
                return null;
            }

            return Numeric.SchemeNumber.MakeRatio(numerator, denominator);
        }

        object integer = ParseInteger(body, radix);
        if (integer != null)
        {
            return integer;
        }

        if (radix != 10)
        {
            return null;
        }

        // Reject bare signs and symbols like "..." or "+" that double.TryParse would
        // otherwise mis-handle, then let the CLR parse the usual decimal syntaxes.
        if (body == "+" || body == "-" || body == "..." || body == ".")
        {
            return null;
        }

        bool looksNumeric = char.IsDigit(body[0])
                            || ((body[0] == '+' || body[0] == '-' || body[0] == '.') && body.Length > 1);
        if (!looksNumeric)
        {
            return null;
        }

        if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
        {
            return real;
        }

        return null;
    }

    private static object ParseInteger(string text, int radix)
    {
        if (text.Length == 0)
        {
            return null;
        }

        bool negative = false;
        int start = 0;
        if (text[0] == '+' || text[0] == '-')
        {
            negative = text[0] == '-';
            start = 1;
            if (text.Length == 1)
            {
                return null;
            }
        }

        BigInteger accumulator = BigInteger.Zero;
        for (int i = start; i < text.Length; i++)
        {
            int digit = DigitValue(text[i]);
            if (digit < 0 || digit >= radix)
            {
                return null;
            }

            accumulator = (accumulator * radix) + digit;
        }

        if (negative)
        {
            accumulator = -accumulator;
        }

        return Numeric.SchemeNumber.Normalize(accumulator);
    }

    private static int DigitValue(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'z')
        {
            return (c - 'a') + 10;
        }

        if (c >= 'A' && c <= 'Z')
        {
            return (c - 'A') + 10;
        }

        return -1;
    }

    private Exception Error(string message)
        => new SchemeReaderException(
            _fileName + ":" + _line.ToString(CultureInfo.InvariantCulture) + ":"
            + _column.ToString(CultureInfo.InvariantCulture) + ": " + message);
}

/// <summary>Raised when source text cannot be read as Scheme data.</summary>
public sealed class SchemeReaderException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">A message including the source position.</param>
    public SchemeReaderException(string message)
        : base(message)
    {
    }
}
