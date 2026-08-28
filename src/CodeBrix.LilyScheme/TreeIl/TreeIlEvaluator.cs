// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.TreeIl;

/// <summary>
/// A procedure whose body is Tree-IL rather than source. Produced when a
/// <c>lambda</c> node is evaluated.
/// </summary>
public sealed class TreeIlClosure : Procedure
{
    /// <summary>Initializes a Tree-IL closure.</summary>
    /// <param name="lambdaCase">The <c>lambda-case</c> node holding the parameter list and body.</param>
    /// <param name="environment">The captured lexical environment.</param>
    /// <param name="module">The module the closure was defined in.</param>
    public TreeIlClosure(SchemeStruct lambdaCase, LexicalEnvironment environment, SchemeModule module)
    {
        LambdaCase = lambdaCase;
        Environment = environment;
        Module = module;
    }

    /// <summary>Gets the <c>lambda-case</c> node.</summary>
    public SchemeStruct LambdaCase { get; }

    /// <summary>Gets the captured lexical environment.</summary>
    public LexicalEnvironment Environment { get; }

    /// <summary>Gets the module the closure was defined in.</summary>
    public SchemeModule Module { get; }

    /// <summary>
    /// Gets or sets the closure's docstring, or <see langword="null"/> when it has none.
    /// <para>
    /// A string as the first of several body forms is a DOCSTRING, and psyntax has
    /// already lifted it out of the body by the time Tree-IL exists: its
    /// <c>parse-body</c> (<c>ice-9/psyntax.scm:2088-2094</c>) moves it into the lambda's
    /// META alist under the key <c>documentation</c>. Reading it back out is all this
    /// takes — the alternative, inspecting the body at apply time, would also have to
    /// know not to return the string for a one-form lambda whose body IS a string.
    /// </para>
    /// </summary>
    public string Documentation { get; set; }

    /// <summary>
    /// Gets or sets where the <c>lambda</c> was read from, or <see langword="null"/> when
    /// nothing recorded it.
    /// <para>
    /// psyntax threads this through expansion as the <c>src</c> field of every Tree-IL
    /// node — a <c>#(filename line column)</c> vector it builds out of the reader's
    /// source properties. It is what lets an ANONYMOUS procedure still say where it came
    /// from, which is the whole of Guile's <c>#&lt;procedure at file:line:col (args)&gt;</c>.
    /// </para>
    /// </summary>
    public Reader.SourceLocation Source { get; set; }

    /// <summary>
    /// Returns the procedure's parameter list as Guile's printer shows it, for example
    /// <c>(layout props args)</c> or <c>(a #:optional b)</c>.
    /// <para>
    /// This is <c>arguments-alist-&gt;lambda-list</c> (<c>system/vm/program.scm:225-234</c>):
    /// the required names, then <c>#:optional</c> and the optionals, then <c>#:key</c> and
    /// the keyword names, then a rest parameter as an improper tail. The names are the
    /// SOURCE names from the <c>lambda-case</c> node, not the gensyms the body is bound
    /// through — the gensyms are what make expansion hygienic and would print as noise.
    /// </para>
    /// </summary>
    /// <returns>The parameter list.</returns>
    public string LambdaList()
    {
        object[] fields = LambdaCase.Fields;
        List<string> items = new List<string>();
        foreach (object name in Pair.ToList(fields[1]))
        {
            items.Add(Runtime.Printer.Display(name));
        }

        List<object> optionals = fields[2] is Pair || fields[2] is Nil
            ? Pair.ToList(fields[2])
            : new List<object>();
        if (optionals.Count > 0)
        {
            items.Add("#:optional");
            foreach (object name in optionals)
            {
                items.Add(Runtime.Printer.Display(name));
            }
        }

        if (fields[4] is Pair keywordSpec)
        {
            List<object> keywords = Pair.ToList(keywordSpec.Cdr);
            if (keywords.Count > 0)
            {
                items.Add("#:key");
                foreach (object entry in keywords)
                {
                    List<object> parts = Pair.ToList(entry);
                    if (parts.Count >= 2)
                    {
                        items.Add(Runtime.Printer.Display(parts[1]));
                    }
                }
            }
        }

        string rest = fields[3] is Symbol restName ? restName.Name : null;

        // Guile builds `(,@req ,@opt ,@key . ,rest) with rest defaulting to '(), so a
        // procedure that takes ONLY a rest argument has a lambda list that is not a list
        // at all — (lambda args ...) prints its formals as a bare `args'.
        if (rest != null && items.Count == 0)
        {
            return rest;
        }

        string joined = string.Join(" ", items);
        return rest == null ? "(" + joined + ")" : "(" + joined + " . " + rest + ")";
    }
}

