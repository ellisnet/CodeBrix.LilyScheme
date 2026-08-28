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

        // THE FOUR ARITHMETIC OPERATORS ARE PAIRWISE, AND EACH PAIR IS A PRIMITIVE-GENERIC
        // CALL. Guile's VM folds (+ a b c) as (+ (+ a b) c) through scm_sum, and scm_sum is
        // where SCM_WTA_DISPATCH_2 lives: a pair whose operands are not both numbers falls
        // over to the generic enable-primitive-generic! attached, and a generic with no
        // applicable method raises goops-error naming THAT PAIR -- (+ 1 2 "x") reports
        // (+ 3 "x"). With no generic attached the pair raises Guile's positioned
        // wrong-type-arg: position 1 when the accumulator is the offender, 2 otherwise.
        // MEASURED on the pinned 2.27.2, all of it, including the shape.
        //
        //was previously: one Fold over all arguments with a Check that raised
        // (wrong-type-arg "arithmetic" "Wrong type argument: ~S" (value) #f) -- no
        // procedure name, no position, and no route to the attached generic at all (the
        // evaluator selected a method BEFORE the primitive, so a method with real
        // specializers never saw a bad pair). Changed 2026-08-28 (two-mode items 4a/4b).
        Primitive plus = null;
        plus = interpreter.DefinePrimitive("+", 0, -1, a =>
            FoldDispatch(interpreter, plus, "+", a, 0L, SchemeNumber.Add, false));
        Primitive times = null;
        times = interpreter.DefinePrimitive("*", 0, -1, a =>
            FoldDispatch(interpreter, times, "*", a, 1L, SchemeNumber.Multiply, true));

        Primitive minus = null;
        minus = interpreter.DefinePrimitive("-", 1, -1, a =>
        {
            if (a.Length == 1)
            {
                return SchemeNumber.IsNumber(a[0])
                    ? SchemeNumber.Negate(a[0])
                    : FallOver(interpreter, minus, "-", a, 1, a[0]);
            }

            object result = a[0];
            for (int i = 1; i < a.Length; i++)
            {
                result = Binary(interpreter, minus, "-", result, a[i], SchemeNumber.Subtract, false);
            }

            return result;
        });

        Primitive divide = null;
        divide = interpreter.DefinePrimitive("/", 1, -1, a =>
        {
            if (a.Length == 1)
            {
                return SchemeNumber.IsNumber(a[0])
                    ? SchemeNumber.Divide(1L, a[0])
                    : FallOver(interpreter, divide, "/", a, 1, a[0]);
            }

            object result = a[0];
            for (int i = 1; i < a.Length; i++)
            {
                result = Binary(interpreter, divide, "/", result, a[i], SchemeNumber.Divide, false);
            }

            return result;
        });

        // Per INTERPRETER, never static: the primitives that 1+, 1-, expt and ash dispatch
        // through must be this interpreter's own, or a second interpreter alive in the same
        // process would route them through the other's attached generics.
        NumericContext context = new NumericContext(plus, minus, times);

        InstallComparisons(interpreter, context);
        InstallIntegerOperations(interpreter, context);
        InstallIntegerDivision(interpreter);
        InstallPredicates(interpreter, context);
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
        object dividend = Check(dividendValue, procedureName, 1);
        object divisor = Check(divisorValue, procedureName, 2);

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

    private static void InstallComparisons(Interpreter interpreter, NumericContext context)
    {
        // The comparisons are pairwise primitive-generic calls too (scm_less_p and
        // relatives), short-circuiting on the first false pair, and a ONE-ARGUMENT
        // comparison answers #t without looking at its argument: (< "x") is #t. Guile
        // reports the offending position for < > <= >=, but `=' names position 1 whichever
        // operand is bad -- (= 1 "x") reports (1 "x"). All MEASURED on the pinned 2.27.2.
        // `=' takes a complex ((= 1+2i 1+2i) is #t); the ordered comparisons are REAL-only
        // and refuse one with the positioned error (MEASURED).
        DefineComparison(interpreter, "=", NumericEquals, true, SchemeNumber.IsNumber);
        context.Less = DefineComparison(interpreter, "<", (x, y) => SchemeNumber.Compare(x, y) < 0, false, IsReal);
        DefineComparison(interpreter, ">", (x, y) => SchemeNumber.Compare(x, y) > 0, false, IsReal);
        DefineComparison(interpreter, "<=", (x, y) => SchemeNumber.Compare(x, y) <= 0, false, IsReal);
        DefineComparison(interpreter, ">=", (x, y) => SchemeNumber.Compare(x, y) >= 0, false, IsReal);

        DefineExtremum(interpreter, "max", 1);
        DefineExtremum(interpreter, "min", -1);
    }

    private static void InstallIntegerOperations(Interpreter interpreter, NumericContext context)
    {
        // The integer family accepts an INEXACT integer -- (quotient 4.0 2) is 2.0 -- and
        // refuses a non-integer or a non-number with the positioned wrong-type-arg;
        // the quotient and its remainders are the truncate and floor roundings of the
        // shared Divide, which keeps dividend = divisor * quotient + remainder exact.
        //
        //was previously: SchemeNumber.Quotient/Remainder/Modulo straight off the raw
        // arguments, whose ArgumentException ("not an integer") ESCAPED TO THE HOST as a
        // .NET exception no Scheme `catch' could see. Found 2026-08-28.
        interpreter.DefinePrimitive("quotient", 2, 2, a =>
        {
            CheckInteger(a[0], "quotient", 1);
            CheckInteger(a[1], "quotient", 2);
            Divide(a[0], a[1], "quotient", DivisionRounding.Truncate, out object quotient, out object _);
            return quotient;
        });
        interpreter.DefinePrimitive("remainder", 2, 2, a =>
        {
            CheckInteger(a[0], "remainder", 1);
            CheckInteger(a[1], "remainder", 2);
            Divide(a[0], a[1], "remainder", DivisionRounding.Truncate, out object _, out object remainder);
            return remainder;
        });
        interpreter.DefinePrimitive("modulo", 2, 2, a =>
        {
            CheckInteger(a[0], "modulo", 1);
            CheckInteger(a[1], "modulo", 2);
            Divide(a[0], a[1], "modulo", DivisionRounding.Floor, out object _, out object remainder);
            return remainder;
        });

        // gcd and lcm are generic-capable pairwise folds like +, over integers (an inexact
        // integer is accepted and makes the answer inexact: (gcd 4.0 2) is 2.0).
        Primitive gcd = null;
        gcd = interpreter.DefinePrimitive("gcd", 0, -1, a =>
            FoldDispatch(interpreter, gcd, "gcd", a, 0L, IntegerGcd, false, IsIntegerValue));
        Primitive lcm = null;
        lcm = interpreter.DefinePrimitive("lcm", 0, -1, a =>
            FoldDispatch(interpreter, lcm, "lcm", a, 1L, Lcm, false, IsIntegerValue));

        // 1+ and 1- ARE (+ x 1) and (- x 1) in Guile, generic and all: (1+ foo) with a
        // <foo> method on + applies it, and (1+ "x") reports the call (+ "x" 1).
        interpreter.DefinePrimitive("1+", 1, 1, a =>
            Binary(interpreter, context.Plus, "+", a[0], 1L, SchemeNumber.Add, false));
        interpreter.DefinePrimitive("1-", 1, 1, a =>
            Binary(interpreter, context.Minus, "-", a[0], 1L, SchemeNumber.Subtract, false));
        // abs is REAL-only and refuses a complex with the positioned error (MEASURED:
        // (abs 3.0+1.0i) is a wrong-type-arg in position 1; magnitude is the complex one).
        interpreter.DefinePrimitive("abs", 1, 1, a =>
            SchemeNumber.Compare(CheckReal(a[0], "abs", 1), 0L) < 0 ? SchemeNumber.Negate(a[0]) : a[0]);
        interpreter.DefinePrimitive("expt", 2, 2, a => Expt(interpreter, context, a));
        interpreter.DefinePrimitive("sqrt", 1, 1, a => Sqrt(Check(a[0], "sqrt", 1)));

        // The transcendental functions are always inexact. A REAL goes through System.Math;
        // a COMPLEX goes through System.Numerics.Complex, which is what Guile computes:
        // (exp +i) is 0.5403023058681398+0.8414709848078965i on the pinned 2.27.2.
        //was previously: SchemeNumber.ToDouble on the argument, so a complex threw a raw
        // .NET ArgumentException.
        interpreter.DefinePrimitive("exp", 1, 1, a => Transcendental(a[0], "exp", Math.Exp, System.Numerics.Complex.Exp));
        interpreter.DefinePrimitive("sin", 1, 1, a => Transcendental(a[0], "sin", Math.Sin, System.Numerics.Complex.Sin));
        interpreter.DefinePrimitive("cos", 1, 1, a => Transcendental(a[0], "cos", Math.Cos, System.Numerics.Complex.Cos));
        interpreter.DefinePrimitive("tan", 1, 1, a => Transcendental(a[0], "tan", Math.Tan, System.Numerics.Complex.Tan));
        interpreter.DefinePrimitive("asin", 1, 1, a => Transcendental(a[0], "asin", Math.Asin, System.Numerics.Complex.Asin));
        interpreter.DefinePrimitive("acos", 1, 1, a => Transcendental(a[0], "acos", Math.Acos, System.Numerics.Complex.Acos));

        // (atan y) is the one-argument arc tangent, complex-aware; (atan y x) is the
        // two-argument form, REAL-only, which is Math.Atan2 with the arguments in that order.
        interpreter.DefinePrimitive("atan", 1, 2, a => a.Length == 1
            ? Transcendental(a[0], "atan", Math.Atan, System.Numerics.Complex.Atan)
            : Math.Atan2(RealOf(a[0], "atan", 1), RealOf(a[1], "atan", 2)));

        interpreter.DefinePrimitive("log", 1, 2, a => a.Length == 1
            ? Transcendental(a[0], "log", Math.Log, System.Numerics.Complex.Log)
            : LogBase(a[0], a[1]));

        // The one rung above the reals this interpreter models. See ComplexNumber's own
        // note: it was narrow at first, until scm/stencil.scm's arrow-stencil-maker
        // turned out to be written entirely in complex arithmetic rather than in the
        // (magnitude (make-rectangular dx dy)) idiom the narrow reading was built for.
        // Every accessor below deliberately accepts a REAL too, which is Guile's own
        // behaviour — a real IS a complex with a zero imaginary part.
        // An EXACT zero imaginary part is no complex at all: (make-rectangular 1 0) is the
        // exact integer 1 and (make-rectangular 1.5 0) is 1.5, while an INEXACT zero keeps
        // the complex -- (make-rectangular 1 0.0) is 1.0+0.0i. Likewise an exact zero angle
        // to make-polar answers the magnitude unchanged. MEASURED on the pinned 2.27.2.
        //was previously: always a ComplexNumber, printed 1+0i.
        interpreter.DefinePrimitive("make-rectangular", 2, 2, a =>
        {
            Check(a[0], "make-rectangular", 1);
            Check(a[1], "make-rectangular", 2);
            return IsExactInteger(a[1]) && SchemeNumber.IsZero(a[1])
                ? a[0]
                : new ComplexNumber(SchemeNumber.ToDouble(a[0]), SchemeNumber.ToDouble(a[1]));
        });

        // make-polar is how a rotation is written: (* (make-polar 1 ang) z) turns z by
        // ang, which is the whole of stencil.scm's `rotate'.
        interpreter.DefinePrimitive("make-polar", 2, 2, a =>
        {
            Check(a[0], "make-polar", 1);
            Check(a[1], "make-polar", 2);
            if (IsExactInteger(a[1]) && SchemeNumber.IsZero(a[1]))
            {
                return a[0];
            }

            double magnitude = SchemeNumber.ToDouble(a[0]);
            double angle = SchemeNumber.ToDouble(a[1]);
            return new ComplexNumber(magnitude * Math.Cos(angle), magnitude * Math.Sin(angle));
        });

        interpreter.DefinePrimitive("magnitude", 1, 1, a => Check(a[0], "magnitude", 1) is ComplexNumber z
            ? z.Magnitude
            : Math.Abs(SchemeNumber.ToDouble(a[0])));

        interpreter.DefinePrimitive("angle", 1, 1, a => Check(a[0], "angle", 1) is ComplexNumber z
            ? z.Angle
            : SchemeNumber.ToDouble(a[0]) < 0 ? Math.PI : 0.0);

        interpreter.DefinePrimitive("real-part", 1, 1, a => Check(a[0], "real-part", 1) is ComplexNumber z
            ? z.Real
            : a[0]);

        interpreter.DefinePrimitive("imag-part", 1, 1, a => Check(a[0], "imag-part", 1) is ComplexNumber z
            ? (object)z.Imaginary
            : 0L);

        //was previously: the rounding family and numerator/denominator answered a
        // NON-NUMBER unchanged -- (floor "x") was "x". Guile raises the positioned
        // wrong-type-arg; and numerator/denominator of an INEXACT rational answer the
        // inexact parts of its exact form ((numerator 1.5) is 3.0). MEASURED 2026-08-28.
        interpreter.DefinePrimitive("floor", 1, 1, a => RoundTo(CheckReal(a[0], "floor", 1), Math.Floor));
        interpreter.DefinePrimitive("ceiling", 1, 1, a => RoundTo(CheckReal(a[0], "ceiling", 1), Math.Ceiling));
        interpreter.DefinePrimitive("truncate", 1, 1, a => RoundTo(CheckReal(a[0], "truncate", 1), Math.Truncate));
        interpreter.DefinePrimitive("round", 1, 1, a => RoundTo(CheckReal(a[0], "round", 1), d => Math.Round(d, MidpointRounding.ToEven)));
        interpreter.DefinePrimitive("numerator", 1, 1, a => RationalPart(CheckReal(a[0], "numerator", 1), true));
        interpreter.DefinePrimitive("denominator", 1, 1, a => RationalPart(CheckReal(a[0], "denominator", 1), false));
    }

    private static void InstallPredicates(Interpreter interpreter, NumericContext context)
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
        interpreter.DefinePrimitive("exact?", 1, 1, a => SchemeNumber.IsExact(Check(a[0], "exact?", 1)));
        interpreter.DefinePrimitive("inexact?", 1, 1, a => !SchemeNumber.IsExact(Check(a[0], "inexact?", 1)));
        interpreter.DefinePrimitive("exact-integer?", 1, 1, a => IsExactInteger(a[0]));
        // zero? takes a complex ((zero? 0.0+0.0i) is #t); positive? and negative? are
        // real-only and refuse one (MEASURED).
        interpreter.DefinePrimitive("zero?", 1, 1, a => Check(a[0], "zero?", 1) is ComplexNumber z
            ? z.Real == 0.0 && z.Imaginary == 0.0
            : SchemeNumber.IsZero(a[0]));
        interpreter.DefinePrimitive("positive?", 1, 1, a => SchemeNumber.Compare(CheckReal(a[0], "positive?", 1), 0L) > 0);
        interpreter.DefinePrimitive("negative?", 1, 1, a => SchemeNumber.Compare(CheckReal(a[0], "negative?", 1), 0L) < 0);

        // even? and odd? take any INTEGER, exact or inexact -- (odd? 2.0) is #f -- and
        // refuse 2.5 or a non-number with the positioned wrong-type-arg (MEASURED).
        interpreter.DefinePrimitive("even?", 1, 1, a => IntegerOf(CheckInteger(a[0], "even?", 1)).IsEven);
        interpreter.DefinePrimitive("odd?", 1, 1, a => !IntegerOf(CheckInteger(a[0], "odd?", 1)).IsEven);

        // Guile's core bitwise family. LilyPond's QR-code generator reaches logxor
        // for its Galois-field arithmetic; the rest of the family travels with it.
        // The bitwise family wants EXACT integers: (logand 2.0 1) is a wrong-type-arg in
        // position 1, and lognot reports itself as `logxor' because that is how libguile
        // implements it. (ash n s) checks its shift through `<' -- (ash 1 "x") reports the
        // call (< "x" 0) -- and a non-integer shift is Guile's unnamed
        // "Wrong type (expecting exact integer)". All MEASURED on the pinned 2.27.2.
        interpreter.DefinePrimitive("logand", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, "logand", (x, y) => x & y, BigInteger.MinusOne)));
        interpreter.DefinePrimitive("logior", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, "logior", (x, y) => x | y, BigInteger.Zero)));
        interpreter.DefinePrimitive("logxor", 0, -1, a =>
            SchemeNumber.Normalize(BitwiseFold(a, "logxor", (x, y) => x ^ y, BigInteger.Zero)));
        interpreter.DefinePrimitive("lognot", 1, 1, a =>
            SchemeNumber.Normalize(~IntegerOf(CheckExactInteger(a[0], "logxor", 1))));
        interpreter.DefinePrimitive("ash", 2, 2, a =>
        {
            BigInteger value = IntegerOf(CheckExactInteger(a[0], "ash", 1));
            if (!SchemeNumber.IsNumber(a[1]))
            {
                // scm_ash asks (< shift 0) first, so a non-number shift fails THERE.
                Binary(interpreter, context.Less, "<", a[1], 0L, (x, y) => SchemeNumber.Compare(x, y) < 0, false);
            }

            if (!IsExactInteger(a[1]))
            {
                throw ExpectingExactInteger(a[1]);
            }

            int shift = (int)SchemeNumber.ToBigInteger(a[1]);
            return SchemeNumber.Normalize(shift >= 0 ? value << shift : value >> -shift);
        });

        // (logcount n) -- Guile counts the 1 bits of a non-negative integer and
        // the 0 bits of a negative one's two's-complement form; both are the
        // population count of (n < 0 ? ~n : n). define-music-callbacks.scm sizes
        // tremolo dot counts with it.
        interpreter.DefinePrimitive("logcount", 1, 1, a =>
        {
            BigInteger value = IntegerOf(CheckExactInteger(a[0], "logcount", 1));
            return SchemeNumber.Normalize(BigInteger.PopCount(value.Sign < 0 ? ~value : value));
        });
        // (logbit? index j) -- is bit number `index` of j set, counting from 0 at the
        // least significant end. BigInteger shifts and masks in two's complement, which
        // is the representation Guile tests against for a negative j too.
        // lily-library.scm's int->bit-list is built on it.
        interpreter.DefinePrimitive("logbit?", 2, 2, a =>
        {
            int index = (int)IntegerOf(CheckExactInteger(a[0], "logbit?", 1));
            BigInteger value = IntegerOf(CheckExactInteger(a[1], "logbit?", 2));
            return !((value >> index) & BigInteger.One).IsZero;
        });

        // (integer-length n) -- the number of bits needed to represent n. libguile's
        // scm_integer_length_i replaces a negative n with (-1 - n) first, so the answer
        // is about the ones-complement magnitude and integer-length of 0 and of -1 are
        // both 0. LilyPond's qr-code.scm sizes its length markers with it.
        interpreter.DefinePrimitive("integer-length", 1, 1, a =>
        {
            BigInteger value = IntegerOf(CheckExactInteger(a[0], "integer-length", 1));
            if (value.Sign < 0)
            {
                value = BigInteger.MinusOne - value;
            }

            return SchemeNumber.Normalize(new BigInteger(value.GetBitLength()));
        });
        // nan?, inf? and finite? all REQUIRE a number (MEASURED: (nan? "x") is a
        // wrong-type-arg in position 1, not #f). Every exact number is finite.
        //was previously: nan? and inf? answered #f for a non-number.
        interpreter.DefinePrimitive("nan?", 1, 1, a => Check(a[0], "nan?", 1) is double d && double.IsNaN(d));
        interpreter.DefinePrimitive("inf?", 1, 1, a => Check(a[0], "inf?", 1) is double d && double.IsInfinity(d));
        interpreter.DefinePrimitive("finite?", 1, 1, a => Check(a[0], "finite?", 1) is double real
            ? !double.IsInfinity(real) && !double.IsNaN(real)
            : true);
        interpreter.DefinePrimitive("inf", 0, 0, a => double.PositiveInfinity);
        interpreter.DefinePrimitive("nan", 0, 0, a => double.NaN);

        interpreter.DefineValue("most-positive-fixnum", SchemeNumber.MostPositiveFixnum);
        interpreter.DefineValue("most-negative-fixnum", SchemeNumber.MostNegativeFixnum);
    }

    private static void InstallConversions(Interpreter interpreter)
    {
        //was previously: exact->inexact threw a .NET ArgumentException on a non-number and
        // inexact->exact answered it unchanged. Both are the positioned wrong-type-arg.
        // A complex is already inexact, so exact->inexact answers it as it stands; inexact->exact
        // of a complex with a ZERO imaginary part is the exact real ((inexact->exact 3.0+0.0i)
        // is 3) and with a non-zero one is the positioned error. MEASURED.
        interpreter.DefinePrimitive("exact->inexact", 1, 1, a => ToInexactNumber(Check(a[0], "exact->inexact", 1)));
        interpreter.DefinePrimitive("inexact->exact", 1, 1, a => ToExactNumber(Check(a[0], "inexact->exact", 1), "inexact->exact"));
        interpreter.DefinePrimitive("exact", 1, 1, a => ToExactNumber(Check(a[0], "exact", 1), "exact"));
        interpreter.DefinePrimitive("inexact", 1, 1, a => ToInexactNumber(Check(a[0], "inexact", 1)));

        // A radix that is not an exact integer is Guile's UNNAMED wrong-type-arg,
        // (wrong-type-arg #f "Wrong type (expecting ~A): ~S" ("exact integer" v) (v)),
        // and a non-string to string->number is the named, expecting form (MEASURED).
        interpreter.DefinePrimitive("number->string", 1, 2, a =>
        {
            int radix = a.Length > 1 ? (int)IntegerOf(RadixOf(a[1])) : 10;
            return new MutableString(SchemeNumber.NumberToString(Check(a[0], "number->string", 1), radix));
        });

        interpreter.DefinePrimitive("string->number", 1, 2, a =>
        {
            if (!(a[0] is MutableString))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("string->number"),
                        new MutableString("Wrong type argument in position ~A (expecting ~A): ~S"),
                        Pair.List(1L, new MutableString("string"), a[0]),
                        Pair.List(a[0])));
            }

            string text = a[0].ToString();
            if (a.Length > 1)
            {
                int radix = (int)IntegerOf(RadixOf(a[1]));
                string prefix = radix == 16 ? "#x" : radix == 8 ? "#o" : radix == 2 ? "#b" : string.Empty;
                text = prefix + text;
            }

            object parsed = SchemeReader.ParseNumber(text);
            return parsed ?? (object)false;
        });
    }

    private static object Lcm(object a, object b)
    {
        BigInteger x = BigInteger.Abs(IntegerOf(a));
        BigInteger y = BigInteger.Abs(IntegerOf(b));
        if (x.IsZero || y.IsZero)
        {
            return 0L;
        }

        return SchemeNumber.Normalize(x / BigInteger.GreatestCommonDivisor(x, y) * y);
    }

    private static object Expt(Interpreter interpreter, NumericContext context, object[] arguments)
    {
        object baseValue = arguments[0];
        object exponent = arguments[1];

        // scm_expt with an EXACT INTEGER exponent goes through scm_integer_expt, which
        // multiplies through `*' -- so a non-number base with such an exponent fails
        // (or dispatches) as a product: (expt "x" 2) reports (* "x" "x"), (expt "x" 1)
        // is "x" and (expt "x" 0) is 1. Any other shape is expt's own positioned check.
        // MEASURED on the pinned 2.27.2.
        if (!SchemeNumber.IsNumber(baseValue) && IsExactInteger(exponent))
        {
            BigInteger power = IntegerOf(exponent);
            if (power.IsZero)
            {
                return 1L;
            }

            if (power.IsOne)
            {
                return baseValue;
            }

            object product = Binary(interpreter, context.Times, "*", baseValue, baseValue, SchemeNumber.Multiply, true);
            for (BigInteger i = 2; i < BigInteger.Abs(power); i++)
            {
                product = Binary(interpreter, context.Times, "*", product, baseValue, SchemeNumber.Multiply, true);
            }

            return power.Sign < 0
                ? Binary(interpreter, context.Times, "/", 1L, product, SchemeNumber.Divide, false)
                : product;
        }

        Check(baseValue, "expt", 1);
        Check(exponent, "expt", 2);

        if (baseValue is ComplexNumber || exponent is ComplexNumber)
        {
            // An exact-integer power of a complex is repeated multiplication, which keeps
            // (expt +i 2) at exactly -1.0+0.0i as Guile answers it; anything else is
            // exp(w log z).
            if (IsExactInteger(exponent))
            {
                BigInteger power = IntegerOf(exponent);
                object product = 1L;
                for (BigInteger i = 0; i < BigInteger.Abs(power); i++)
                {
                    product = SchemeNumber.Multiply(product, baseValue);
                }

                return power.Sign < 0 ? SchemeNumber.Divide(1L, product) : product;
            }

            return FromComplex(System.Numerics.Complex.Pow(ToComplex(baseValue), ToComplex(exponent)));
        }

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
        // The square root of a complex is a complex ((sqrt -3+4i) is 1.0+2.0i), and of a
        // NEGATIVE real too, inexact even for an exact perfect square: (sqrt -4) is
        // 0.0+2.0i on the pinned 2.27.2.
        //was previously: Math.Sqrt of the double, i.e. +nan.0 for both.
        if (value is ComplexNumber complex)
        {
            return FromComplex(System.Numerics.Complex.Sqrt(ToComplex(complex)));
        }

        if (SchemeNumber.Compare(value, 0L) < 0)
        {
            return new ComplexNumber(0.0, Math.Sqrt(-SchemeNumber.ToDouble(value)));
        }

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
        string procedureName,
        Func<BigInteger, BigInteger, BigInteger> combine,
        BigInteger identity)
    {
        BigInteger result = identity;
        for (int i = 0; i < arguments.Length; i++)
        {
            // Pairwise like the arithmetic folds: the accumulator is position 1 of every
            // pair after the first, so a later offender always reports position 2.
            result = combine(result, IntegerOf(CheckExactInteger(arguments[i], procedureName, i == 0 ? 1 : 2)));
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

    /// <summary>
    /// ONE interpreter's arithmetic and comparison primitives, for the primitives that
    /// dispatch through them: <c>1+</c> and <c>1-</c> are <c>(+ x 1)</c> and <c>(- x 1)</c>,
    /// <c>expt</c> with an exact-integer exponent multiplies through <c>*</c>, and
    /// <c>ash</c> tests its shift through <c>&lt;</c>. Captured by those primitives' closures
    /// at install time, so each interpreter dispatches through ITS OWN attached generics.
    /// <para>//was previously (briefly, 2026-08-28): a static holder, which a second live
    /// interpreter in the same process overwrote — the parallel test run found it.</para>
    /// </summary>
    private sealed class NumericContext
    {
        public NumericContext(Primitive plus, Primitive minus, Primitive times)
        {
            Plus = plus;
            Minus = minus;
            Times = times;
        }

        public Primitive Plus { get; }

        public Primitive Minus { get; }

        public Primitive Times { get; }

        public Primitive Less { get; set; }
    }

    private static bool NumericEquals(object x, object y)
    {
        if (x is ComplexNumber || y is ComplexNumber)
        {
            ComplexNumber a = x as ComplexNumber ?? new ComplexNumber(SchemeNumber.ToDouble(x), 0.0);
            ComplexNumber b = y as ComplexNumber ?? new ComplexNumber(SchemeNumber.ToDouble(y), 0.0);
            return a.Real == b.Real && a.Imaginary == b.Imaginary;
        }

        return SchemeNumber.Compare(x, y) == 0;
    }

    private static Primitive DefineComparison(
        Interpreter interpreter,
        string name,
        Func<object, object, bool> comparison,
        bool alwaysPositionOne,
        Func<object, bool> accepts)
    {
        Primitive self = null;
        self = interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            if (a.Length == 1)
            {
                // scm_i_num_less_p and relatives with one argument answer #t without a
                // type check: (< "x") is #t on the pinned 2.27.2.
                return true;
            }

            object x = a[0];
            for (int i = 1; i < a.Length; i++)
            {
                object y = a[i];
                bool xOk = accepts(x);
                bool yOk = accepts(y);
                object verdict;
                if (xOk && yOk)
                {
                    verdict = comparison(x, y);
                }
                else
                {
                    object bad = xOk ? y : x;
                    verdict = FallOver(interpreter, self, name, new[] { x, y }, alwaysPositionOne ? 1 : xOk ? 2 : 1, bad);
                }

                if (!Evaluator.IsTrue(verdict))
                {
                    return false;
                }

                x = y;
            }

            return true;
        });

        return self;
    }

    private static void DefineExtremum(Interpreter interpreter, string name, int sign)
    {
        Primitive self = null;
        self = interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            if (a.Length == 1)
            {
                return IsReal(a[0]) ? a[0] : FallOver(interpreter, self, name, a, 1, a[0]);
            }

            // Pairwise, like scm_max: the running best is position 1 of every later pair.
            // R7RS: if any argument is inexact, the result is inexact.
            object best = a[0];
            bool inexact = a[0] is double;
            for (int i = 1; i < a.Length; i++)
            {
                object candidate = a[i];
                inexact |= candidate is double;
                bool bestOk = IsReal(best);
                bool candidateOk = IsReal(candidate);
                if (bestOk && candidateOk)
                {
                    if (SchemeNumber.Compare(candidate, best) * sign > 0)
                    {
                        best = candidate;
                    }
                }
                else
                {
                    best = FallOver(interpreter, self, name, new[] { best, candidate }, bestOk ? 2 : 1, bestOk ? candidate : best);
                }
            }

            return inexact && IsReal(best) ? SchemeNumber.ToInexact(best) : best;
        });
    }

    /// <summary>
    /// Folds an n-ary arithmetic primitive pairwise, each pair a primitive-generic call
    /// (<see cref="Binary"/>), as Guile's VM does for <c>+</c>, <c>*</c>, <c>gcd</c> and
    /// <c>lcm</c>. One argument answers itself when it is a number and falls over to the
    /// generic otherwise (<c>SCM_WTA_DISPATCH_1</c>); none answers the identity.
    /// </summary>
    private static object FoldDispatch(
        Interpreter interpreter,
        Primitive self,
        string name,
        object[] arguments,
        object identity,
        Func<object, object, object> operation,
        bool multiplicative,
        Func<object, bool> accepts = null)
    {
        if (arguments.Length == 0)
        {
            return identity;
        }

        Func<object, bool> ok = accepts ?? SchemeNumber.IsNumber;
        object result = arguments[0];
        if (arguments.Length == 1)
        {
            return ok(result) ? result : FallOver(interpreter, self, name, arguments, 1, result);
        }

        for (int i = 1; i < arguments.Length; i++)
        {
            result = Binary(interpreter, self, name, result, arguments[i], operation, multiplicative, accepts);
        }

        return result;
    }

    /// <summary>
    /// One pair of a generic-capable arithmetic primitive: the operation when both operands
    /// apply, otherwise the fall-over to the attached generic or Guile's positioned error.
    /// For <c>*</c>, an EXACT 1 is the universal multiplicative identity and is honoured
    /// BEFORE any type check — <c>(* 1 "x")</c> and <c>(* "x" 1)</c> both answer <c>"x"</c>
    /// on the pinned 2.27.2, because <c>scm_product</c> tests for it first.
    /// </summary>
    private static object Binary(
        Interpreter interpreter,
        Primitive self,
        string name,
        object x,
        object y,
        Func<object, object, object> operation,
        bool multiplicative,
        Func<object, bool> accepts = null)
    {
        if (multiplicative)
        {
            if (IsExactOne(x))
            {
                return y;
            }

            if (IsExactOne(y))
            {
                return x;
            }
        }

        Func<object, bool> ok = accepts ?? SchemeNumber.IsNumber;
        bool xOk = ok(x);
        bool yOk = ok(y);
        if (xOk && yOk)
        {
            object result = operation(x, y);

            // Only the INTEGER folds (gcd, lcm -- the ones that pass their own `accepts')
            // compute exactly and then owe inexact contagion: (gcd 4.0 2) is 2.0. The
            // arithmetic operators' own SchemeNumber operations already answer in the
            // right exactness, and a complex result must never reach ToInexact.
            return accepts != null && (x is double || y is double) && !(result is ComplexNumber)
                ? SchemeNumber.ToInexact(result)
                : result;
        }

        return FallOver(interpreter, self, name, new[] { x, y }, xOk ? 2 : 1, xOk ? y : x);
    }

    /// <summary>
    /// <c>SCM_WTA_DISPATCH</c>: what a generic-capable primitive does when its own arguments
    /// fail — apply the attached generic's applicable method, raise Guile's
    /// <c>goops-error</c> when the generic has none, and raise the positioned
    /// <c>wrong-type-arg</c> when no generic has been attached at all.
    /// </summary>
    private static object FallOver(
        Interpreter interpreter, Primitive self, string name, object[] arguments, int badPosition, object badValue)
    {
        if (self != null && self.AttachedGeneric is GenericFunction generic)
        {
            GenericMethod method = generic.Select(arguments);
            if (method != null)
            {
                return interpreter.Evaluator.Apply(method.Implementation, arguments);
            }

            throw PrimitiveGenerics.NoApplicableMethod(generic, name, arguments);
        }

        throw WrongType(name, badPosition, badValue);
    }

    /// <summary>
    /// Guile's numeric <c>wrong-type-arg</c>, MEASURED on the pinned 2.27.2 for every
    /// primitive in this file: <c>(NAME "Wrong type argument in position ~A: ~S" (POS VALUE)
    /// (VALUE))</c> — the message is a template, the position and value are its arguments,
    /// and the data slot holds the value in a list.
    /// <para>
    /// //was previously: <c>(NAME "Wrong type argument: ~S" (VALUE) #f)</c>, and for the
    /// four arithmetic operators the name was the invented word "arithmetic" (and
    /// "comparison" for the five comparisons). Changed 2026-08-28 (two-mode item 4a).
    /// </para>
    /// </summary>
    private static SchemeThrow WrongType(string procedureName, int position, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument in position ~A: ~S"),
                Pair.List((long)position, value),
                Pair.List(value)));

    /// <summary>
    /// Guile's UNNAMED expectation error — <c>(wrong-type-arg #f "Wrong type (expecting ~A): ~S"
    /// ("exact integer" VALUE) (VALUE))</c> — which is what a radix or a shift count that is
    /// not an exact integer raises (MEASURED).
    /// </summary>
    private static SchemeThrow ExpectingExactInteger(object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                false,
                new MutableString("Wrong type (expecting ~A): ~S"),
                Pair.List(new MutableString("exact integer"), value),
                Pair.List(value)));

    private static object Check(object value, string procedureName, int position)
        => SchemeNumber.IsNumber(value) ? value : throw WrongType(procedureName, position, value);

    /// <summary>A REAL number — what the ordered comparisons, abs, the rounding family and
    /// their kin accept; a complex is refused with the positioned error, as Guile refuses it.</summary>
    private static object CheckReal(object value, string procedureName, int position)
        => IsReal(value) ? value : throw WrongType(procedureName, position, value);

    private static bool IsReal(object value)
        => SchemeNumber.IsNumber(value) && !(value is ComplexNumber);

    private static double RealOf(object value, string procedureName, int position)
        => SchemeNumber.ToDouble(CheckReal(value, procedureName, position));

    private static System.Numerics.Complex ToComplex(object value)
        => value is ComplexNumber z
            ? new System.Numerics.Complex(z.Real, z.Imaginary)
            : new System.Numerics.Complex(SchemeNumber.ToDouble(value), 0.0);

    private static object FromComplex(System.Numerics.Complex value)
        => new ComplexNumber(value.Real, value.Imaginary);

    private static object Transcendental(
        object value,
        string procedureName,
        Func<double, double> real,
        Func<System.Numerics.Complex, System.Numerics.Complex> complex)
    {
        Check(value, procedureName, 1);
        return value is ComplexNumber z
            ? FromComplex(complex(new System.Numerics.Complex(z.Real, z.Imaginary)))
            : real(SchemeNumber.ToDouble(value));
    }

    private static object LogBase(object value, object baseValue)
    {
        Check(value, "log", 1);
        Check(baseValue, "log", 2);
        if (value is ComplexNumber || baseValue is ComplexNumber)
        {
            return FromComplex(System.Numerics.Complex.Log(ToComplex(value)) / System.Numerics.Complex.Log(ToComplex(baseValue)));
        }

        return Math.Log(SchemeNumber.ToDouble(value)) / Math.Log(SchemeNumber.ToDouble(baseValue));
    }

    private static object ToInexactNumber(object value)
        => value is ComplexNumber ? value : SchemeNumber.ToInexact(value);

    private static object ToExactNumber(object value, string procedureName)
    {
        if (value is ComplexNumber z)
        {
            return z.Imaginary == 0.0 ? SchemeNumber.ToExact(z.Real) : throw WrongType(procedureName, 1, value);
        }

        return SchemeNumber.ToExact(value);
    }

    /// <summary>An integer, exact or inexact: what quotient, gcd, even? and their kin accept.</summary>
    private static object CheckInteger(object value, string procedureName, int position)
        => IsIntegerValue(value) ? value : throw WrongType(procedureName, position, value);

    /// <summary>An EXACT integer: what the bitwise family accepts.</summary>
    private static object CheckExactInteger(object value, string procedureName, int position)
        => IsExactInteger(value) ? value : throw WrongType(procedureName, position, value);

    private static bool IsIntegerValue(object value)
        => SchemeNumber.IsNumber(value) && !(value is ComplexNumber) && SchemeNumber.IsInteger(value);

    private static bool IsExactInteger(object value)
        => value is long || value is BigInteger || value is int;

    private static bool IsExactOne(object value)
        => (value is long l && l == 1) || (value is int i && i == 1) || (value is BigInteger b && b.IsOne);

    private static BigInteger IntegerOf(object value)
        => value is double d ? new BigInteger(d) : SchemeNumber.ToBigInteger(value);

    private static object RadixOf(object value)
        => IsExactInteger(value) ? value : throw ExpectingExactInteger(value);

    private static object IntegerGcd(object a, object b)
        => SchemeNumber.GreatestCommonDivisor(
            SchemeNumber.Normalize(IntegerOf(a)), SchemeNumber.Normalize(IntegerOf(b)));

    /// <summary>
    /// numerator / denominator: of an exact rational, the part; of an inexact rational,
    /// the part of its exact form made inexact again — <c>(numerator 1.5)</c> is
    /// <c>3.0</c>, <c>(denominator 1.5)</c> is <c>2.0</c> (Guile).
    /// </summary>
    private static object RationalPart(object value, bool numerator)
    {
        if (value is double d)
        {
            object exact = SchemeNumber.ToExact(d);
            object part = exact is Ratio er
                ? SchemeNumber.Normalize(numerator ? er.Numerator : er.Denominator)
                : numerator ? exact : 1L;
            return SchemeNumber.ToInexact(part);
        }

        if (value is Ratio r)
        {
            return SchemeNumber.Normalize(numerator ? r.Numerator : r.Denominator);
        }

        return numerator ? value : 1L;
    }
}
