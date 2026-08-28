// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;
using System.Numerics;

namespace CodeBrix.LilyScheme.Numeric;

/// <summary>
/// An exact rational with a normalized sign and a denominator greater than one.
/// Integers never reach this type — <see cref="SchemeNumber.MakeRatio"/> collapses
/// them to <see cref="long"/> or <see cref="BigInteger"/> first.
/// </summary>
public sealed class Ratio
{
    /// <summary>Initializes a ratio from already-normalized parts.</summary>
    /// <param name="numerator">The numerator, carrying the sign.</param>
    /// <param name="denominator">The denominator, strictly greater than one.</param>
    public Ratio(BigInteger numerator, BigInteger denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Gets the numerator.</summary>
    public BigInteger Numerator { get; }

    /// <summary>Gets the denominator.</summary>
    public BigInteger Denominator { get; }

    /// <summary>Returns the value as a double.</summary>
    /// <returns>The nearest double.</returns>
    public double ToDouble() => (double)Numerator / (double)Denominator;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The ratio written as <c>numerator/denominator</c>.</returns>
    public override string ToString()
        => Numerator.ToString(CultureInfo.InvariantCulture) + "/"
           + Denominator.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// An inexact complex number: the one rung of Scheme's numeric tower above the reals
/// that this interpreter models.
/// <para>
/// It was deliberately NARROW at first — built only by <c>make-rectangular</c>,
/// understood only by <c>magnitude</c>, <c>angle</c>, <c>real-part</c> and
/// <c>imag-part</c> — on the reading that upstream only ever writes
/// <c>(magnitude (make-rectangular dx dy))</c> to mean the length of a vector. That
/// reading was incomplete: <c>scm/stencil.scm</c>'s <c>arrow-stencil-maker</c> is written
/// entirely in complex arithmetic, using the plane as a rotation group, and it is reached
/// by every <c>annotate-spacing</c> page. So the literals <c>1+0i</c> and <c>0+1i</c> now
/// READ, and <c>+</c>, <c>-</c>, <c>*</c> and <c>/</c> accept a complex operand.
/// </para>
/// <para>
/// The parts are doubles, so a complex is always INEXACT here. Guile's exact complexes
/// are not modelled; the one place exactness is observable — an exact zero imaginary
/// part, which Guile collapses to the real — is handled by the reader instead, so
/// <c>1+0i</c> arrives as the exact integer 1 exactly as it does upstream.
/// </para>
/// </summary>
public sealed class ComplexNumber
{
    /// <summary>Initializes a complex number from its two parts.</summary>
    /// <param name="real">The real part.</param>
    /// <param name="imaginary">The imaginary part.</param>
    public ComplexNumber(double real, double imaginary)
    {
        Real = real;
        Imaginary = imaginary;
    }

    /// <summary>Gets the real part.</summary>
    public double Real { get; }

    /// <summary>Gets the imaginary part.</summary>
    public double Imaginary { get; }

    /// <summary>Gets the distance from the origin.</summary>
    public double Magnitude => Math.Sqrt((Real * Real) + (Imaginary * Imaginary));

    /// <summary>Gets the angle from the positive real axis, in radians.</summary>
    public double Angle => Math.Atan2(Imaginary, Real);

    /// <summary>Returns the external representation, in Scheme's rectangular notation.</summary>
    /// <returns>The number written as <c>a+bi</c>.</returns>
    /// <summary>
    /// Returns Guile's external representation: BOTH parts written as the inexact reals
    /// they are — <c>1.0+2.0i</c>, <c>0.0+1.0i</c>, <c>-1.0-2.0i</c> — never as integers.
    /// MEASURED on the pinned 2.27.2 (<c>1+2i</c> reads back and prints as <c>1.0+2.0i</c>;
    /// <c>(sqrt -4)</c> prints <c>0.0+2.0i</c>).
    /// <para>//was previously: <c>double.ToString</c> on each part, which wrote <c>1+2i</c>.</para>
    /// </summary>
    /// <returns>The external representation.</returns>
    public override string ToString()
        => SchemeNumber.NumberToString(Real, 10)
           + (Imaginary < 0 || (Imaginary == 0 && double.IsNegative(Imaginary)) ? "-" : "+")
           + SchemeNumber.NumberToString(Math.Abs(Imaginary), 10)
           + "i";
}

/// <summary>
/// The numeric tower. Scheme numbers are represented as <see cref="long"/> for
/// fixnums, <see cref="BigInteger"/> for bignums, <see cref="Ratio"/> for exact
/// rationals and <see cref="double"/> for inexact reals; every operation here
/// normalizes back to the narrowest exact type that fits.
/// </summary>
public static class SchemeNumber
{
    /// <summary>Guile's <c>most-positive-fixnum</c> on a 64-bit build.</summary>
    public const long MostPositiveFixnum = (long.MaxValue >> 1);

    /// <summary>Guile's <c>most-negative-fixnum</c> on a 64-bit build.</summary>
    public const long MostNegativeFixnum = -(long.MaxValue >> 1) - 1;

    /// <summary>Determines whether a value is any kind of number.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for fixnums, bignums, ratios, reals and complexes.</returns>
    public static bool IsNumber(object value)
        => value is long || value is BigInteger || value is Ratio || value is double
           || value is int || value is ComplexNumber;

    /// <summary>Determines whether a value is an exact number.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for fixnums, bignums and ratios.</returns>
    public static bool IsExact(object value)
        => value is long || value is BigInteger || value is Ratio || value is int;

    /// <summary>Determines whether a value is an integer, exact or not.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value has no fractional part.</returns>
    public static bool IsInteger(object value)
    {
        if (value is long || value is BigInteger || value is int)
        {
            return true;
        }

        return value is double d && !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d;
    }

    /// <summary>Narrows a <see cref="BigInteger"/> to a fixnum when it fits.</summary>
    /// <param name="value">The integer to normalize.</param>
    /// <returns>A <see cref="long"/> when representable, otherwise the original value.</returns>
    public static object Normalize(BigInteger value)
    {
        if (value >= long.MinValue && value <= long.MaxValue)
        {
            return (long)value;
        }

        return value;
    }

    /// <summary>Converts any exact integer representation to <see cref="BigInteger"/>.</summary>
    /// <param name="value">The value to widen.</param>
    /// <returns>The value as a <see cref="BigInteger"/>.</returns>
    public static BigInteger ToBigInteger(object value)
    {
        switch (value)
        {
            case long l: return l;
            case int i: return i;
            case BigInteger b: return b;
            case double d: return new BigInteger(d);
            case Ratio r: return r.Numerator / r.Denominator;
            default: throw new ArgumentException("not an integer", nameof(value));
        }
    }

    /// <summary>Converts a number to a double.</summary>
    /// <param name="value">The number to convert.</param>
    /// <returns>The value as a double.</returns>
    public static double ToDouble(object value)
    {
        switch (value)
        {
            case long l: return l;
            case int i: return i;
            case double d: return d;
            case BigInteger b: return (double)b;
            case Ratio r: return r.ToDouble();
            default: throw new ArgumentException("not a number", nameof(value));
        }
    }

    /// <summary>Builds an exact rational, reducing it and collapsing integral results.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator.</param>
    /// <returns>A fixnum, bignum or <see cref="Ratio"/>.</returns>
    public static object MakeRatio(object numerator, object denominator)
    {
        BigInteger n = ToBigInteger(numerator);
        BigInteger d = ToBigInteger(denominator);
        if (d.IsZero)
        {
            throw new DivideByZeroException("Division by exact zero.");
        }

        if (d.Sign < 0)
        {
            n = -n;
            d = -d;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
        if (!divisor.IsOne && !divisor.IsZero)
        {
            n /= divisor;
            d /= divisor;
        }

        return d.IsOne ? Normalize(n) : new Ratio(n, d);
    }

    /// <summary>Converts a number to its inexact (floating point) form.</summary>
    /// <param name="value">The number to convert.</param>
    /// <returns>A <see cref="double"/>.</returns>
    public static object ToInexact(object value)
        => value is double ? value : ToDouble(value);

    /// <summary>Converts a number to its exact form.</summary>
    /// <param name="value">The number to convert.</param>
    /// <returns>An exact number.</returns>
    public static object ToExact(object value)
    {
        if (!(value is double d))
        {
            return value;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            throw new ArgumentException("cannot convert non-finite value to exact", nameof(value));
        }

        if (Math.Floor(d) == d)
        {
            return Normalize(new BigInteger(d));
        }

        // Expand the binary fraction exactly: doubles are dyadic rationals, so scaling
        // by a power of two loses nothing.
        BigInteger denominator = BigInteger.One;
        double working = d;
        while (Math.Floor(working) != working)
        {
            working *= 2.0;
            denominator *= 2;
        }

        return MakeRatio(Normalize(new BigInteger(working)), Normalize(denominator));
    }

    private static bool EitherInexact(object a, object b) => a is double || b is double;

    private static bool EitherComplex(object a, object b)
        => a is ComplexNumber || b is ComplexNumber;

    /// <summary>
    /// Widens a real to the complex plane. Only ever called once one operand is already
    /// complex, so a non-number reaching here is the caller's error, not a coercion.
    /// </summary>
    private static ComplexNumber AsComplex(object value)
        => value as ComplexNumber ?? new ComplexNumber(ToDouble(value), 0.0);

    /// <summary>
    /// Applies Guile's collapse: a complex whose imaginary part is zero AND whose parts
    /// came out of real arithmetic is still a complex there, so the ONLY collapse is the
    /// one the reader does on an exact zero. Kept as a single named place so arithmetic
    /// never quietly drops an imaginary part.
    /// </summary>
    private static object Complex(double real, double imaginary)
        => new ComplexNumber(real, imaginary);

    /// <summary>Adds two numbers.</summary>
    /// <param name="a">The first addend.</param>
    /// <param name="b">The second addend.</param>
    /// <returns>The sum.</returns>
    public static object Add(object a, object b)
    {
        if (EitherComplex(a, b))
        {
            ComplexNumber x = AsComplex(a);
            ComplexNumber y = AsComplex(b);
            return Complex(x.Real + y.Real, x.Imaginary + y.Imaginary);
        }

        if (EitherInexact(a, b))
        {
            return ToDouble(a) + ToDouble(b);
        }

        if (a is Ratio || b is Ratio)
        {
            (BigInteger an, BigInteger ad) = AsRatio(a);
            (BigInteger bn, BigInteger bd) = AsRatio(b);
            return MakeRatio(Normalize((an * bd) + (bn * ad)), Normalize(ad * bd));
        }

        return Normalize(ToBigInteger(a) + ToBigInteger(b));
    }

    /// <summary>Subtracts one number from another.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference.</returns>
    public static object Subtract(object a, object b)
    {
        if (EitherComplex(a, b))
        {
            ComplexNumber x = AsComplex(a);
            ComplexNumber y = AsComplex(b);
            return Complex(x.Real - y.Real, x.Imaginary - y.Imaginary);
        }

        if (EitherInexact(a, b))
        {
            return ToDouble(a) - ToDouble(b);
        }

        if (a is Ratio || b is Ratio)
        {
            (BigInteger an, BigInteger ad) = AsRatio(a);
            (BigInteger bn, BigInteger bd) = AsRatio(b);
            return MakeRatio(Normalize((an * bd) - (bn * ad)), Normalize(ad * bd));
        }

        return Normalize(ToBigInteger(a) - ToBigInteger(b));
    }

    /// <summary>Multiplies two numbers.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The product.</returns>
    public static object Multiply(object a, object b)
    {
        if (EitherComplex(a, b))
        {
            //was previously: an EXACT zero factor answered the exact integer 0, on the
            // claim that "Guile answers EXACT 0 for a product with an exact zero" and that
            // stencil.scm's arrow heads (the literal 0 rotated by a complex, read back
            // with real-part) relied on it. MEASURED on the pinned 2.27.2 ("running Guile
            // 3.0") on 2026-08-28: (* 0 1+2i) is 0.0+0.0i and (real-part (* 0 1+2i)) is
            // 0.0 -- the product is computed, not short-circuited -- so stencil.scm has
            // always run on 0.0 there. (* 0 1.5) is 0.0 on both sides and is the real
            // branch below. Removed under the ruling that LilyScheme works like Guile
            // wherever possible.
            ComplexNumber x = AsComplex(a);
            ComplexNumber y = AsComplex(b);
            return Complex(
                (x.Real * y.Real) - (x.Imaginary * y.Imaginary),
                (x.Real * y.Imaginary) + (x.Imaginary * y.Real));
        }

        if (EitherInexact(a, b))
        {
            return ToDouble(a) * ToDouble(b);
        }

        if (a is Ratio || b is Ratio)
        {
            (BigInteger an, BigInteger ad) = AsRatio(a);
            (BigInteger bn, BigInteger bd) = AsRatio(b);
            return MakeRatio(Normalize(an * bn), Normalize(ad * bd));
        }

        return Normalize(ToBigInteger(a) * ToBigInteger(b));
    }

    /// <summary>Divides one number by another, producing an exact ratio when both are exact.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient.</returns>
    public static object Divide(object a, object b)
    {
        if (EitherComplex(a, b))
        {
            ComplexNumber x = AsComplex(a);
            ComplexNumber y = AsComplex(b);
            double denominator = (y.Real * y.Real) + (y.Imaginary * y.Imaginary);
            return Complex(
                ((x.Real * y.Real) + (x.Imaginary * y.Imaginary)) / denominator,
                ((x.Imaginary * y.Real) - (x.Real * y.Imaginary)) / denominator);
        }

        if (EitherInexact(a, b))
        {
            return ToDouble(a) / ToDouble(b);
        }

        (BigInteger an, BigInteger ad) = AsRatio(a);
        (BigInteger bn, BigInteger bd) = AsRatio(b);
        if (bn.IsZero)
        {
            throw new DivideByZeroException("Division by exact zero.");
        }

        return MakeRatio(Normalize(an * bd), Normalize(ad * bn));
    }

    private static (BigInteger Numerator, BigInteger Denominator) AsRatio(object value)
    {
        if (value is Ratio r)
        {
            return (r.Numerator, r.Denominator);
        }

        return (ToBigInteger(value), BigInteger.One);
    }

    /// <summary>Compares two numbers.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(object a, object b)
    {
        if (EitherInexact(a, b))
        {
            return ToDouble(a).CompareTo(ToDouble(b));
        }

        (BigInteger an, BigInteger ad) = AsRatio(a);
        (BigInteger bn, BigInteger bd) = AsRatio(b);
        return (an * bd).CompareTo(bn * ad);
    }

    /// <summary>Determines whether two numbers are numerically equal.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns><see langword="true"/> when the values compare equal.</returns>
    public static bool NumericEquals(object a, object b) => Compare(a, b) == 0;

    /// <summary>Determines whether a number is zero.</summary>
    /// <param name="value">The number to test.</param>
    /// <returns><see langword="true"/> when the value is zero.</returns>
    public static bool IsZero(object value)
    {
        switch (value)
        {
            case long l: return l == 0;
            case int i: return i == 0;
            case double d: return d == 0.0;
            case BigInteger b: return b.IsZero;
            case Ratio r: return r.Numerator.IsZero;
            default: return false;
        }
    }

    /// <summary>Negates a number.</summary>
    /// <param name="value">The number to negate.</param>
    /// <returns>The additive inverse.</returns>
    public static object Negate(object value) => Subtract(0L, value);

    /// <summary>Computes the truncating quotient of two integers.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient.</returns>
    public static object Quotient(object a, object b)
    {
        if (EitherInexact(a, b))
        {
            return Math.Truncate(ToDouble(a) / ToDouble(b));
        }

        return Normalize(BigInteger.Divide(ToBigInteger(a), ToBigInteger(b)));
    }

    /// <summary>Computes the remainder, taking the sign of the dividend.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder.</returns>
    public static object Remainder(object a, object b)
    {
        if (EitherInexact(a, b))
        {
            return Math.IEEERemainder(ToDouble(a), ToDouble(b));
        }

        return Normalize(BigInteger.Remainder(ToBigInteger(a), ToBigInteger(b)));
    }

    /// <summary>Computes the modulo, taking the sign of the divisor.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The modulus.</returns>
    public static object Modulo(object a, object b)
    {
        BigInteger x = ToBigInteger(a);
        BigInteger y = ToBigInteger(b);
        BigInteger result = BigInteger.Remainder(x, y);
        if (!result.IsZero && (result.Sign != y.Sign))
        {
            result += y;
        }

        return Normalize(result);
    }

    /// <summary>Computes the greatest common divisor of two integers.</summary>
    /// <param name="a">The first integer.</param>
    /// <param name="b">The second integer.</param>
    /// <returns>The greatest common divisor, always non-negative.</returns>
    public static object GreatestCommonDivisor(object a, object b)
        => Normalize(BigInteger.GreatestCommonDivisor(ToBigInteger(a), ToBigInteger(b)));

    /// <summary>Renders a number in the given radix.</summary>
    /// <param name="value">The number to render.</param>
    /// <param name="radix">The radix, between 2 and 36.</param>
    /// <returns>The external representation.</returns>
    public static string NumberToString(object value, int radix)
    {
        if (radix == 10)
        {
            return ToDisplayString(value);
        }

        BigInteger integer = ToBigInteger(value);
        bool negative = integer.Sign < 0;
        if (negative)
        {
            integer = -integer;
        }

        if (integer.IsZero)
        {
            return "0";
        }

        const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        while (!integer.IsZero)
        {
            builder.Insert(0, Digits[(int)(integer % radix)]);
            integer /= radix;
        }

        if (negative)
        {
            builder.Insert(0, '-');
        }

        return builder.ToString();
    }

    /// <summary>Renders a number in base ten, using Scheme's conventions for reals.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The external representation.</returns>
    public static string ToDisplayString(object value)
    {
        switch (value)
        {
            case long l:
                return l.ToString(CultureInfo.InvariantCulture);
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            case BigInteger b:
                return b.ToString(CultureInfo.InvariantCulture);
            case Ratio r:
                return r.ToString();
            case double d:
                if (double.IsNaN(d))
                {
                    return "+nan.0";
                }

                if (double.IsPositiveInfinity(d))
                {
                    return "+inf.0";
                }

                if (double.IsNegativeInfinity(d))
                {
                    return "-inf.0";
                }

                // Scheme writes inexact integers with a trailing dot, so 1.0 reads back
                // as inexact rather than as the exact integer 1.
                if (Math.Floor(d) == d && Math.Abs(d) < 1e16)
                {
                    return d.ToString("0.0", CultureInfo.InvariantCulture);
                }

                return d.ToString("R", CultureInfo.InvariantCulture);
            default:
                return value == null ? "#f" : value.ToString();
        }
    }
}
