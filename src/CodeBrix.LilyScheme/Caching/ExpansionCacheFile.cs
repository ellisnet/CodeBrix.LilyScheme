// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Caching;

/// <summary>
/// Binary serialization for <see cref="ExpansionCache"/> contents. The format is a
/// tagged value graph with a shared object table: every heap object is registered at
/// first encounter and every later occurrence is written as a back-reference, which
/// preserves object identity — load-bearing for gensyms, whose lookup is reference
/// equality — and handles shared structure and cycles.
/// </summary>
/// <remarks>
/// The value universe is the CLOSED set observed in expanded Tree-IL (measured over a
/// full engine boot): the eighteen expanded-vtable structs, pairs, symbols, keywords,
/// mutable strings, characters, vectors, object arrays, and the numeric tower.
/// <see cref="Write"/> throws <see cref="NotSupportedException"/> on anything else —
/// the caller treats that as "this boot is not cacheable" and carries on live, so an
/// unexpected type can never produce a wrong cache, only a missing one.
/// </remarks>
public static class ExpansionCacheFile
{
    private const uint Magic = 0x4C535843; // "LSXC"
    private const byte FormatVersion = 1;

    private const byte TagRef = 0;
    private const byte TagNull = 1;
    private const byte TagFalse = 2;
    private const byte TagTrue = 3;
    private const byte TagNil = 4;
    private const byte TagInt64 = 5;
    private const byte TagDouble = 6;
    private const byte TagBigInteger = 7;
    private const byte TagRatio = 8;
    private const byte TagComplex = 9;
    private const byte TagChar = 10;
    private const byte TagInternedSymbol = 11;
    private const byte TagUninternedSymbol = 12;
    private const byte TagKeyword = 13;
    private const byte TagMutableString = 14;
    private const byte TagPairChain = 15;
    private const byte TagStruct = 16;
    private const byte TagObjectArray = 17;
    private const byte TagVector = 18;
    private const byte TagUnspecified = 19;
    private const byte TagSyntaxObject = 20;

