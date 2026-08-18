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
/// A record type, as created by <c>make-record-type</c> and used by
/// <c>define-record-type</c> and the exception-object machinery.
/// <para>
/// An instance is a vector whose slot zero holds its <see cref="RecordType"/>. That makes
/// the predicate a reference comparison and means Scheme cannot forge one, which is what
/// SRFI-9 requires of a disjoint type.
/// </para>
/// <para>
/// Guile's single-inheritance model (boot-9.scm's records section) is carried in full:
/// a type made with <c>#:parent</c> lays out the parent's fields FIRST and its own after
/// them, <see cref="Fields"/> answers that complete layout, and the ancestor vector makes
/// <c>record-type-has-parent?</c> a constant-time prefix check. Only a type made with
/// <c>#:extensible? #t</c> may be a parent — boot-9 refuses "parent type is final".
/// </para>
/// </summary>
public sealed class RecordType
{
    private readonly bool[] _mutability;

    /// <summary>Initializes a flat, final record type whose fields are all mutable —
    /// the SRFI-9 shape the prelude's <c>define-record-type</c> builds.</summary>
    /// <param name="name">The type name.</param>
    /// <param name="fields">The field names, in slot order.</param>
    public RecordType(string name, IReadOnlyList<object> fields)
        : this(name, fields, null, false, null)
    {
    }

    /// <summary>Initializes a record type, optionally derived from a parent.</summary>
    /// <param name="name">The type name.</param>
    /// <param name="ownFields">This type's OWN field names, in slot order; the parent's
    /// fields are prepended automatically.</param>
    /// <param name="parent">The parent type, or null for a root type.</param>
    /// <param name="extensible">Whether this type may itself be a parent.</param>
    /// <param name="ownMutability">Per-own-field mutability, aligned with
    /// <paramref name="ownFields"/>; null means every field is mutable.</param>
    public RecordType(
        string name,
        IReadOnlyList<object> ownFields,
        RecordType parent,
        bool extensible,
        bool[] ownMutability)
    {
        Name = name;
        Parent = parent;
        Extensible = extensible;

        IReadOnlyList<object> own = ownFields ?? Array.Empty<object>();
        int parentCount = parent == null ? 0 : parent.Fields.Count;
        object[] layout = new object[parentCount + own.Count];
        bool[] mutability = new bool[layout.Length];
        for (int i = 0; i < parentCount; i++)
        {
            layout[i] = parent.Fields[i];
            mutability[i] = parent._mutability[i];
        }

        for (int i = 0; i < own.Count; i++)
        {
            layout[parentCount + i] = own[i];
            mutability[parentCount + i] = ownMutability == null || ownMutability[i];
        }

        Fields = layout;
        _mutability = mutability;

        if (parent == null)
        {
            Ancestors = Array.Empty<RecordType>();
        }
        else
        {
            RecordType[] ancestors = new RecordType[parent.Ancestors.Count + 1];
            for (int i = 0; i < parent.Ancestors.Count; i++)
            {
                ancestors[i] = parent.Ancestors[i];
            }

            ancestors[ancestors.Length - 1] = parent;
            Ancestors = ancestors;
        }
    }

    /// <summary>Gets the type name.</summary>
    public string Name { get; }

    /// <summary>Gets the COMPLETE field layout — the parent's fields first, then this
    /// type's own — matching Guile's <c>record-type-fields</c>.</summary>
    public IReadOnlyList<object> Fields { get; }

    /// <summary>Gets the parent type, or null for a root type.</summary>
    public RecordType Parent { get; }

    /// <summary>Gets the ancestor chain, root first, excluding this type itself —
    /// Guile's parents vector.</summary>
    public IReadOnlyList<RecordType> Ancestors { get; }

    /// <summary>Gets a value indicating whether subtypes may be derived from this type.</summary>
    public bool Extensible { get; }

    /// <summary>Returns the slot index of a field, or -1 when there is no such field.</summary>
    /// <param name="field">The field name.</param>
    /// <returns>The zero-based index among the fields.</returns>
    public int IndexOf(object field)
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Equals(Fields[i], field))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Answers whether the field at an index may be assigned after construction.</summary>
    /// <param name="index">The zero-based field index.</param>
    /// <returns>Whether the field is mutable.</returns>
    public bool IsFieldMutable(int index) => index >= 0 && index < _mutability.Length && _mutability[index];

    /// <summary>
    /// Answers Guile's <c>record-type-has-parent?</c>: whether this type IS
    /// <paramref name="candidate"/> or descends from it — a constant-time check against
    /// the ancestor vector, exactly as boot-9 does it.
    /// </summary>
    /// <param name="candidate">The candidate ancestor.</param>
    /// <returns>Whether instances of this type are instances of the candidate.</returns>
    public bool HasParent(RecordType candidate)
    {
        if (ReferenceEquals(this, candidate))
        {
            return true;
        }

        int position = candidate.Ancestors.Count;
        return position < Ancestors.Count && ReferenceEquals(Ancestors[position], candidate);
    }

    /// <summary>
    /// Answers whether a value is an instance of this type, including instances of any
    /// subtype — what <c>record-predicate</c> promises for an extensible type.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>Whether the value is an instance.</returns>
    public bool IsInstance(object value)
        => value is object[] vector
           && vector.Length > 0
           && vector[0] is RecordType actual
           && actual.HasParent(this);

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the type.</returns>
    public override string ToString() => "#<record-type " + Name + ">";
}
