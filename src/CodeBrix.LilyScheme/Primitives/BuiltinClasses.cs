// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Numerics;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// GOOPS classes for the built-in types, and the mapping from a Scheme value to its class.
/// <para>
/// GOOPS dispatches on the class of each argument, and in Guile every value has one --
/// numbers, strings and pairs included. Without these, a method specialized on
/// <c>&lt;number&gt;</c> can never be selected, which is exactly what LilyPond's
/// <c>scm/operators.scm</c> needs when it extends <c>+</c> and <c>*</c> to mixed
/// moment-and-number arithmetic.
/// </para>
/// <para>
/// The hierarchy mirrors the one documented in the Guile manual: <c>&lt;number&gt;</c>
/// above <c>&lt;complex&gt;</c> above <c>&lt;real&gt;</c> above <c>&lt;integer&gt;</c> and
/// <c>&lt;fraction&gt;</c>, and <c>&lt;list&gt;</c> above both <c>&lt;pair&gt;</c> and
/// <c>&lt;null&gt;</c>.
/// </para>
/// </summary>
public static class BuiltinClasses
{
    private static readonly Dictionary<string, SchemeClass> ByName
        = new Dictionary<string, SchemeClass>(StringComparer.Ordinal);

    /// <summary>The root of the class hierarchy; every value is an instance of it.</summary>
    public static readonly SchemeClass Top = Declare("<top>");

    /// <summary>The base class of GOOPS instances.</summary>
    public static readonly SchemeClass Object = Declare("<object>", Top);

    /// <summary>The class of classes.</summary>
    public static readonly SchemeClass Class = Declare("<class>", Object);

    /// <summary>The class of all numbers.</summary>
    public static readonly SchemeClass Number = Declare("<number>", Top);

    /// <summary>The class of complex numbers.</summary>
    public static readonly SchemeClass Complex = Declare("<complex>", Number);

    /// <summary>The class of real numbers.</summary>
    public static readonly SchemeClass Real = Declare("<real>", Complex);

    /// <summary>The class of exact integers.</summary>
    public static readonly SchemeClass Integer = Declare("<integer>", Real);

    /// <summary>The class of exact non-integer rationals.</summary>
    public static readonly SchemeClass Fraction = Declare("<fraction>", Real);

    /// <summary>The class of proper and improper lists.</summary>
    public static readonly SchemeClass List = Declare("<list>", Top);

    /// <summary>The class of pairs.</summary>
    public static readonly SchemeClass Pair = Declare("<pair>", List);

    /// <summary>The class of the empty list.</summary>
    public static readonly SchemeClass Null = Declare("<null>", List);

    /// <summary>The class of strings.</summary>
    public static readonly SchemeClass String = Declare("<string>", Top);

    /// <summary>The class of symbols.</summary>
    public static readonly SchemeClass Symbol = Declare("<symbol>", Top);

    /// <summary>The class of keywords.</summary>
    public static readonly SchemeClass Keyword = Declare("<keyword>", Top);

    /// <summary>The class of characters.</summary>
    public static readonly SchemeClass Char = Declare("<char>", Top);

    /// <summary>The class of booleans.</summary>
    public static readonly SchemeClass Boolean = Declare("<boolean>", Top);

    /// <summary>The class of vectors.</summary>
    public static readonly SchemeClass Vector = Declare("<vector>", Top);

    /// <summary>The class of procedures.</summary>
    public static readonly SchemeClass Procedure = Declare("<procedure>", Top);

    /// <summary>The class of hash tables.</summary>
    public static readonly SchemeClass HashTable = Declare("<hashtable>", Top);

    /// <summary>The class of ports.</summary>
    public static readonly SchemeClass Port = Declare("<port>", Top);

    /// <summary>The class of Guile structs.</summary>
    public static readonly SchemeClass Struct = Declare("<struct>", Top);

    /// <summary>The class of values with no more specific class.</summary>
    public static readonly SchemeClass Unknown = Declare("<unknown>", Top);

    /// <summary>Gets every built-in class, keyed by its Scheme name.</summary>
    public static IReadOnlyDictionary<string, SchemeClass> All => ByName;

    /// <summary>
    /// Returns the class of a Scheme value. Every value has one, so this never returns
    /// <see langword="null"/> for a value the interpreter can produce.
    /// </summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The value's class.</returns>
    public static SchemeClass ClassOf(object value)
    {
        switch (value)
        {
            case SchemeObject instance:
                return instance.ObjectClass;
            case SchemeClass _:
                return Class;
            case bool _:
                return Boolean;
            case long _:
            case int _:
            case BigInteger _:
                return Integer;
            case Ratio _:
                return Fraction;
            case double _:
            case float _:
                return Real;
            case MutableString _:
            case string _:
                return String;
            case Values.Symbol _:
                return Symbol;
            case Values.Keyword _:
                return Keyword;
            case SchemeChar _:
                return Char;
            case Values.Pair _:
                return Pair;
            case Nil _:
                return Null;
            case object[] _:
                return Vector;
            case SchemeStruct _:
                return Struct;
            case Values.Procedure _:
                return Procedure;
            case null:
                return Unknown;
            default:
                return ClassOfExtension(value) ?? Unknown;
        }
    }

    /// <summary>
    /// Gets or sets a hook that classifies host-supplied values the core does not know
    /// about. LilyPort registers one so its ported engine types -- moments, pitches,
    /// durations -- get real GOOPS classes and can be dispatched on.
    /// </summary>
    public static Func<object, SchemeClass> ClassOfExtensionHook { get; set; }

    /// <summary>Installs the built-in classes as top-level bindings.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach (KeyValuePair<string, SchemeClass> entry in ByName)
        {
            interpreter.DefineValue(entry.Key, entry.Value);
        }
    }

    private static SchemeClass ClassOfExtension(object value)
        => ClassOfExtensionHook?.Invoke(value);

    private static SchemeClass Declare(string name, params SchemeClass[] superclasses)
    {
        SchemeClass declared = new SchemeClass(Values.Symbol.Intern(name), superclasses);
        ByName[name] = declared;
        return declared;
    }
}