    /// <summary>Writes a cache to a stream.</summary>
    /// <param name="cache">The cache to serialize.</param>
    /// <param name="stream">The destination stream.</param>
    /// <param name="key">The caller's world signature, verified on read.</param>
    public static void Write(ExpansionCache cache, Stream stream, string key)
    {
        if (cache == null)
        {
            throw new ArgumentNullException(nameof(cache));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        // The payload is built in memory first so its SHA-256 can sit in the header:
        // a cache that could load corrupted would replay a silently WRONG boot, so
        // integrity is verified before a single value is parsed.
        byte[] payload;
        using (MemoryStream buffer = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                List<ExpansionCache.RecordedFile> files = new List<ExpansionCache.RecordedFile>(cache.Files);
                writer.Write(files.Count);

                Dictionary<object, int> table = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                foreach (ExpansionCache.RecordedFile file in files)
                {
                    writer.Write(file.FileName);
                    writer.Write(file.SourceHash);
                    writer.Write(file.Forms.Count);
                    foreach (object form in file.Forms)
                    {
                        WriteValue(writer, form, table);
                    }
                }
            }

            payload = buffer.ToArray();
        }

        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(key);
            writer.Write(System.Security.Cryptography.SHA256.HashData(payload));
            writer.Write(payload.Length);
            writer.Write(payload);
        }
    }

    /// <summary>
    /// Reads a cache from a stream. Answers null — never a partial cache — when the
    /// magic, format version or key does not match; corrupt content throws instead and
    /// belongs behind <see cref="TryReadFile"/>.
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="expectedKey">The caller's current world signature.</param>
    /// <returns>The cache, or null when the stream is for a different world.</returns>
    public static ExpansionCache Read(Stream stream, string expectedKey)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (expectedKey == null)
        {
            throw new ArgumentNullException(nameof(expectedKey));
        }

        byte[] payload;
        using (BinaryReader header = new BinaryReader(stream, Encoding.UTF8, true))
        {
            if (header.ReadUInt32() != Magic || header.ReadByte() != FormatVersion)
            {
                return null;
            }

            if (!string.Equals(header.ReadString(), expectedKey, StringComparison.Ordinal))
            {
                return null;
            }

            byte[] digest = header.ReadBytes(32);
            int length = header.ReadInt32();
            payload = header.ReadBytes(length);
            if (payload.Length != length
                || !System.Security.Cryptography.SHA256.HashData(payload).AsSpan().SequenceEqual(digest))
            {
                // Truncated or corrupted content is a MISS, detected before any value
                // is parsed — a wrong cache must never be able to become a wrong boot.
                return null;
            }
        }

        using (MemoryStream buffer = new MemoryStream(payload, false))
        using (BinaryReader reader = new BinaryReader(buffer, Encoding.UTF8, false))
        {
            ExpansionCache cache = new ExpansionCache();
            int fileCount = reader.ReadInt32();
            List<object> table = new List<object>();
            for (int i = 0; i < fileCount; i++)
            {
                string fileName = reader.ReadString();
                string sourceHash = reader.ReadString();
                int formCount = reader.ReadInt32();
                List<object> forms = new List<object>(formCount);
                for (int j = 0; j < formCount; j++)
                {
                    forms.Add(ReadValue(reader, table));
                }

                cache.RecordFile(fileName, sourceHash, forms);
            }

            cache.MarkClean();
            cache.IsReplay = true;
            return cache;
        }
    }

    /// <summary>
    /// Writes a cache to a file atomically: to a temporary sibling first, then a rename,
    /// so a concurrent reader sees either the old file or the new one, never a torn one.
    /// </summary>
    /// <param name="cache">The cache to save.</param>
    /// <param name="path">The destination path; its directory is created if absent.</param>
    /// <param name="key">The caller's world signature.</param>
    public static void WriteFile(ExpansionCache cache, string path, string key)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                Write(cache, stream, key);
            }

            File.Move(temp, path, true);
            cache.MarkClean();
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // A leftover temp file is harmless; the unique name never collides.
                }
            }
        }
    }

    /// <summary>
    /// Reads a cache file, answering null on ANY failure — absent file, wrong key,
    /// truncated or corrupt content. A cache must never be able to fail a boot; the
    /// caller falls back to loading live and rewriting the file.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="expectedKey">The caller's current world signature.</param>
    /// <returns>The cache, or null.</returns>
    public static ExpansionCache TryReadFile(string path, string expectedKey)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return Read(stream, expectedKey);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteValue(BinaryWriter writer, object value, Dictionary<object, int> table)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (value == null)
        {
            writer.Write(TagNull);
            return;
        }

        if (value is bool flag)
        {
            writer.Write(flag ? TagTrue : TagFalse);
            return;
        }

        if (value is Nil)
        {
            writer.Write(TagNil);
            return;
        }

        if (value is Unspecified)
        {
            writer.Write(TagUnspecified);
            return;
        }

        if (value is long fixnum)
        {
            writer.Write(TagInt64);
            writer.Write7BitEncodedInt64(fixnum);
            return;
        }

        if (value is double real)
        {
            writer.Write(TagDouble);
            writer.Write(real);
            return;
        }

        if (value is BigInteger bignum)
        {
            writer.Write(TagBigInteger);
            WriteBigInteger(writer, bignum);
            return;
        }

        if (value is Ratio ratio)
        {
            writer.Write(TagRatio);
            WriteBigInteger(writer, ratio.Numerator);
            WriteBigInteger(writer, ratio.Denominator);
            return;
        }

        if (value is ComplexNumber complex)
        {
            writer.Write(TagComplex);
            writer.Write(complex.Real);
            writer.Write(complex.Imaginary);
            return;
        }

        if (value is SchemeChar character)
        {
            writer.Write(TagChar);
            writer.Write7BitEncodedInt(character.CodePoint);
            return;
        }

        int id;
        if (table.TryGetValue(value, out id))
        {
            writer.Write(TagRef);
            writer.Write7BitEncodedInt(id);
            return;
        }

        if (value is Symbol symbol)
        {
            writer.Write(symbol.IsUninterned ? TagUninternedSymbol : TagInternedSymbol);
            table.Add(symbol, table.Count);
            writer.Write(symbol.Name);
            return;
        }

        if (value is Keyword keyword)
        {
            writer.Write(TagKeyword);
            table.Add(keyword, table.Count);
            writer.Write(keyword.Name.Name);
            return;
        }

        if (value is MutableString text)
        {
            writer.Write(TagMutableString);
            table.Add(text, table.Count);
            writer.Write(text.ToString());
            return;
        }

        if (value is Pair head)
        {
            // A cdr-linked run is written as one chain: N pairs are registered BEFORE
            // any car is written, so cars and the tail may reference pairs of the chain
            // itself (shared tails and cycles both round-trip).
            writer.Write(TagPairChain);
            List<Pair> chain = new List<Pair>();
            object cursor = head;
            while (cursor is Pair pair && !table.ContainsKey(pair))
            {
                table.Add(pair, table.Count);
                chain.Add(pair);
                cursor = pair.Cdr;
            }

            writer.Write7BitEncodedInt(chain.Count);
            for (int i = 0; i < chain.Count; i++)
            {
                WriteValue(writer, chain[i].Car, table);
            }

            WriteValue(writer, cursor, table);
            return;
        }

        if (value is SyntaxObject syntax)
        {
            // Syntax templates inside recorded macro transformers. Registered for
            // back-references: hygiene wraps share structure heavily.
            writer.Write(TagSyntaxObject);
            table.Add(syntax, table.Count);
            WriteValue(writer, syntax.Expression, table);
            WriteValue(writer, syntax.Wrap, table);
            WriteValue(writer, syntax.Module, table);
            WriteValue(writer, syntax.SourceVector, table);
            return;
        }

        if (value is SchemeStruct expression)
        {
            int vtableIndex = ExpandedVtables.IndexOf(expression.Vtable);
            if (vtableIndex < 0)
            {
                throw new NotSupportedException(
                    "expansion cache: struct vtable '" + expression.Vtable.Name + "' is not Tree-IL");
            }

            writer.Write(TagStruct);
            table.Add(expression, table.Count);
            writer.Write7BitEncodedInt(vtableIndex);
            writer.Write7BitEncodedInt(expression.Fields.Length);
            foreach (object field in expression.Fields)
            {
                WriteValue(writer, field, table);
            }

            return;
        }

        if (value is object[] array)
        {
            writer.Write(TagObjectArray);
            table.Add(array, table.Count);
            writer.Write7BitEncodedInt(array.Length);
            foreach (object element in array)
            {
                WriteValue(writer, element, table);
            }

            return;
        }

        if (value is SchemeArray vector)
        {
            if (vector.IsShared)
            {
                throw new NotSupportedException("expansion cache: shared arrays are not cacheable");
            }

            writer.Write(TagVector);
            table.Add(vector, table.Count);
            WriteInt32Array(writer, vector.LowerBounds);
            WriteInt32Array(writer, vector.Lengths);
            foreach (object element in vector.Storage)
            {
                WriteValue(writer, element, table);
            }

            return;
        }

        throw new NotSupportedException(
            "expansion cache: unexpected value type " + value.GetType().FullName);
    }

    private static object ReadValue(BinaryReader reader, List<object> table)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        byte tag = reader.ReadByte();
        switch (tag)
        {
            case TagRef:
            {
                object target = table[reader.Read7BitEncodedInt()];
                if (target == null)
                {
                    // Only an under-construction immutable node leaves a null slot; a
                    // reference into one means a cycle the format cannot represent.
                    throw new InvalidDataException("expansion cache: reference into an unfinished node");
                }

                return target;
            }

            case TagNull:
                return null;

            case TagFalse:
                return false;

            case TagTrue:
                return true;

            case TagNil:
                return Nil.Instance;

            case TagUnspecified:
                return Unspecified.Instance;

            case TagInt64:
                return reader.Read7BitEncodedInt64();

            case TagDouble:
                return reader.ReadDouble();

            case TagBigInteger:
                return ReadBigInteger(reader);

            case TagRatio:
                return new Ratio(ReadBigInteger(reader), ReadBigInteger(reader));

            case TagComplex:
                return new ComplexNumber(reader.ReadDouble(), reader.ReadDouble());

            case TagChar:
                return SchemeChar.Get(reader.Read7BitEncodedInt());

            case TagInternedSymbol:
            {
                Symbol symbol = Symbol.Intern(reader.ReadString());
                table.Add(symbol);
                return symbol;
            }

            case TagUninternedSymbol:
            {
                Symbol symbol = Symbol.CreateUninterned(reader.ReadString());
                table.Add(symbol);
                return symbol;
            }

            case TagKeyword:
            {
                Keyword keyword = Keyword.Get(reader.ReadString());
                table.Add(keyword);
                return keyword;
            }

            case TagMutableString:
            {
                MutableString text = new MutableString(reader.ReadString());
                table.Add(text);
                return text;
            }

            case TagPairChain:
            {
                int count = reader.Read7BitEncodedInt();
                Pair[] chain = new Pair[count];
                for (int i = 0; i < count; i++)
                {
                    chain[i] = new Pair(null, null);
                    table.Add(chain[i]);
                }

                for (int i = 0; i < count; i++)
                {
                    chain[i].Car = ReadValue(reader, table);
                }

                object tail = ReadValue(reader, table);
                for (int i = count - 1; i >= 0; i--)
                {
                    chain[i].Cdr = tail;
                    tail = chain[i];
                }

                return tail;
            }

            case TagSyntaxObject:
            {
                int slot = table.Count;
                table.Add(null);
                object expression = ReadValue(reader, table);
                object wrap = ReadValue(reader, table);
                object module = ReadValue(reader, table);
                object sourceVector = ReadValue(reader, table);
                SyntaxObject syntax = new SyntaxObject(expression, wrap, module, sourceVector);
                table[slot] = syntax;
                return syntax;
            }

            case TagStruct:
            {
                StructVtable vtable = ExpandedVtables.Get(reader.Read7BitEncodedInt());
                object[] fields = new object[reader.Read7BitEncodedInt()];
                SchemeStruct expression = new SchemeStruct(vtable, fields);
                table.Add(expression);
                for (int i = 0; i < fields.Length; i++)
                {
                    fields[i] = ReadValue(reader, table);
                }

                return expression;
            }

            case TagObjectArray:
            {
                object[] array = new object[reader.Read7BitEncodedInt()];
                table.Add(array);
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = ReadValue(reader, table);
                }

                return array;
            }

            case TagVector:
            {
                int[] lowerBounds = ReadInt32Array(reader);
                int[] lengths = ReadInt32Array(reader);
                int total = 1;
                foreach (int length in lengths)
                {
                    total *= length;
                }

                object[] storage = new object[total];
                SchemeArray vector = new SchemeArray(lowerBounds, lengths, storage);
                table.Add(vector);
                for (int i = 0; i < storage.Length; i++)
                {
                    storage[i] = ReadValue(reader, table);
                }

                return vector;
            }

            default:
                throw new InvalidDataException("expansion cache: unknown tag " + tag);
        }
    }

    private static void WriteBigInteger(BinaryWriter writer, BigInteger value)
    {
        byte[] bytes = value.ToByteArray();
        writer.Write7BitEncodedInt(bytes.Length);
        writer.Write(bytes);
    }

    private static BigInteger ReadBigInteger(BinaryReader reader)
    {
        return new BigInteger(reader.ReadBytes(reader.Read7BitEncodedInt()));
    }

    private static void WriteInt32Array(BinaryWriter writer, int[] values)
    {
        writer.Write7BitEncodedInt(values.Length);
        foreach (int value in values)
        {
            writer.Write7BitEncodedInt(value);
        }
    }

    private static int[] ReadInt32Array(BinaryReader reader)
    {
        int[] values = new int[reader.Read7BitEncodedInt()];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.Read7BitEncodedInt();
        }

        return values;
    }
}
