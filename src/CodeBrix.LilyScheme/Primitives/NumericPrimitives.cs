// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;
using System.Numerics;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>Arithmetic and numeric predicates over the fixnum/bignum/ratio/real tower.</summary>
public static class NumericPrimitives
{
    /// <summary>Installs the numeric primitives into an interpreter.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("+", 0, -1, a => Fold(a, 0L, SchemeNumber.Add));
        interpreter.DefinePrimitive("*", 0, -1, a => Fold(a, 1L, SchemeNumber.Multiply));

        interpreter.DefinePrimitive("-", 1, -1, a =>
        {
            if (a.Length == 1)
            {
                return SchemeNumber.Negate(Check(a[0], "-"));
            }

            object result = Check(a[0], "-");
            for (int i = 1; i < a.Length; i++)
            {
                result = SchemeNumber.Subtract(result, Check(a[i], "-"));
            }

            return result;
        });

        interpreter.DefinePrimitive("/", 1, -1, a =>
        {
            if (a.Length == 1)
            {
                return SchemeNumber.Divide(1L, Check(a[0], "/"));
            }

            object result = Check(a[0], "/");
            for (int i = 1; i < a.Length; i++)
            {
                result = SchemeNumber.Divide(result, Check(a[i], "/"));
            }

            return result;
        });

