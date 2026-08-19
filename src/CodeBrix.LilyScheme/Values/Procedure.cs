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
/// An object that is APPLICABLE without being a <see cref="Procedure"/>.
/// <para>
/// Guile lets a smob declare an apply hook — <c>scm_set_smob_apply</c>, which the
/// <c>SCM_SMOB_APPLY</c>/<c>LY_DECLARE_SMOB_PROC</c> family wraps — so a host object can
/// sit in operator position and still be its own type for every predicate. This is the
/// managed equivalent: an embedder implements it on a value type of its own, and both the
/// evaluator's apply path and <c>procedure?</c> accept it, exactly as Guile's do.
/// </para>
/// </summary>
public interface IApplicable
{
    /// <summary>Invokes the object.</summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <returns>The result value.</returns>
    object Apply(object[] arguments);
}

/// <summary>
/// A host object that <c>equal?</c> compares BY VALUE rather than by identity.
/// <para>
/// Guile lets a smob declare an equality handler, and <c>scm_equal_p</c> dispatches to
/// it after its own built-in cases; a smob that declares none falls back to <c>eq?</c>,
/// so identity is the DEFAULT and value equality is opt-in. This is the managed
/// equivalent, and it is opt-in for the same reason — implementing it on a type whose
/// upstream counterpart has no handler would make <c>equal?</c> answer <c>#t</c> where
/// upstream answers <c>#f</c>, which is a divergence in the harder-to-notice direction.
/// </para>
/// <para>
/// The hook is consulted only when both operands implement it; <c>equal?</c> is
/// symmetric, and asking one side about a value that cannot answer back is how an
/// asymmetric comparison creeps in.
/// </para>
/// </summary>
public interface ISchemeEqual
{
    /// <summary>Compares this object with another for <c>equal?</c>.</summary>
    /// <param name="other">The value to compare against; never <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    bool SchemeEquals(object other);
}

/// <summary>
/// A host object that prints with an external representation of its own, rather than
/// through <see cref="object.ToString"/>.
/// <para>
/// Guile lets a smob declare a print hook — <c>scm_set_smob_print</c>, which LilyPond
/// spells <c>print_smob</c> — and the printer calls it for both <c>write</c> and
/// <c>display</c>. This is the managed equivalent. It is a SEPARATE surface from
/// <see cref="object.ToString"/> on purpose: upstream's smobs carry both a
/// <c>to_string()</c> that yields the bare content (<c>e'</c>, <c>1</c>) and a
/// <c>print_smob</c> that wraps it (<c>#&lt;Pitch e' &gt;</c>, <c>#&lt;Duration 1 &gt;</c>),
/// and code reads the two through different routes.
/// </para>
/// </summary>
public interface ISchemePrintable
{
    /// <summary>Returns the object's external representation.</summary>
    /// <returns>The representation, as the printer should emit it.</returns>
    string PrintRepresentation();
}

/// <summary>Base class for anything Scheme will accept in operator position.</summary>
public abstract class Procedure
{
    /// <summary>Gets or sets the procedure's name, used in error messages and by <c>procedure-name</c>.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the procedure's property alist, as read by <c>procedure-property</c>.</summary>
    public object Properties { get; set; } = Nil.Instance;

    /// <summary>
    /// Gets or sets the procedure invoked by Guile's generalized <c>set!</c>, or
    /// <see langword="null"/> when this procedure has no setter.
    /// <para>
    /// <c>(set! (proc args ...) value)</c> expands to <c>((setter proc) args ... value)</c>,
    /// so the setter has to travel with the procedure rather than be looked up by name.
    /// </para>
    /// </summary>
    public object Setter { get; set; }