/// <summary>
/// Evaluates Guile's Tree-IL, the intermediate language psyntax produces.
/// <para>
/// Guile's <c>macroexpand</c> does not return s-expressions; it returns structs built
/// from the <c>%expanded-vtables</c> vector. Interpreting those structs directly is
/// Guile's own architecture with the CPS and bytecode stages removed. All eighteen node
/// types from <c>libguile/expand.h</c> are handled here.
/// </para>
/// <para>
/// Note that lexical variables are addressed by their <c>gensym</c>, not by their source
/// name — that is precisely what makes the expander's output hygienic, since two
/// distinct bindings that share a source name get distinct gensyms.
/// </para>
/// </summary>
public sealed class TreeIlEvaluator
{
    private readonly Interpreter _interpreter;

    /// <summary>Initializes a Tree-IL evaluator.</summary>
    /// <param name="interpreter">The owning interpreter.</param>
    public TreeIlEvaluator(Interpreter interpreter)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
    }

    /// <summary>Evaluates a Tree-IL node.</summary>
    /// <param name="node">The node to evaluate.</param>
    /// <param name="environment">The lexical environment, keyed by gensym.</param>
    /// <param name="module">The module providing top-level bindings.</param>
    /// <returns>The value of the node.</returns>
    public object Eval(object node, LexicalEnvironment environment, SchemeModule module)
    {
        while (true)
        {
            if (!(node is SchemeStruct expression))
            {
                // Not a Tree-IL node: treat as a literal, which covers the case where a
                // caller hands us an already-evaluated value.
                return node;
            }

            int type = ExpandedVtables.IndexOf(expression.Vtable);
            object[] fields = expression.Fields;

            switch (type)
            {
                case ExpandedVtables.Void:
                    return Unspecified.Instance;

                case ExpandedVtables.Const:
                    return fields[1];

                case ExpandedVtables.PrimitiveRef:
                    return LookupTopLevel(_interpreter.GuileModule, (Symbol)fields[1]);

                case ExpandedVtables.LexicalRef:
                {
                    Variable variable = environment?.Lookup((Symbol)fields[2]);
                    if (variable == null)
                    {
                        throw Unbound((Symbol)fields[1]);
                    }

                    return variable.GetValue();
                }

                case ExpandedVtables.LexicalSet:
                {
                    Variable variable = environment?.Lookup((Symbol)fields[2]);
                    if (variable == null)
                    {
                        throw Unbound((Symbol)fields[1]);
                    }

                    variable.SetValue(Eval(fields[3], environment, module));
                    return Unspecified.Instance;
                }

                case ExpandedVtables.ModuleRef:
                    return LookupTopLevel(ResolveModule(fields[1], module), (Symbol)fields[2]);

                case ExpandedVtables.ModuleSet:
                {
                    SchemeModule target = ResolveModule(fields[1], module);
                    target.EnsureVariable((Symbol)fields[2]).SetValue(Eval(fields[4], environment, module));
                    return Unspecified.Instance;
                }

                case ExpandedVtables.ToplevelRef:
                    return LookupTopLevel(ResolveModule(fields[1], module), (Symbol)fields[2]);

                case ExpandedVtables.ToplevelSet:
                {
                    SchemeModule target = ResolveModule(fields[1], module);
                    Symbol name = (Symbol)fields[2];
                    Variable variable = target.Lookup(name) ?? target.EnsureVariable(name);
                    variable.SetValue(Eval(fields[3], environment, module));
                    return Unspecified.Instance;
                }

                case ExpandedVtables.ToplevelDefine:
                {
                    SchemeModule target = ResolveModule(fields[1], module);
                    Symbol name = (Symbol)fields[2];
                    object value = Eval(fields[3], environment, module);

                    // A define does NOT name the value it binds. psyntax has already
                    // done the naming where Guile does it: build-global-definition
                    // (ice-9/psyntax.scm:248-250) passes the expression through
                    // maybe-name-value, which adds a `name' META entry to a LAMBDA and
                    // leaves everything else alone. Naming here as well would also name
                    // a procedure the definition merely COMPUTED --
                    // (define-public format-coda-mark (format-mark-generic '(numbers coda)))
                    // binds a closure built inside another procedure, and Guile answers #f
                    // for its procedure-name. LilyPond's scm->string branches on exactly
                    // that: a named procedure documents itself by name, an unnamed one by
                    // its printed representation.
                    target.Define(name, value);
                    return Symbol.Intern(name.Name);
                }

                case ExpandedVtables.Conditional:
                    node = Evaluator.IsTrue(Eval(fields[1], environment, module)) ? fields[2] : fields[3];
                    continue;

                case ExpandedVtables.Call:
                {
                    object procedure = Eval(fields[1], environment, module);
                    object[] arguments = EvalList(fields[2], environment, module);
                    if (procedure is TreeIlClosure closure)
                    {
                        // Tail call: rebind and loop rather than recursing.
                        environment = BindArguments(closure, arguments, out object body);
                        module = closure.Module;
                        node = body;
                        continue;
                    }

                    return _interpreter.Evaluator.Apply(procedure, arguments);
                }

                case ExpandedVtables.Primcall:
                {
                    object procedure = LookupTopLevel(_interpreter.GuileModule, (Symbol)fields[1]);
                    object[] arguments = EvalList(fields[2], environment, module);
                    if (procedure is TreeIlClosure closure)
                    {
                        environment = BindArguments(closure, arguments, out object body);
                        module = closure.Module;
                        node = body;
                        continue;
                    }

                    return _interpreter.Evaluator.Apply(procedure, arguments);
                }

                case ExpandedVtables.Seq:
                    Eval(fields[1], environment, module);
                    node = fields[2];
                    continue;

                case ExpandedVtables.Lambda:
                {
                    if (!(fields[2] is SchemeStruct lambdaCase))
                    {
                        return Unspecified.Instance;
                    }

                    TreeIlClosure closure = new TreeIlClosure(lambdaCase, environment, module);
                    object name = MetaEntry(fields[1], "name");
                    if (name is Symbol nameSymbol)
                    {
                        closure.Name = nameSymbol.Name;
                    }

                    // Field 0 of every Tree-IL node is its src: psyntax's sourcev, the
                    // #(filename line column) vector built from the reader's source
                    // properties, or #f when nothing was recorded.
                    closure.Source = SourceFromVector(fields[0]);

                    object documentation = MetaEntry(fields[1], "documentation");
                    if (documentation != null)
                    {
                        closure.Documentation = documentation is MutableString text
                            ? text.ToString()
                            : documentation as string;
                    }

                    return closure;
                }

                case ExpandedVtables.LambdaCase:
                    // A bare lambda-case outside a lambda has no captured arguments;
                    // evaluating its body directly is the sensible reading.
                    node = fields[7];
                    continue;

                case ExpandedVtables.Let:
                {
                    List<object> gensyms = Pair.ToList(fields[2]);
                    List<object> values = Pair.ToList(fields[3]);
                    LexicalEnvironment frame = new LexicalEnvironment(environment, gensyms.Count);
                    for (int i = 0; i < gensyms.Count; i++)
                    {
                        object value = i < values.Count ? Eval(values[i], environment, module) : Unspecified.Instance;
                        frame.Define((Symbol)gensyms[i], value);
                    }

                    environment = frame;
                    node = fields[4];
                    continue;
                }

                case ExpandedVtables.Letrec:
                {
                    List<object> gensyms = Pair.ToList(fields[3]);
                    List<object> values = Pair.ToList(fields[4]);
                    LexicalEnvironment frame = new LexicalEnvironment(environment, gensyms.Count);

                    // Every name is visible to every initializer, so reserve the slots
                    // before evaluating any of them.
                    foreach (object gensym in gensyms)
                    {
                        frame.Define((Symbol)gensym, Unspecified.Instance);
                    }

                    for (int i = 0; i < gensyms.Count && i < values.Count; i++)
                    {
                        frame.Lookup((Symbol)gensyms[i]).SetValue(Eval(values[i], frame, module));
                    }

                    environment = frame;
                    node = fields[5];
                    continue;
                }

                default:
                    throw new SchemeEvaluationException(
                        "Unknown Tree-IL node type: " + expression.Vtable.Name);
            }
        }
    }

    /// <summary>
    /// Expands a source form through psyntax and evaluates the resulting Tree-IL. This is
    /// the full pipeline: reader output in, value out.
    /// </summary>
    /// <remarks>
    /// NOTE what this does NOT do: it does not make <paramref name="module"/> current.
    /// psyntax resolves free identifiers against <c>(current-module)</c> at EXPANSION
    /// time, so a caller wanting a form expanded in a module other than the current one
    /// has to set it — see the <c>eval</c> primitive, which is where Guile puts that
    /// excursion. Doing it here instead is wrong and was tried: this is also the
    /// per-form loader path, and a <c>(define-module ...)</c> at the head of a file
    /// takes effect BY changing the current module, so restoring it afterwards silently
    /// undoes the declaration and every later form in the file lands in the caller's
    /// module.
    /// </remarks>
    /// <param name="form">The source form.</param>
    /// <param name="module">The module to evaluate the expanded Tree-IL in.</param>
    /// <returns>The value of the form.</returns>
    public object ExpandAndEval(object form, SchemeModule module)
    {
        return Eval(Expand(form), null, module);
    }

    /// <summary>
    /// Macro-expands a top-level form to Tree-IL through psyntax's <c>macroexpand</c>,
    /// without evaluating the result. This is the expensive half of
    /// <see cref="ExpandAndEval"/> — the half the expansion cache replaces on a
    /// cache hit.
    /// </summary>
    /// <param name="form">The form to expand.</param>
    /// <returns>The expanded Tree-IL.</returns>
    public object Expand(object form)
    {
        return Expand(form, false);
    }

    /// <summary>
    /// Macro-expands a top-level form, optionally in psyntax's <c>c&amp;e</c>
    /// (compile-and-evaluate) mode — upstream's own file-compilation mode, and the one
    /// the expansion cache records in. In the default <c>e</c> mode a top-level
    /// <c>define-syntax</c> installs its macro purely as an expansion-time side effect
    /// and the returned Tree-IL carries nothing, so a REPLAYED boot would rebuild every
    /// value binding but no Scheme-defined macro; <c>c&amp;e</c> makes the expander
    /// evaluate the installation now (exactly as <c>e</c> does) AND emit it into the
    /// returned Tree-IL, so evaluating the recording reinstalls the macro.
    /// </summary>
    /// <param name="form">The form to expand.</param>
    /// <param name="compileAndEval"><c>true</c> to expand in <c>c&amp;e</c> mode.</param>
    /// <returns>The expanded Tree-IL.</returns>
    public object Expand(object form, bool compileAndEval)
    {
        object expander = LookupTopLevel(_interpreter.GuileModule, Symbol.Intern("macroexpand"));
        bool outermost = LoadDiagnostics.EnterExpand();
        long expandStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (compileAndEval)
            {
                return _interpreter.Evaluator.Apply(
                    expander,
                    new object[]
                    {
                        form,
                        Symbol.Intern("c&e"),
                        new Pair(Symbol.Intern("eval"), Nil.Instance),
                    });
            }

            return _interpreter.Evaluator.Apply(expander, new[] { form });
        }
        finally
        {
            LoadDiagnostics.ExitExpand(
                outermost, System.Diagnostics.Stopwatch.GetTimestamp() - expandStart);
        }
    }

    /// <summary>Applies a Tree-IL closure to already-evaluated arguments.</summary>
    /// <param name="closure">The closure to call.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <returns>The result value.</returns>
    public object ApplyClosure(TreeIlClosure closure, object[] arguments)
    {
        LexicalEnvironment frame = BindArguments(closure, arguments, out object body);
        return Eval(body, frame, closure.Module);
    }

    private object[] EvalList(object nodes, LexicalEnvironment environment, SchemeModule module)
    {
        List<object> values = new List<object>();
        object cursor = nodes;
        while (cursor is Pair pair)
        {
            values.Add(Eval(pair.Car, environment, module));
            cursor = pair.Cdr;
        }

        return values.ToArray();
    }

    private LexicalEnvironment BindArguments(TreeIlClosure closure, object[] arguments, out object body)
        => BindArguments(closure, closure, arguments, out body);

    /// <summary>
    /// Binds a call's arguments to a <c>lambda-case</c>'s parameters, chaining to the
    /// case's alternate clause when the count does not fit and raising Guile's
    /// <c>wrong-number-of-args</c> when no clause fits.
    /// <para>
    /// The error is the VM's <c>vm_error_wrong_num_args</c> (<c>libguile/vm.c</c>): key
    /// <c>wrong-number-of-args</c>, subr <c>#f</c>, message
    /// <c>"Wrong number of arguments to ~A"</c>, and the PROCEDURE OBJECT as the one
    /// format argument — which is why Guile's report reads
    /// <c>Wrong number of arguments to #&lt;procedure unfold-repeats (types music)&gt;</c>.
    /// The procedure named is the one the caller applied (<paramref name="applied"/>),
    /// not the alternate clause that finally refused: a <c>case-lambda</c> reports
    /// itself, not its last arm.
    /// </para>
    /// <para>
    /// //was previously: a missing required argument was bound to
    /// <c>Unspecified.Instance</c> and surplus arguments were dropped, so a call with the
    /// wrong arity ran the body anyway. Found 2026-08-28 through LilyPond: Mutopia files
    /// call <c>unfold-repeats</c> from embedded Scheme with its pre-2.23 arity, and where
    /// 2.27.2 refuses the file the port silently engraved it with <c>music</c> unbound.
    /// The classic <see cref="Runtime.Evaluator"/> closure path had checked all along;
    /// this path — every psyntax-expanded procedure — had not.
    /// </para>
    /// </summary>
    /// <param name="applied">The procedure the caller applied, named in the error.</param>
    /// <param name="closure">The clause being tried; the same as <paramref name="applied"/> until an alternate is reached.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="body">Receives the clause's body.</param>
    /// <returns>The frame the body runs in.</returns>
    private LexicalEnvironment BindArguments(
        TreeIlClosure applied, TreeIlClosure closure, object[] arguments, out object body)
    {
        SchemeStruct lambdaCase = closure.LambdaCase;
        object[] fields = lambdaCase.Fields;

        List<object> required = Pair.ToList(fields[1]);
        List<object> optional = fields[2] is Pair || fields[2] is Nil ? Pair.ToList(fields[2]) : new List<object>();
        object rest = fields[3];
        List<object> inits = Pair.ToList(fields[5]);
        List<object> gensyms = Pair.ToList(fields[6]);

        // A lambda-case may chain to an alternate clause when the argument count does
        // not fit, which is how case-lambda and optional arities are represented. A
        // keyword clause has no positional ceiling: its tail is keyword/value pairs,
        // read below.
        int minimum = required.Count;
        int maximum = rest is Symbol || fields[4] is Pair ? int.MaxValue : minimum + optional.Count;
        if (arguments.Length < minimum || arguments.Length > maximum)
        {
            if (fields[8] is SchemeStruct alternate)
            {
                return BindArguments(
                    applied,
                    new TreeIlClosure(alternate, closure.Environment, closure.Module),
                    arguments,
                    out body);
            }

            throw new SchemeThrow(
                Symbol.Intern("wrong-number-of-args"),
                Pair.List(
                    false,
                    new MutableString("Wrong number of arguments to ~A"),
                    Pair.List(applied),
                    false));
        }

        LexicalEnvironment frame = new LexicalEnvironment(closure.Environment, gensyms.Count);
        int slot = 0;
        int argumentIndex = 0;

        for (int i = 0; i < required.Count && slot < gensyms.Count; i++, slot++)
        {
            frame.Define((Symbol)gensyms[slot], arguments[argumentIndex++]);
        }

        for (int i = 0; i < optional.Count && slot < gensyms.Count; i++, slot++)
        {
            if (argumentIndex < arguments.Length)
            {
                frame.Define((Symbol)gensyms[slot], arguments[argumentIndex++]);
            }
            else
            {
                object initializer = i < inits.Count ? inits[i] : null;
                frame.Define(
                    (Symbol)gensyms[slot],
                    initializer == null ? (object)false : Eval(initializer, frame, closure.Module));
            }
        }

        if (rest is Symbol && slot < gensyms.Count)
        {
            object tail = Nil.Instance;
            for (int i = arguments.Length - 1; i >= argumentIndex; i--)
            {
                tail = new Pair(arguments[i], tail);
            }

            frame.Define((Symbol)gensyms[slot], tail);
            slot++;
        }

        // Keyword parameters. Guile encodes the kw field as
        //     #f  |  (allow-other-keys? (keyword name gensym) ...)
        // and the caller supplies them as alternating keyword/value pairs in the
        // positional tail, in any order.
        if (fields[4] is Pair keywordSpec)
        {
            Dictionary<object, object> supplied = new Dictionary<object, object>(ReferenceComparer.Instance);
            for (int i = argumentIndex; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] is Keyword)
                {
                    supplied[arguments[i]] = arguments[i + 1];
                    i++;
                }
            }

            int initIndex = optional.Count;
            foreach (object entry in Pair.ToList(keywordSpec.Cdr))
            {
                List<object> parts = Pair.ToList(entry);
                if (parts.Count < 3)
                {
                    continue;
                }

                Symbol gensym = parts[2] as Symbol;
                if (gensym == null)
                {
                    continue;
                }

                if (supplied.TryGetValue(parts[0], out object value))
                {
                    frame.Define(gensym, value);
                }
                else
                {
                    object initializer = initIndex < inits.Count ? inits[initIndex] : null;
                    frame.Define(
                        gensym,
                        initializer == null ? (object)false : Eval(initializer, frame, closure.Module));
                }

                initIndex++;
            }
        }
        else
        {
            // No keyword section: any gensyms left over still need a value, taken from
            // their initializer if one was supplied.
            for (int i = slot; i < gensyms.Count; i++)
            {
                int initIndex = i - required.Count;
                object initializer = initIndex >= 0 && initIndex < inits.Count ? inits[initIndex] : null;
                frame.Define(
                    (Symbol)gensyms[i],
                    initializer == null ? (object)false : Eval(initializer, frame, closure.Module));
            }
        }

        body = fields[7];
        return frame;
    }

    /// <summary>
    /// Reads psyntax's <c>sourcev</c> — a <c>#(filename line column)</c> vector — into a
    /// location, or answers <see langword="null"/> for psyntax's <c>no-source</c>
    /// (<c>#f</c>) and for anything else that is not that shape.
    /// </summary>
    private static Reader.SourceLocation SourceFromVector(object source)
    {
        if (!(source is object[] vector) || vector.Length < 3)
        {
            return null;
        }

        string fileName = vector[0] is MutableString text ? text.ToString() : vector[0] as string;
        if (fileName == null
            || !Numeric.SchemeNumber.IsNumber(vector[1])
            || !Numeric.SchemeNumber.IsNumber(vector[2]))
        {
            return null;
        }

        return new Reader.SourceLocation(
            fileName,
            (int)Numeric.SchemeNumber.ToDouble(vector[1]),
            (int)Numeric.SchemeNumber.ToDouble(vector[2]));
    }

    private static object MetaEntry(object meta, string key)
    {
        foreach (object entry in Pair.ToList(meta))
        {
            if (entry is Pair pair
                && pair.Car is Symbol entryKey
                && string.Equals(entryKey.Name, key, StringComparison.Ordinal))
            {
                return pair.Cdr;
            }
        }

        return null;
    }

    private SchemeModule ResolveModule(object moduleName, SchemeModule fallback)
    {
        if (moduleName is SchemeModule module)
        {
            return module;
        }

        if (moduleName is Pair)
        {
            return _interpreter.Modules.Resolve(moduleName);
        }

        return fallback ?? _interpreter.CurrentModule;
    }

    private static object LookupTopLevel(SchemeModule module, Symbol name)
    {
        Variable variable = module.Lookup(name);
        if (variable == null || !variable.IsBound)
        {
            throw Unbound(name);
        }

        return variable.GetValue();
    }

    private static SchemeThrow Unbound(Symbol name)
        => new SchemeThrow(
            Symbol.Intern("unbound-variable"),
            Pair.List(false, new MutableString("Unbound variable: ~S"), Pair.List(name), false));
}