        InstallComparisons(interpreter);
        InstallIntegerOperations(interpreter);
        InstallIntegerDivision(interpreter);
        InstallPredicates(interpreter);
        InstallConversions(interpreter);
    }

    /// <summary>
    /// Guile's integer-division family: a quotient rounded a named way, the remainder that
    /// pairs with it, and the two-value form that returns both.
    /// <para>
    /// Four of the six roundings are here — floor, ceiling, truncate and euclidean. That is
    /// what LilyPond reaches, measured rather than assumed: <c>ceiling-quotient</c> and
    /// <c>floor-quotient</c> are the two arms of one <c>if</c> in
    /// <c>lily-library.scm</c> (so the log naming only the first understated the gap by
    /// exactly one name), and <c>auto-beam.scm</c> takes <c>euclidean-remainder</c>.
    /// Guile's <c>round-*</c> and <c>centered-*</c> are deliberately NOT here: nothing in
    /// LilyPond names them, and a rounding rule invented rather than demanded is a parity
    /// bug waiting for its first caller.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallIntegerDivision(Interpreter interpreter)
    {
        InstallDivisionRounding(interpreter, "floor", DivisionRounding.Floor);
        InstallDivisionRounding(interpreter, "ceiling", DivisionRounding.Ceiling);
        InstallDivisionRounding(interpreter, "truncate", DivisionRounding.Truncate);
        InstallDivisionRounding(interpreter, "euclidean", DivisionRounding.Euclidean);
    }

    private static void InstallDivisionRounding(
        Interpreter interpreter,
        string prefix,
        DivisionRounding rounding)
    {
        string quotientName = prefix + "-quotient";
        string remainderName = prefix + "-remainder";

        interpreter.DefinePrimitive(quotientName, 2, 2, a =>
        {
            Divide(a[0], a[1], quotientName, rounding, out object quotient, out object _);
            return quotient;
        });

        interpreter.DefinePrimitive(remainderName, 2, 2, a =>
        {
            Divide(a[0], a[1], remainderName, rounding, out object _, out object remainder);
            return remainder;
        });

        interpreter.DefinePrimitive(prefix + "/", 2, 2, a =>
        {
            Divide(a[0], a[1], prefix + "/", rounding, out object quotient, out object remainder);
            return new MultipleValues(new[] { quotient, remainder });
        });
    }

    /// <summary>How the quotient of an integer division is rounded to an integer.</summary>
    private enum DivisionRounding
    {
        /// <summary>Toward negative infinity.</summary>
        Floor,

        /// <summary>Toward positive infinity.</summary>
        Ceiling,

        /// <summary>Toward zero — the rounding of <c>quotient</c> and <c>remainder</c>.</summary>
        Truncate,

        /// <summary>Whichever way leaves a NON-NEGATIVE remainder, whatever the divisor's sign.</summary>
        Euclidean,
    }

    /// <summary>
    /// Splits a division into a rounded quotient and the remainder that pairs with it, so
    /// that <c>dividend = divisor * quotient + remainder</c> holds exactly.
    /// <para>
    /// The remainder is always derived from the quotient rather than computed on its own,
    /// which is what keeps that identity true across the whole tower — including the
    /// inexact case, where computing both independently lets rounding pull them apart.
    /// </para>
    /// </summary>
    private static void Divide(
        object dividendValue,
        object divisorValue,
        string procedureName,
        DivisionRounding rounding,
        out object quotient,
        out object remainder)
    {
        object dividend = Check(dividendValue, procedureName);
        object divisor = Check(divisorValue, procedureName);

        if (SchemeNumber.IsZero(divisor))
        {
            throw new SchemeThrow(
                Symbol.Intern("numerical-overflow"),
                Pair.List(
                    new MutableString(procedureName),
                    new MutableString("Division by zero"),
                    Nil.Instance,
                    false));
        }

        bool divisorIsNegative = SchemeNumber.Compare(divisor, 0L) < 0;

        if (SchemeNumber.IsExact(dividend) && SchemeNumber.IsExact(divisor))
        {
            ExactParts(dividend, out BigInteger dividendNumerator, out BigInteger dividendDenominator);
            ExactParts(divisor, out BigInteger divisorNumerator, out BigInteger divisorDenominator);

            // dividend / divisor as one fraction, with a strictly positive denominator so
            // the rounding below can reason about the sign of the numerator alone.
            BigInteger numerator = dividendNumerator * divisorDenominator;
            BigInteger denominator = dividendDenominator * divisorNumerator;
            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            BigInteger rounded = RoundFraction(numerator, denominator, rounding, divisorIsNegative);
            quotient = SchemeNumber.Normalize(rounded);
            remainder = SchemeNumber.Subtract(dividend, SchemeNumber.Multiply(quotient, divisor));
            return;
        }

        double exactDividend = SchemeNumber.ToDouble(dividend);
        double exactDivisor = SchemeNumber.ToDouble(divisor);
        double ratio = exactDividend / exactDivisor;

        double roundedQuotient;
        switch (rounding)
        {
            case DivisionRounding.Floor:
                roundedQuotient = Math.Floor(ratio);
                break;
            case DivisionRounding.Ceiling:
                roundedQuotient = Math.Ceiling(ratio);
                break;
            case DivisionRounding.Truncate:
                roundedQuotient = Math.Truncate(ratio);
                break;
            default:
                roundedQuotient = divisorIsNegative ? Math.Ceiling(ratio) : Math.Floor(ratio);
                break;
        }

        quotient = roundedQuotient;
        remainder = exactDividend - (exactDivisor * roundedQuotient);
    }

    private static BigInteger RoundFraction(
        BigInteger numerator,
        BigInteger denominator,
        DivisionRounding rounding,
        bool divisorIsNegative)
    {
        // C# truncates toward zero and gives the remainder the numerator's sign, which is
        // the one fact the four cases below are built on.
        BigInteger truncated = BigInteger.DivRem(numerator, denominator, out BigInteger left);

        DivisionRounding effective = rounding;
        if (effective == DivisionRounding.Euclidean)
        {
            // A non-negative remainder means rounding away from the divisor's sign.
            effective = divisorIsNegative ? DivisionRounding.Ceiling : DivisionRounding.Floor;
        }

        switch (effective)
        {
            case DivisionRounding.Floor:
                return left.Sign < 0 ? truncated - BigInteger.One : truncated;
            case DivisionRounding.Ceiling:
                return left.Sign > 0 ? truncated + BigInteger.One : truncated;
            default:
                return truncated;
        }
    }

    private static void ExactParts(object value, out BigInteger numerator, out BigInteger denominator)
    {
        if (value is Ratio ratio)
        {
            // Ratio keeps the sign on the numerator and the denominator strictly positive.
            numerator = ratio.Numerator;
            denominator = ratio.Denominator;
            return;
        }

        numerator = SchemeNumber.ToBigInteger(value);
        denominator = BigInteger.One;
    }

    private static void InstallComparisons(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("=", 1, -1, a => Chain(a, (x, y) => SchemeNumber.Compare(x, y) == 0));
        interpreter.DefinePrimitive("<", 1, -1, a => Chain(a, (x, y) => SchemeNumber.Compare(x, y) < 0));
        interpreter.DefinePrimitive(">", 1, -1, a => Chain(a, (x, y) => SchemeNumber.Compare(x, y) > 0));
        interpreter.DefinePrimitive("<=", 1, -1, a => Chain(a, (x, y) => SchemeNumber.Compare(x, y) <= 0));
        interpreter.DefinePrimitive(">=", 1, -1, a => Chain(a, (x, y) => SchemeNumber.Compare(x, y) >= 0));

        interpreter.DefinePrimitive("max", 1, -1, a =>
        {
            object best = Check(a[0], "max");
            bool inexact = a[0] is double;
            for (int i = 1; i < a.Length; i++)
            {
                inexact |= a[i] is double;
                if (SchemeNumber.Compare(a[i], best) > 0)
                {
                    best = a[i];
                }
            }

            // R7RS: if any argument is inexact, the result is inexact.
            return inexact ? SchemeNumber.ToInexact(best) : best;
        });

        interpreter.DefinePrimitive("min", 1, -1, a =>
        {
            object best = Check(a[0], "min");
            bool inexact = a[0] is double;
            for (int i = 1; i < a.Length; i++)
            {
                inexact |= a[i] is double;
                if (SchemeNumber.Compare(a[i], best) < 0)
                {
                    best = a[i];
                }
            }

            return inexact ? SchemeNumber.ToInexact(best) : best;
        });
    }

    private static void InstallIntegerOperations(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("quotient", 2, 2, a => SchemeNumber.Quotient(a[0], a[1]));
        interpreter.DefinePrimitive("remainder", 2, 2, a => SchemeNumber.Remainder(a[0], a[1]));
        interpreter.DefinePrimitive("modulo", 2, 2, a => SchemeNumber.Modulo(a[0], a[1]));
        interpreter.DefinePrimitive("gcd", 0, -1, a => Fold(a, 0L, SchemeNumber.GreatestCommonDivisor));
        interpreter.DefinePrimitive("lcm", 0, -1, a => Fold(a, 1L, Lcm));
        interpreter.DefinePrimitive("1+", 1, 1, a => SchemeNumber.Add(a[0], 1L));
        interpreter.DefinePrimitive("1-", 1, 1, a => SchemeNumber.Subtract(a[0], 1L));
        interpreter.DefinePrimitive("abs", 1, 1, a => SchemeNumber.Compare(a[0], 0L) < 0 ? SchemeNumber.Negate(a[0]) : a[0]);
        interpreter.DefinePrimitive("expt", 2, 2, Expt);
        interpreter.DefinePrimitive("sqrt", 1, 1, a => Sqrt(a[0]));

        // The transcendental functions are always inexact, so they go straight through
        // System.Math rather than through the exact tower.
        interpreter.DefinePrimitive("exp", 1, 1, a => Math.Exp(SchemeNumber.ToDouble(a[0])));
        interpreter.DefinePrimitive("sin", 1, 1, a => Math.Sin(SchemeNumber.ToDouble(a[0])));
        interpreter.DefinePrimitive("cos", 1, 1, a => Math.Cos(SchemeNumber.ToDouble(a[0])));
        interpreter.DefinePrimitive("tan", 1, 1, a => Math.Tan(SchemeNumber.ToDouble(a[0])));
        interpreter.DefinePrimitive("asin", 1, 1, a => Math.Asin(SchemeNumber.ToDouble(a[0])));
        interpreter.DefinePrimitive("acos", 1, 1, a => Math.Acos(SchemeNumber.ToDouble(a[0])));

        // (atan y) is the one-argument arc tangent; (atan y x) is the two-argument form,
        // which is Math.Atan2 with the arguments in that same order.
        interpreter.DefinePrimitive("atan", 1, 2, a => a.Length == 1
            ? Math.Atan(SchemeNumber.ToDouble(a[0]))
            : Math.Atan2(SchemeNumber.ToDouble(a[0]), SchemeNumber.ToDouble(a[1])));

        interpreter.DefinePrimitive("log", 1, 2, a => a.Length == 1
            ? Math.Log(SchemeNumber.ToDouble(a[0]))
            : Math.Log(SchemeNumber.ToDouble(a[0])) / Math.Log(SchemeNumber.ToDouble(a[1])));

        // The one rung above the reals this interpreter models. See ComplexNumber's own
        // note: it was narrow at first, until scm/stencil.scm's arrow-stencil-maker
        // turned out to be written entirely in complex arithmetic rather than in the
        // (magnitude (make-rectangular dx dy)) idiom the narrow reading was built for.
        // Every accessor below deliberately accepts a REAL too, which is Guile's own
        // behaviour — a real IS a complex with a zero imaginary part.
        interpreter.DefinePrimitive("make-rectangular", 2, 2, a =>
            new ComplexNumber(SchemeNumber.ToDouble(a[0]), SchemeNumber.ToDouble(a[1])));

        // make-polar is how a rotation is written: (* (make-polar 1 ang) z) turns z by
        // ang, which is the whole of stencil.scm's `rotate'.
        interpreter.DefinePrimitive("make-polar", 2, 2, a =>
        {
            double magnitude = SchemeNumber.ToDouble(a[0]);
            double angle = SchemeNumber.ToDouble(a[1]);
            return new ComplexNumber(magnitude * Math.Cos(angle), magnitude * Math.Sin(angle));
        });

        interpreter.DefinePrimitive("magnitude", 1, 1, a => a[0] is ComplexNumber z
            ? z.Magnitude
            : Math.Abs(SchemeNumber.ToDouble(a[0])));

        interpreter.DefinePrimitive("angle", 1, 1, a => a[0] is ComplexNumber z
            ? z.Angle
            : SchemeNumber.ToDouble(a[0]) < 0 ? Math.PI : 0.0);

        interpreter.DefinePrimitive("real-part", 1, 1, a => a[0] is ComplexNumber z
            ? z.Real
            : a[0]);

        interpreter.DefinePrimitive("imag-part", 1, 1, a => a[0] is ComplexNumber z
            ? (object)z.Imaginary
            : 0L);
        interpreter.DefinePrimitive("floor", 1, 1, a => RoundTo(a[0], Math.Floor));
        interpreter.DefinePrimitive("ceiling", 1, 1, a => RoundTo(a[0], Math.Ceiling));
        interpreter.DefinePrimitive("truncate", 1, 1, a => RoundTo(a[0], Math.Truncate));
        interpreter.DefinePrimitive("round", 1, 1, a => RoundTo(a[0], d => Math.Round(d, MidpointRounding.ToEven)));
        interpreter.DefinePrimitive("numerator", 1, 1, a => a[0] is Ratio r ? SchemeNumber.Normalize(r.Numerator) : a[0]);
        interpreter.DefinePrimitive("denominator", 1, 1, a => a[0] is Ratio r ? SchemeNumber.Normalize(r.Denominator) : 1L);
    }

    private static void InstallPredicates(Interpreter interpreter)
    {
        // number? and complex? are THE SAME PREDICATE in R7RS and in Guile, and they
        // are the same one here: SchemeNumber.IsNumber accepts a complex,
        // because arithmetic now does. They used to part company, and the reason given
        // was "number? is what the arithmetic layer tests, and arithmetic does not accept
        // one" — true then, and a divergence that a test had pinned in place.
        //
        // real? and rational? are NOT the same predicate and must not be widened with
        // them: (real? 3+4i) is #f in Guile. They are spelled out here rather than
        // sharing IsNumber, so a later widening of the tower cannot silently take them
        // along for the ride.
        interpreter.DefinePrimitive("number?", 1, 1, a => SchemeNumber.IsNumber(a[0]));
        interpreter.DefinePrimitive("complex?", 1, 1, a => SchemeNumber.IsNumber(a[0]));
        interpreter.DefinePrimitive("integer?", 1, 1, a =>
            SchemeNumber.IsNumber(a[0]) && !(a[0] is ComplexNumber) && SchemeNumber.IsInteger(a[0]));
        interpreter.DefinePrimitive("rational?", 1, 1, a =>
            SchemeNumber.IsNumber(a[0]) && !(a[0] is ComplexNumber));
        interpreter.DefinePrimitive("real?", 1, 1, a =>
            SchemeNumber.IsNumber(a[0]) && !(a[0] is ComplexNumber));
        interpreter.DefinePrimitive("exact?", 1, 1, a => SchemeNumber.IsExact(Check(a[0], "exact?")));
        interpreter.DefinePrimitive("inexact?", 1, 1, a => !SchemeNumber.IsExact(Check(a[0], "inexact?")));
        interpreter.DefinePrimitive("exact-integer?", 1, 1, a => a[0] is long || a[0] is BigInteger || a[0] is int);
        interpreter.DefinePrimitive("zero?", 1, 1, a => SchemeNumber.IsZero(Check(a[0], "zero?")));
        interpreter.DefinePrimitive("positive?", 1, 1, a => SchemeNumber.Compare(Check(a[0], "positive?"), 0L) > 0);
        interpreter.DefinePrimitive("negative?", 1, 1, a => SchemeNumber.Compare(Check(a[0], "negative?"), 0L) < 0);
        interpreter.DefinePrimitive("even?", 1, 1, a => SchemeNumber.ToBigInteger(a[0]).IsEven);
        interpreter.DefinePrimitive("odd?", 1, 1, a => !SchemeNumber.ToBigInteger(a[0]).IsEven);

        // Guile's core bitwise family. LilyPond's QR-code generator reaches logxor
        // for its Galois-field arithmetic; the rest of the family travels with it.
        interpreter.DefinePrimitive("logand", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, (x, y) => x & y, BigInteger.MinusOne)));
        interpreter.DefinePrimitive("logior", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, (x, y) => x | y, BigInteger.Zero)));
        interpreter.DefinePrimitive("logxor", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, (x, y) => x ^ y, BigInteger.Zero)));
        interpreter.DefinePrimitive("lognot", 1, 1, a =>
            SchemeNumber.Normalize(~SchemeNumber.ToBigInteger(a[0])));
        interpreter.DefinePrimitive("ash", 2, 2, a =>
        {
            BigInteger value = SchemeNumber.ToBigInteger(a[0]);
            int shift = (int)SchemeNumber.ToBigInteger(a[1]);
            return SchemeNumber.Normalize(shift >= 0 ? value << shift : value >> -shift);
        });

        // (logcount n) -- Guile counts the 1 bits of a non-negative integer and
        // the 0 bits of a negative one's two's-complement form; both are the
        // population count of (n < 0 ? ~n : n). define-music-callbacks.scm sizes
        // tremolo dot counts with it.
        interpreter.DefinePrimitive("logcount", 1, 1, a =>
        {
            BigInteger value = SchemeNumber.ToBigInteger(a[0]);
            return SchemeNumber.Normalize(BigInteger.PopCount(value.Sign < 0 ? ~value : value));
        });
        // (logbit? index j) -- is bit number `index` of j set, counting from 0 at the
        // least significant end. BigInteger shifts and masks in two's complement, which
        // is the representation Guile tests against for a negative j too.
        // lily-library.scm's int->bit-list is built on it.
        interpreter.DefinePrimitive("logbit?", 2, 2, a =>
        {
            int index = (int)SchemeNumber.ToBigInteger(a[0]);
            BigInteger value = SchemeNumber.ToBigInteger(a[1]);
            return !((value >> index) & BigInteger.One).IsZero;
        });

        // (integer-length n) -- the number of bits needed to represent n. libguile's
        // scm_integer_length_i replaces a negative n with (-1 - n) first, so the answer
        // is about the ones-complement magnitude and integer-length of 0 and of -1 are
        // both 0. LilyPond's qr-code.scm sizes its length markers with it.
        interpreter.DefinePrimitive("integer-length", 1, 1, a =>
        {
            BigInteger value = SchemeNumber.ToBigInteger(a[0]);
            if (value.Sign < 0)
            {
                value = BigInteger.MinusOne - value;
            }

            return SchemeNumber.Normalize(new BigInteger(value.GetBitLength()));
        });
        interpreter.DefinePrimitive("nan?", 1, 1, a => a[0] is double d && double.IsNaN(d));
        interpreter.DefinePrimitive("inf?", 1, 1, a => a[0] is double d && double.IsInfinity(d));

        // (finite? z) -- Guile REQUIRES a number and raises on anything else, where nan?
        // and inf? above simply answer #f. Every exact number is finite by construction.
        interpreter.DefinePrimitive("finite?", 1, 1, a => a[0] is double real
            ? !double.IsInfinity(real) && !double.IsNaN(real)
            : SchemeNumber.IsExact(Check(a[0], "finite?")));
        interpreter.DefinePrimitive("inf", 0, 0, a => double.PositiveInfinity);
        interpreter.DefinePrimitive("nan", 0, 0, a => double.NaN);

        interpreter.DefineValue("most-positive-fixnum", SchemeNumber.MostPositiveFixnum);
        interpreter.DefineValue("most-negative-fixnum", SchemeNumber.MostNegativeFixnum);
    }

    private static void InstallConversions(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("exact->inexact", 1, 1, a => SchemeNumber.ToInexact(a[0]));
        interpreter.DefinePrimitive("inexact->exact", 1, 1, a => SchemeNumber.ToExact(a[0]));
        interpreter.DefinePrimitive("exact", 1, 1, a => SchemeNumber.ToExact(a[0]));
        interpreter.DefinePrimitive("inexact", 1, 1, a => SchemeNumber.ToInexact(a[0]));

        interpreter.DefinePrimitive("number->string", 1, 2, a =>
        {
            int radix = a.Length > 1 ? (int)SchemeNumber.ToBigInteger(a[1]) : 10;
            return new MutableString(SchemeNumber.NumberToString(Check(a[0], "number->string"), radix));
        });

        interpreter.DefinePrimitive("string->number", 1, 2, a =>
        {
            string text = a[0].ToString();
            if (a.Length > 1)
            {
                int radix = (int)SchemeNumber.ToBigInteger(a[1]);
                string prefix = radix == 16 ? "#x" : radix == 8 ? "#o" : radix == 2 ? "#b" : string.Empty;
                text = prefix + text;
            }

            object parsed = SchemeReader.ParseNumber(text);
            return parsed ?? (object)false;
        });
    }

    private static object Lcm(object a, object b)
    {
        BigInteger x = BigInteger.Abs(SchemeNumber.ToBigInteger(a));
        BigInteger y = BigInteger.Abs(SchemeNumber.ToBigInteger(b));
        if (x.IsZero || y.IsZero)
        {
            return 0L;
        }

        return SchemeNumber.Normalize(x / BigInteger.GreatestCommonDivisor(x, y) * y);
    }

    private static object Expt(object[] arguments)
    {
        object baseValue = arguments[0];
        object exponent = arguments[1];

        if (SchemeNumber.IsExact(baseValue) && SchemeNumber.IsExact(exponent) && SchemeNumber.IsInteger(exponent))
        {
            BigInteger power = SchemeNumber.ToBigInteger(exponent);
            if (power.Sign >= 0 && power <= int.MaxValue)
            {
                if (baseValue is Ratio ratio)
                {
                    return SchemeNumber.MakeRatio(
                        SchemeNumber.Normalize(BigInteger.Pow(ratio.Numerator, (int)power)),
                        SchemeNumber.Normalize(BigInteger.Pow(ratio.Denominator, (int)power)));
                }

                return SchemeNumber.Normalize(BigInteger.Pow(SchemeNumber.ToBigInteger(baseValue), (int)power));
            }
        }

        return Math.Pow(SchemeNumber.ToDouble(baseValue), SchemeNumber.ToDouble(exponent));
    }

    private static object Sqrt(object value)
    {
        if (SchemeNumber.IsExact(value) && SchemeNumber.IsInteger(value))
        {
            BigInteger n = SchemeNumber.ToBigInteger(value);
            if (n.Sign >= 0)
            {
                // Prefer an exact result when the input is a perfect square, which is what
                // Guile does and what LilyPond's rational arithmetic relies on.
                BigInteger root = IntegerSquareRoot(n);
                if (root * root == n)
                {
                    return SchemeNumber.Normalize(root);
                }
            }
        }

        return Math.Sqrt(SchemeNumber.ToDouble(value));
    }

    private static BigInteger BitwiseFold(
        object[] arguments,
        Func<BigInteger, BigInteger, BigInteger> combine,
        BigInteger identity)
    {
        BigInteger result = identity;
        foreach (object argument in arguments)
        {
            result = combine(result, SchemeNumber.ToBigInteger(argument));
        }

        return result;
    }

    private static BigInteger IntegerSquareRoot(BigInteger value)
    {
        if (value.Sign <= 0)
        {
            return BigInteger.Zero;
        }

        BigInteger guess = value;
        BigInteger next = (guess + 1) / 2;
        while (next < guess)
        {
            guess = next;
            next = (guess + (value / guess)) / 2;
        }

        return guess;
    }

    private static object RoundTo(object value, Func<double, double> operation)
    {
        if (value is double d)
        {
            return operation(d);
        }

        if (value is Ratio ratio)
        {
            return SchemeNumber.ToExact(operation(ratio.ToDouble()));
        }

        return value;
    }

    private static object Fold(object[] arguments, object seed, Func<object, object, object> operation)
    {
        object result = seed;
        foreach (object argument in arguments)
        {
            result = operation(result, Check(argument, "arithmetic"));
        }

        return result;
    }

    private static object Chain(object[] arguments, Func<object, object, bool> comparison)
    {
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (!comparison(Check(arguments[i], "comparison"), Check(arguments[i + 1], "comparison")))
            {
                return false;
            }
        }

        return true;
    }

    private static object Check(object value, string procedureName)
    {
        if (SchemeNumber.IsNumber(value))
        {
            return value;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument: ~S"),
                Pair.List(value),
                false));
    }
}