    /// <summary>
    /// Gets the name Guile answers for this procedure: the <c>name</c> PROCEDURE
    /// PROPERTY when one has been set, and otherwise the name the definition gave it.
    /// <para>
    /// The property comes first because <c>scm_procedure_name</c> consults it first, and
    /// code relies on being able to name a procedure after the fact.
    /// LilyPond's <c>define-markup-command-internal</c>
    /// (<c>scm/markup-macros.scm:242-247</c>) is the case that matters: the markup command
    /// is built by a helper and is therefore anonymous, so the macro names it with
    /// <c>(set-procedure-property! definition 'name command-name)</c>. Reading only the
    /// definition-time name leaves every markup command anonymous, which is invisible
    /// until something PRINTS one.
    /// </para>
    /// </summary>
    public string EffectiveName
    {
        get
        {
            foreach (object entry in Pair.ToList(Properties))
            {
                if (entry is Pair pair
                    && pair.Car is Symbol key
                    && string.Equals(key.Name, "name", StringComparison.Ordinal))
                {
                    if (pair.Cdr is Symbol value)
                    {
                        return value.Name;
                    }

                    if (pair.Cdr is MutableString text)
                    {
                        return text.ToString();
                    }
                }
            }

            return Name;
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the procedure name when known.</returns>
    public override string ToString()
        => "#<procedure " + (EffectiveName ?? "anonymous") + ">";
}

/// <summary>
/// A procedure implemented in C#. The delegate receives the evaluated arguments and
/// returns a Scheme value.
/// </summary>
public sealed class Primitive : Procedure
{
    private readonly Func<object[], object> _implementation;

    /// <summary>Initializes a primitive.</summary>
    /// <param name="name">The Scheme-visible name.</param>
    /// <param name="minimumArgumentCount">The smallest acceptable argument count.</param>
    /// <param name="maximumArgumentCount">The largest acceptable count, or -1 when variadic.</param>
    /// <param name="implementation">The C# implementation.</param>
    public Primitive(string name, int minimumArgumentCount, int maximumArgumentCount, Func<object[], object> implementation)
    {
        Name = name;
        MinimumArgumentCount = minimumArgumentCount;
        MaximumArgumentCount = maximumArgumentCount;
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
    }

    /// <summary>Gets the smallest acceptable argument count.</summary>
    public int MinimumArgumentCount { get; }

    /// <summary>Gets the largest acceptable argument count, or -1 when variadic.</summary>
    public int MaximumArgumentCount { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this primitive may dispatch to a generic
    /// when its arguments fail to apply — Guile's <c>SCM_PRIMITIVE_GENERIC</c> declaration,
    /// which is exactly what <c>generic-capability?</c> reports.
    /// </summary>
    public bool IsGenericCapable { get; set; }

    /// <summary>
    /// Gets or sets the generic attached by <c>enable-primitive-generic!</c>, or
    /// <see langword="null"/> when nothing has extended this primitive yet.
    /// <para>
    /// The generic hangs off the PRIMITIVE rather than off any module binding, which is
    /// what makes a <c>define-method</c> global: every module that imports the core sees
    /// this one object, so specializing <c>-</c> anywhere extends subtraction everywhere.
    /// </para>
    /// </summary>
    public object AttachedGeneric { get; set; }

    /// <summary>Invokes the primitive.</summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <returns>The result value.</returns>
    public object Invoke(object[] arguments)
    {
        //was previously: => _implementation(arguments);
        // A bare cast inside a primitive's body is that primitive's argument type
        // check failing the .NET way. Guile raises a catchable `wrong-type-arg' for
        // the same mistake (libguile's scm_wrong_type_arg), and Scheme code
        // legitimately catches that key — so the raw InvalidCastException must not
        // escape to the host, where no `catch' can see it. Primitives with checked
        // accessors (Primitives.TypeChecks, StringPrimitives.Text) raise the
        // POSITIONED error themselves and never reach this net; this is the
        // last-resort translation for any site still casting bare, at the one place
        // every primitive passes through. A SchemeThrow from a NESTED primitive is
        // not an InvalidCastException and passes through untouched, keeping the
        // inner primitive's own attribution.
        try
        {
            return _implementation(arguments);
        }
        catch (InvalidCastException)
        {
            throw new Runtime.SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(
                    new MutableString(Name ?? "primitive"),
                    new MutableString("Wrong type argument"),
                    Nil.Instance,
                    false));
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the primitive name.</returns>
    public override string ToString() => "#<primitive " + (Name ?? "anonymous") + ">";
}

/// <summary>
/// One optional or keyword parameter of a <c>lambda*</c> signature: a name, the
/// expression producing its default, and — for keyword parameters — the keyword that
/// selects it at the call site.
/// </summary>
public sealed class OptionalParameter
{
    /// <summary>Initializes an optional or keyword parameter.</summary>
    /// <param name="name">The bound variable name.</param>
    /// <param name="defaultExpression">The default-value expression, or <see langword="null"/>.</param>
    /// <param name="keyword">The selecting keyword for keyword parameters, or <see langword="null"/>.</param>
    public OptionalParameter(Symbol name, object defaultExpression, Keyword keyword)
    {
        ParameterName = name;
        DefaultExpression = defaultExpression;
        SelectingKeyword = keyword;
    }

    /// <summary>Gets the bound variable name.</summary>
    public Symbol ParameterName { get; }

    /// <summary>Gets the expression evaluated to produce the default, or <see langword="null"/>.</summary>
    public object DefaultExpression { get; }

    /// <summary>Gets the keyword that selects this parameter, or <see langword="null"/> for positional optionals.</summary>
    public Keyword SelectingKeyword { get; }
}

/// <summary>
/// A parsed lambda parameter list. Guile's <c>lambda*</c> — and therefore the
/// <c>lambda-case</c> Tree-IL node — supports required, optional, rest and keyword
/// parameters in one signature, so all four are modelled here.
/// </summary>
public sealed class LambdaSignature
{
    /// <summary>Initializes a signature.</summary>
    /// <param name="required">The required parameter names, in order.</param>
    /// <param name="optionals">The positional optional parameters, in order.</param>
    /// <param name="keywords">The keyword parameters.</param>
    /// <param name="rest">The rest parameter name, or <see langword="null"/>.</param>
    /// <param name="allowOtherKeys">Whether unrecognised keywords are tolerated.</param>
    public LambdaSignature(
        IReadOnlyList<Symbol> required,
        IReadOnlyList<OptionalParameter> optionals,
        IReadOnlyList<OptionalParameter> keywords,
        Symbol rest,
        bool allowOtherKeys)
    {
        Required = required ?? Array.Empty<Symbol>();
        Optionals = optionals ?? Array.Empty<OptionalParameter>();
        Keywords = keywords ?? Array.Empty<OptionalParameter>();
        RestParameter = rest;
        AllowOtherKeys = allowOtherKeys;
    }

    /// <summary>Gets the required parameter names.</summary>
    public IReadOnlyList<Symbol> Required { get; }

    /// <summary>Gets the positional optional parameters.</summary>
    public IReadOnlyList<OptionalParameter> Optionals { get; }

    /// <summary>Gets the keyword parameters.</summary>
    public IReadOnlyList<OptionalParameter> Keywords { get; }

    /// <summary>Gets the rest parameter name, or <see langword="null"/> when absent.</summary>
    public Symbol RestParameter { get; }

    /// <summary>Gets a value indicating whether unrecognised keywords are tolerated.</summary>
    public bool AllowOtherKeys { get; }

    /// <summary>Gets a value indicating whether this signature has only required parameters.</summary>
    public bool IsSimple
        => Optionals.Count == 0 && Keywords.Count == 0 && RestParameter == null;
}
