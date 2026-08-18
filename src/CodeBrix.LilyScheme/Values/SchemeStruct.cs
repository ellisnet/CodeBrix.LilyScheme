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
/// A struct vtable. Guile's struct system is the substrate psyntax is built on: the
/// macro expander returns Tree-IL nodes, and every Tree-IL node is a struct whose
/// vtable comes out of the <c>%expanded-vtables</c> vector.
/// </summary>
public sealed class StructVtable
{
    /// <summary>Initializes a vtable.</summary>
    /// <param name="name">The type name, as it appears in Guile (for example <c>lambda-case</c>).</param>
    /// <param name="fieldNames">The ordered field names.</param>
    public StructVtable(string name, params string[] fieldNames)
    {
        Name = name;
        FieldNames = fieldNames ?? Array.Empty<string>();
    }

    /// <summary>Gets the vtable's type name.</summary>
    public string Name { get; }

    /// <summary>Gets the ordered field names.</summary>
    public string[] FieldNames { get; }

    /// <summary>Gets the number of fields an instance carries.</summary>
    public int FieldCount => FieldNames.Length;

    /// <summary>Returns the index of a named field, or -1 when absent.</summary>
    /// <param name="fieldName">The field name to look up.</param>
    /// <returns>The zero-based index, or -1.</returns>
    public int IndexOf(string fieldName)
    {
        for (int i = 0; i < FieldNames.Length; i++)
        {
            if (string.Equals(FieldNames[i], fieldName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the type name.</returns>
    public override string ToString() => "#<vtable " + Name + ">";
}

/// <summary>A struct instance: a vtable plus a flat vector of field values.</summary>
public sealed class SchemeStruct
{
    /// <summary>Initializes a struct instance.</summary>
    /// <param name="vtable">The vtable describing the layout.</param>
    /// <param name="fields">The field values, in vtable order.</param>
    public SchemeStruct(StructVtable vtable, object[] fields)
    {
        Vtable = vtable;
        Fields = fields ?? Array.Empty<object>();
    }

    /// <summary>Gets the vtable describing this instance.</summary>
    public StructVtable Vtable { get; }

    /// <summary>Gets the field storage, in vtable order.</summary>
    public object[] Fields { get; }

    /// <summary>Reads a field by name, returning <see langword="null"/> when absent.</summary>
    /// <param name="fieldName">The field to read.</param>
    /// <returns>The field value, or <see langword="null"/> when the field does not exist.</returns>
    public object GetField(string fieldName)
    {
        int index = Vtable.IndexOf(fieldName);
        return index < 0 || index >= Fields.Length ? null : Fields[index];
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the vtable name.</returns>
    public override string ToString() => "#<" + Vtable.Name + ">";
}

/// <summary>
/// The eighteen Tree-IL node vtables, in the order Guile publishes them as
/// <c>%expanded-vtables</c>. Layouts mirror <c>libguile/expand.h</c> exactly — psyntax
/// constructs these positionally via <c>make-struct/simple</c>, so field order is part
/// of the contract, not a detail.
/// </summary>
public static class ExpandedVtables
{
    /// <summary>Index of the <c>void</c> node.</summary>
    public const int Void = 0;

    /// <summary>Index of the <c>const</c> node.</summary>
    public const int Const = 1;

    /// <summary>Index of the <c>primitive-ref</c> node.</summary>
    public const int PrimitiveRef = 2;

    /// <summary>Index of the <c>lexical-ref</c> node.</summary>
    public const int LexicalRef = 3;

    /// <summary>Index of the <c>lexical-set</c> node.</summary>
    public const int LexicalSet = 4;

    /// <summary>Index of the <c>module-ref</c> node.</summary>
    public const int ModuleRef = 5;

    /// <summary>Index of the <c>module-set</c> node.</summary>
    public const int ModuleSet = 6;

    /// <summary>Index of the <c>toplevel-ref</c> node.</summary>
    public const int ToplevelRef = 7;

    /// <summary>Index of the <c>toplevel-set</c> node.</summary>
    public const int ToplevelSet = 8;

    /// <summary>Index of the <c>toplevel-define</c> node.</summary>
    public const int ToplevelDefine = 9;

    /// <summary>Index of the <c>conditional</c> node.</summary>
    public const int Conditional = 10;

    /// <summary>Index of the <c>call</c> node.</summary>
    public const int Call = 11;

    /// <summary>Index of the <c>primcall</c> node.</summary>
    public const int Primcall = 12;

    /// <summary>Index of the <c>seq</c> node.</summary>
    public const int Seq = 13;

    /// <summary>Index of the <c>lambda</c> node.</summary>
    public const int Lambda = 14;

    /// <summary>Index of the <c>lambda-case</c> node.</summary>
    public const int LambdaCase = 15;

    /// <summary>Index of the <c>let</c> node.</summary>
    public const int Let = 16;

    /// <summary>Index of the <c>letrec</c> node.</summary>
    public const int Letrec = 17;

    /// <summary>Gets the number of Tree-IL node types.</summary>
    public const int Count = 18;

    private static readonly StructVtable[] Table = BuildTable();

    /// <summary>Gets the vtable array, indexed by the constants on this class.</summary>
    /// <returns>The eighteen vtables in publication order.</returns>
    public static StructVtable[] All => Table;

    /// <summary>Gets a single vtable by index.</summary>
    /// <param name="index">The node-type index.</param>
    /// <returns>The vtable at that index.</returns>
    public static StructVtable Get(int index) => Table[index];

    /// <summary>Builds the <c>%expanded-vtables</c> value exposed to Scheme.</summary>
    /// <returns>A Scheme vector of the eighteen vtables.</returns>
    public static object[] BuildSchemeVector()
    {
        object[] vector = new object[Table.Length];
        for (int i = 0; i < Table.Length; i++)
        {
            vector[i] = Table[i];
        }

        return vector;
    }

    private static StructVtable[] BuildTable()
    {
        return new[]
        {
            new StructVtable("void", "src"),
            new StructVtable("const", "src", "exp"),
            new StructVtable("primitive-ref", "src", "name"),
            new StructVtable("lexical-ref", "src", "name", "gensym"),
            new StructVtable("lexical-set", "src", "name", "gensym", "exp"),
            new StructVtable("module-ref", "src", "mod", "name", "public?"),
            new StructVtable("module-set", "src", "mod", "name", "public?", "exp"),
            new StructVtable("toplevel-ref", "src", "mod", "name"),
            new StructVtable("toplevel-set", "src", "mod", "name", "exp"),
            new StructVtable("toplevel-define", "src", "mod", "name", "exp"),
            new StructVtable("conditional", "src", "test", "consequent", "alternate"),
            new StructVtable("call", "src", "proc", "args"),
            new StructVtable("primcall", "src", "name", "args"),
            new StructVtable("seq", "src", "head", "tail"),
            new StructVtable("lambda", "src", "meta", "body"),
            new StructVtable(
                "lambda-case",
                "src",
                "req",
                "opt",
                "rest",
                "kw",
                "inits",
                "gensyms",
                "body",
                "alternate"),
            new StructVtable("let", "src", "names", "gensyms", "vals", "body"),
            new StructVtable("letrec", "src", "in-order?", "names", "gensyms", "vals", "body"),
        };
    }

    /// <summary>Maps a vtable back to its node-type index, or -1 when it is not a Tree-IL vtable.</summary>
    /// <param name="vtable">The vtable to identify.</param>
    /// <returns>The node-type index, or -1.</returns>
    public static int IndexOf(StructVtable vtable)
    {
        for (int i = 0; i < Table.Length; i++)
        {
            if (ReferenceEquals(Table[i], vtable))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// A syntax object: an expression paired with the hygiene information psyntax needs to
/// decide whether two identifiers refer to the same binding.
/// </summary>
public sealed class SyntaxObject
{
    /// <summary>Initializes a syntax object.</summary>
    /// <param name="expression">The wrapped datum.</param>
    /// <param name="wrap">The hygiene wrap.</param>
    /// <param name="module">The module the expression was read in.</param>
    /// <param name="sourceVector">The source location vector, or <see langword="null"/>.</param>
    public SyntaxObject(object expression, object wrap, object module, object sourceVector)
    {
        Expression = expression;
        Wrap = wrap;
        Module = module;
        SourceVector = sourceVector;
    }

    /// <summary>Gets the wrapped datum.</summary>
    public object Expression { get; }

    /// <summary>Gets the hygiene wrap.</summary>
    public object Wrap { get; }

    /// <summary>Gets the module the expression was read in.</summary>
    public object Module { get; }

    /// <summary>Gets the source location vector, or <see langword="null"/> when unknown.</summary>
    public object SourceVector { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the wrapped expression.</returns>
    public override string ToString() => "#<syntax " + (Expression ?? "?") + ">";
}

/// <summary>
/// A syntax transformer, as produced by <c>make-syntax-transformer</c>. Guile stores
/// macros in module bindings using this wrapper so the expander can tell a macro
/// binding from an ordinary value binding.
/// </summary>
public sealed class SyntaxTransformer
{
    /// <summary>Initializes a syntax transformer.</summary>
    /// <param name="name">The macro's name, or <see langword="false"/> when anonymous.</param>
    /// <param name="type">The transformer type symbol, such as <c>macro</c>.</param>
    /// <param name="binding">The transformer procedure or associated binding.</param>
    public SyntaxTransformer(object name, object type, object binding)
    {
        Name = name;
        TransformerType = type;
        Binding = binding;
    }

    /// <summary>Gets the macro's name.</summary>
    public object Name { get; }

    /// <summary>Gets the transformer type symbol.</summary>
    public object TransformerType { get; }

    /// <summary>Gets the transformer procedure or associated binding.</summary>
    public object Binding { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the macro name.</returns>
    public override string ToString() => "#<syntax-transformer " + (Name ?? "anonymous") + ">";
}

/// <summary>Reference-equality comparer used by <c>hashq</c> tables and <c>eq?</c> hashing.</summary>
public sealed class ReferenceComparer : IEqualityComparer<object>
{
    /// <summary>Gets the shared comparer instance.</summary>
    public static ReferenceComparer Instance { get; } = new ReferenceComparer();

    /// <summary>Compares two objects by reference, with value-type fast paths.</summary>
    /// <param name="x">The first object.</param>
    /// <param name="y">The second object.</param>
    /// <returns><see langword="true"/> when the objects are <c>eq?</c>.</returns>
    public new bool Equals(object x, object y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        // Boxed immediates must still compare eq? by value: Guile fixnums and characters
        // are immediates, not heap objects, so (eq? 5 5) is true there.
        if (x is long lx && y is long ly)
        {
            return lx == ly;
        }

        if (x is bool bx && y is bool by)
        {
            return bx == by;
        }

        return x is SchemeChar cx && y is SchemeChar cy && cx.CodePoint == cy.CodePoint;
    }

    /// <summary>Returns a reference-based hash code, with value-type fast paths.</summary>
    /// <param name="obj">The object to hash.</param>
    /// <returns>The hash code.</returns>
    public int GetHashCode(object obj)
    {
        if (obj == null)
        {
            return 0;
        }

        if (obj is long l)
        {
            return l.GetHashCode();
        }

        if (obj is bool b)
        {
            return b ? 1 : 2;
        }

        if (obj is SchemeChar c)
        {
            return c.CodePoint;
        }

        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
