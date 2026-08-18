// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Guile's PRIMITIVE GENERICS: the core procedures that dispatch to a generic when their
/// own arguments fail to apply, and the mechanism that attaches that generic.
/// <para>
/// The distinction this type exists to preserve is WHERE the generic lives. An ordinary
/// <c>define-method</c> on a fresh name defines a generic in the current module, which is
/// Guile's <c>toplevel-define!</c>. A <c>define-method</c> on a generic-capable PRIMITIVE
/// does something else entirely: <c>oop/goops.scm</c>'s
/// <c>(define-method (add-method! (proc &lt;procedure&gt;) (m &lt;method&gt;)) ...)</c> calls
/// <c>enable-primitive-generic!</c>, which hangs the generic off the subr ITSELF and adds
/// the method there. No binding is created or shadowed.
/// </para>
/// <para>
/// That difference is the whole point. The primitive object is shared by every module that
/// imports the core, so extending it is global — which is why LilyPond can write
/// <c>(define-method (- (a &lt;Pitch&gt;) (b &lt;Pitch&gt;)) ...)</c> once in
/// <c>lily/operators.scm</c> and have <c>\transpose</c> work from a parser scope that never
/// loaded that file. Defining a fresh generic in the defining module instead compiles,
/// loads, and passes any test that exercises subtraction from that same module — and then
/// every other module still resolves the raw numeric <c>-</c> and throws
/// <c>wrong-type-arg</c> on a pitch.
/// </para>
/// </summary>
public static class PrimitiveGenerics
{
    /// <summary>
    /// The names Guile declares generic-capable. Measured from the pinned Guile source
    /// rather than recalled: every <c>SCM_PRIMITIVE_GENERIC</c> in <c>libguile/*.c</c>,
    /// plus <c>display</c> and <c>write</c>, which use the older <c>SCM_GPROC</c> form in
    /// <c>print.c</c>. Names LilyScheme does not define are skipped, so this list may name
    /// more than the interpreter has.
    /// </summary>
    private static readonly string[] GenericCapableNames =
    {
        "*", "+", "-", "/", "<", "<=", "=", ">", ">=",
        "abs", "acos", "acosh", "angle", "asin", "asinh", "atan", "atanh",
        "ceiling", "ceiling/", "ceiling-quotient", "ceiling-remainder",
        "centered/", "centered-quotient", "centered-remainder",
        "cos", "cosh", "denominator", "display", "equal?", "even?",
        "exact?", "exact->inexact", "exp", "expt", "finite?",
        "floor", "floor/", "floor-quotient", "floor-remainder",
        "gcd", "imag-part", "inexact?", "inexact->exact", "inf?", "lcm",
        "log", "log10", "magnitude", "max", "min", "modulo",
        "nan?", "negative?", "numerator", "odd?", "positive?", "quotient",
        "real-part", "remainder", "round", "round/", "round-quotient",
        "round-remainder", "setter", "sin", "sinh", "sqrt", "tan", "tanh",
        "truncate", "truncate/", "truncate-quotient", "truncate-remainder",
        "write", "zero?",
    };

    /// <summary>
    /// Marks the generic-capable primitives and installs the four bindings that
    /// <c>oop/goops.scm</c> exports for them.
    /// <para>
    /// This runs AFTER every primitive is installed, because it marks the objects that are
    /// bound by then. A primitive redefined later loses its mark, which matches Guile: a
    /// redefinition is a different subr.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach (string name in GenericCapableNames)
        {
            Variable variable = interpreter.GuileModule.Lookup(Symbol.Intern(name));
            if (variable != null && variable.IsBound && variable.GetValue() is Primitive primitive)
            {
                primitive.IsGenericCapable = true;
            }
        }

        interpreter.DefinePrimitive("generic-capability?", 1, 1, a =>
            a[0] is Primitive primitive && primitive.IsGenericCapable);

        // Guile's is variadic and enables every argument; it returns unspecified, and the
        // generic is fetched separately with primitive-generic-generic.
        interpreter.DefinePrimitive("enable-primitive-generic!", 0, -1, a =>
        {
            foreach (object candidate in a)
            {
                if (candidate is Primitive primitive && primitive.IsGenericCapable)
                {
                    Enable(primitive);
                }
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("primitive-generic-generic", 1, 1, a =>
        {
            if (a[0] is Primitive primitive && primitive.IsGenericCapable)
            {
                return Enable(primitive);
            }

            throw new SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(
                    new MutableString("primitive-generic-generic"),
                    new MutableString("Not a primitive generic: ~S"),
                    Pair.List(a[0]),
                    false));
        });

        interpreter.DefinePrimitive("set-primitive-generic!", 2, 2, a =>
        {
            if (a[0] is Primitive primitive)
            {
                primitive.AttachedGeneric = a[1];
            }

            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Attaches a generic to a primitive, or returns the one already attached — Guile's
    /// <c>enable-primitive-generic!</c> for a single subr.
    /// </summary>
    /// <param name="primitive">The generic-capable primitive to extend.</param>
    /// <returns>The generic that carries this primitive's methods.</returns>
    public static GenericFunction Enable(Primitive primitive)
    {
        if (primitive == null)
        {
            throw new ArgumentNullException(nameof(primitive));
        }

        if (primitive.AttachedGeneric is GenericFunction existing)
        {
            return existing;
        }

        // The primitive stays the default. Specializing '+' on moments must leave ordinary
        // addition working for every other caller, and the apply path reaches it either
        // way — through this fallback when the generic is applied directly, and by simply
        // invoking the primitive when no method matches.
        GenericFunction generic = new GenericFunction
        {
            Name = primitive.Name,
            Fallback = primitive,
        };

        primitive.AttachedGeneric = generic;
        return generic;
    }
}
