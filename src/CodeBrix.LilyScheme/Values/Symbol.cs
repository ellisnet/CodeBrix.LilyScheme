// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Concurrent;

namespace CodeBrix.LilyScheme.Values;

/// <summary>
/// An interned Scheme symbol. Symbols with the same name are always reference-equal,
/// which is what makes <c>eq?</c> a valid symbol comparison and what allows symbols to
/// be used as hash-table keys under <c>hashq</c>.
/// </summary>
public sealed class Symbol
{
    private static readonly ConcurrentDictionary<string, Symbol> Interned
        = new ConcurrentDictionary<string, Symbol>(StringComparer.Ordinal);

    private static long _gensymCounter;

    private Symbol(string name, bool isUninterned)
    {
        Name = name;
        IsUninterned = isUninterned;
    }

    /// <summary>Gets the symbol's printed name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether this symbol was produced by <c>gensym</c> and is
    /// therefore absent from the intern table. Uninterned symbols never compare
    /// <c>eq?</c> to a symbol read from source, which is what makes them usable as
    /// hygiene markers.
    /// </summary>
    public bool IsUninterned { get; }

    /// <summary>Returns the interned symbol with the given name, creating it if needed.</summary>
    /// <param name="name">The symbol name. Must not be null.</param>
    /// <returns>The unique <see cref="Symbol"/> for <paramref name="name"/>.</returns>
    public static Symbol Intern(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return Interned.GetOrAdd(name, static key => new Symbol(key, false));
    }

    /// <summary>
    /// Creates a fresh uninterned symbol. Guile's <c>gensym</c> and <c>module-gensym</c>
    /// are both built on this; psyntax uses them to generate hygienic renamings.
    /// </summary>
    /// <param name="prefix">Prefix for the generated name.</param>
    /// <returns>A new symbol that is not in the intern table.</returns>
    public static Symbol Generate(string prefix)
    {
        long n = System.Threading.Interlocked.Increment(ref _gensymCounter);
        return new Symbol((prefix ?? " g") + n.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
    }

    /// <summary>
    /// Recreates an uninterned symbol with an exact name, for expansion-cache
    /// deserialization. Uninterned symbols compare by identity, never by name, so a
    /// recreated gensym can never collide with one generated live — the deserializer
    /// only has to preserve identity WITHIN the recorded graph, which it does by
    /// creating each recorded gensym exactly once.
    /// </summary>
    /// <param name="name">The recorded name.</param>
    /// <returns>A fresh uninterned symbol.</returns>
    internal static Symbol CreateUninterned(string name)
    {
        return new Symbol(name, true);
    }

    /// <summary>Returns the symbol's name.</summary>
    /// <returns>The printed representation.</returns>
    public override string ToString() => Name;

    // Frequently used symbols, interned once so the evaluator can compare by reference
    // instead of by string on every dispatch.

    /// <summary>The <c>quote</c> symbol.</summary>
    public static readonly Symbol Quote = Intern("quote");

    /// <summary>The <c>quasiquote</c> symbol.</summary>
    public static readonly Symbol Quasiquote = Intern("quasiquote");

    /// <summary>The <c>unquote</c> symbol.</summary>
    public static readonly Symbol Unquote = Intern("unquote");

    /// <summary>The <c>unquote-splicing</c> symbol.</summary>
    public static readonly Symbol UnquoteSplicing = Intern("unquote-splicing");

    /// <summary>The <c>lambda</c> symbol.</summary>
    public static readonly Symbol Lambda = Intern("lambda");

    /// <summary>The <c>lambda*</c> symbol.</summary>
    public static readonly Symbol LambdaStar = Intern("lambda*");

    /// <summary>The <c>define</c> symbol.</summary>
    public static readonly Symbol Define = Intern("define");

    /// <summary>The <c>if</c> symbol.</summary>
    public static readonly Symbol If = Intern("if");

    /// <summary>The <c>set!</c> symbol.</summary>
    public static readonly Symbol SetBang = Intern("set!");

    /// <summary>The <c>begin</c> symbol.</summary>
    public static readonly Symbol Begin = Intern("begin");

    /// <summary>The <c>let</c> symbol.</summary>
    public static readonly Symbol Let = Intern("let");

    /// <summary>The <c>let*</c> symbol.</summary>
    public static readonly Symbol LetStar = Intern("let*");

    /// <summary>The <c>letrec</c> symbol.</summary>
    public static readonly Symbol Letrec = Intern("letrec");

    /// <summary>The <c>letrec*</c> symbol.</summary>
    public static readonly Symbol LetrecStar = Intern("letrec*");

    /// <summary>The <c>and</c> symbol.</summary>
    public static readonly Symbol And = Intern("and");

    /// <summary>The <c>or</c> symbol.</summary>
    public static readonly Symbol Or = Intern("or");

    /// <summary>The <c>cond</c> symbol.</summary>
    public static readonly Symbol Cond = Intern("cond");

    /// <summary>The <c>case</c> symbol.</summary>
    public static readonly Symbol Case = Intern("case");

    /// <summary>The <c>else</c> symbol, used by <c>cond</c> and <c>case</c>.</summary>
    public static readonly Symbol Else = Intern("else");

    /// <summary>The <c>=&gt;</c> symbol, used by <c>cond</c> clauses.</summary>
    public static readonly Symbol Arrow = Intern("=>");

    /// <summary>The <c>eval-when</c> symbol.</summary>
    public static readonly Symbol EvalWhen = Intern("eval-when");

    /// <summary>The <c>when</c> symbol.</summary>
    public static readonly Symbol When = Intern("when");

    /// <summary>The <c>unless</c> symbol.</summary>
    public static readonly Symbol Unless = Intern("unless");

    /// <summary>The <c>case-lambda</c> symbol.</summary>
    public static readonly Symbol CaseLambda = Intern("case-lambda");

    /// <summary>The <c>define-syntax</c> symbol.</summary>
    public static readonly Symbol DefineSyntax = Intern("define-syntax");

    /// <summary>The <c>do</c> symbol.</summary>
    public static readonly Symbol Do = Intern("do");

    /// <summary>The <c>delay</c> symbol.</summary>
    public static readonly Symbol Delay = Intern("delay");

    /// <summary>The <c>#:optional</c> lambda* marker.</summary>
    public static readonly Symbol OptionalMarker = Intern("#:optional");

    /// <summary>The <c>#:key</c> lambda* marker.</summary>
    public static readonly Symbol KeyMarker = Intern("#:key");

    /// <summary>The <c>#:rest</c> lambda* marker.</summary>
    public static readonly Symbol RestMarker = Intern("#:rest");

    /// <summary>The <c>#:allow-other-keys</c> lambda* marker.</summary>
    public static readonly Symbol AllowOtherKeysMarker = Intern("#:allow-other-keys");
}
