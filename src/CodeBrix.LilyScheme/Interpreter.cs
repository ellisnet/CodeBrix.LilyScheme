// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme;

/// <summary>
/// A LilyScheme interpreter instance: a module registry, an evaluator, and the set of
/// primitives that Scheme code sees. This is the entry point for embedding LilyScheme.
/// </summary>
public sealed class Interpreter
{
    /// <summary>
    /// Stack size for <see cref="RunWithLargeStack"/>. psyntax recurses deeply while
    /// expanding, and the CLR default of one megabyte is not enough to load it.
    /// </summary>
    public const int LargeStackBytes = 256 * 1024 * 1024;

    private readonly Dictionary<Symbol, object> _objectProperties = new Dictionary<Symbol, object>();

    /// <summary>Initializes an interpreter with the core primitives installed.</summary>
    public Interpreter()
    {
        Modules = new ModuleRegistry();
        GuileModule = new SchemeModule(Pair.List(Symbol.Intern("guile")));
        Modules.RootModule = GuileModule;
        Modules.Register(GuileModule);
        CurrentModule = GuileModule;
        Evaluator = new Evaluator(this);
        TreeIlEvaluator = new TreeIl.TreeIlEvaluator(this);

        CorePrimitives.Install(this);
        NumericPrimitives.Install(this);
        StringPrimitives.Install(this);
        VectorPrimitives.Install(this);
        ArrayPrimitives.Install(this);
        ControlPrimitives.Install(this);
        ModulePrimitives.Install(this);
        PortPrimitives.Install(this);
        GoopsPrimitives.Install(this);
        GuileCorePrimitives.Install(this);
        PosixPrimitives.Install(this);
        ExceptionPrimitives.Install(this);

        // Last, because it MARKS the primitive objects installed above rather than
        // defining them: Guile's generic-capability? is a property of the subr.
        PrimitiveGenerics.Install(this);
    }

    /// <summary>Gets the module registry.</summary>
    public ModuleRegistry Modules { get; }

    /// <summary>Gets the root <c>(guile)</c> module holding the core bindings.</summary>
    public SchemeModule GuileModule { get; }

    /// <summary>Gets or sets the module that top-level evaluation happens in.</summary>
    public SchemeModule CurrentModule { get; set; }

    /// <summary>Gets the source-form evaluator used to bootstrap and to run core forms.</summary>
    public Evaluator Evaluator { get; }

    /// <summary>Gets the Tree-IL evaluator used for everything psyntax expands.</summary>
    public TreeIl.TreeIlEvaluator TreeIlEvaluator { get; }

    /// <summary>
    /// Gets or sets a value indicating whether psyntax has been loaded. Once it has,
    /// evaluation routes through macroexpand and the Tree-IL evaluator.
    /// </summary>
    public bool IsPsyntaxLoaded { get; set; }

    /// <summary>
    /// Gets or sets whether a <c>use-modules</c> clause without <c>#:select</c> imports the
    /// module's PUBLIC INTERFACE, as Guile does — the default — or the WHOLE module.
    /// <para>
    /// On (the default since 2026-08-28), only exported names arrive, through a live view
    /// that keeps growing with the module's exports, exactly as Guile documents. Off, the
    /// whole module goes on the importer's use list, so private names are visible and
    /// scope is wider than Guile's — the behaviour the LilyPond layer's module world was
    /// verified under, which CodeBrix.LilyPort therefore selects EXPLICITLY at interpreter
    /// creation until its own corpus has been swept under the narrow import. The switch
    /// is consulted each time a <c>use-modules</c> clause is resolved, so it must be set
    /// before loading the code it is meant to govern. <c>#:select</c> clauses and the
    /// implicit core import behave identically in both settings.
    /// </para>
    /// <para>
    /// //was previously: <see langword="false"/> by default (the wide import), with the
    /// Guile-exact position opt-in. Flipped under the ruling that LilyScheme works like
    /// Guile wherever possible; consumers that need the wide import say so.
    /// </para>
    /// </summary>
    public bool NarrowModuleImports { get; set; } = true;

    /// <summary>
    /// Gets or sets the expansion cache <see cref="Runtime.SchemeBootstrap.LoadExpanded"/>
    /// consults. Null (the default) loads live. A cache instance must not be shared
    /// between interpreters — see <see cref="Caching.ExpansionCache"/>.
    /// </summary>
    public Caching.ExpansionCache ExpansionCache { get; set; }

    /// <summary>Gets or sets the writer used by <c>display</c>, <c>write</c> and friends.</summary>
    public TextWriter OutputWriter { get; set; } = Console.Out;

    /// <summary>Gets or sets the writer used for warnings and error output.</summary>
    public TextWriter ErrorWriter { get; set; } = Console.Error;

    /// <summary>
    /// Gets or sets the reader behind <c>(current-input-port)</c>. Defaults to the
    /// process's standard input; a host embedding the interpreter substitutes its own.
    /// </summary>
    public TextReader InputReader { get; set; } = Console.In;

    /// <summary>Gets the load path searched by <c>%search-load-path</c>.</summary>
    public List<string> LoadPath { get; } = new List<string>();

    /// <summary>Gets the object-property table backing <c>object-property</c>.</summary>
    public Dictionary<Symbol, object> ObjectProperties => _objectProperties;

    /// <summary>Gets the modern exception API's per-interpreter state — the standard
    /// exception types, the dynamic handler stack, and the printer alist.</summary>
    internal ExceptionRuntime Exceptions { get; } = new ExceptionRuntime();

    /// <summary>Registers a primitive procedure in the root module.</summary>
    /// <param name="name">The Scheme-visible name.</param>
    /// <param name="minimumArgumentCount">The smallest acceptable argument count.</param>
    /// <param name="maximumArgumentCount">The largest acceptable count, or -1 when variadic.</param>
    /// <param name="implementation">The C# implementation.</param>
    /// <returns>The primitive that was registered.</returns>
    public Primitive DefinePrimitive(
        string name,
        int minimumArgumentCount,
        int maximumArgumentCount,
        Func<object[], object> implementation)
    {
        Primitive primitive = new Primitive(name, minimumArgumentCount, maximumArgumentCount, implementation);
        GuileModule.Define(Symbol.Intern(name), primitive);
        return primitive;
    }

    /// <summary>Registers a non-procedure value in the root module.</summary>
    /// <param name="name">The Scheme-visible name.</param>
    /// <param name="value">The value to bind.</param>
    public void DefineValue(string name, object value)
        => GuileModule.Define(Symbol.Intern(name), value);

    /// <summary>
    /// Evaluates a single form in the current module, the way Guile's <c>eval</c> does:
    /// macro-expanded through psyntax once psyntax is loaded, and through the core
    /// evaluator before that (the boot layer).
    /// <para>
    /// //was previously: always the core evaluator, which does not expand macros. A macro
    /// reached this way — <c>(markup …)</c> from LilyPond's <c>(lily)</c> module through
    /// <c>EvalString</c> — failed as <c>Wrong type to apply: #&lt;syntax-transformer markup&gt;</c>,
    /// while the same form loaded from a module file worked. Guile's <c>eval</c> and
    /// <c>eval-string</c> both expand (MEASURED on the pinned 2.27.2: an <c>eval-string</c>
    /// that defines a <c>syntax-rules</c> macro and uses it in the next form answers the
    /// expansion). Changed 2026-08-28. <see cref="LoadFile"/> and
    /// <see cref="LoadFileWithProgress"/> keep the core evaluator on purpose: they are the
    /// boot paths that load psyntax itself.
    /// </para>
    /// </summary>
    /// <param name="form">The form to evaluate.</param>
    /// <returns>The value of the form.</returns>
    public object Eval(object form) => Primitives.ControlPrimitives.EvalAny(this, form, CurrentModule);

    /// <summary>
    /// Reads and evaluates every form in a string, returning the last value — expanding
    /// macros once psyntax is loaded, exactly as <see cref="Eval"/> does (Guile's
    /// <c>eval-string</c>).
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="fileName">A name used in error messages.</param>
    /// <returns>The value of the final form, or unspecified when there are none.</returns>
    public object EvalString(string text, string fileName)
    {
        List<object> forms = SchemeReader.ReadAll(text, fileName);
        object result = Unspecified.Instance;
        foreach (object form in forms)
        {
            result = Primitives.ControlPrimitives.EvalAny(this, form, CurrentModule);
        }

        return result;
    }

    /// <summary>Reads and evaluates every form in a file.</summary>
    /// <param name="path">The path to the source file.</param>
    /// <returns>The value of the final form.</returns>
    public object LoadFile(string path)
    {
        string text = HostFile.ReadAllText(path);
        return EvalString(text, path);
    }

    /// <summary>
    /// Reads and evaluates a file one form at a time, reporting progress. Used when
    /// loading large bootstrap files where knowing which form failed matters.
    /// </summary>
    /// <param name="path">The path to the source file.</param>
    /// <param name="onForm">Invoked with the zero-based index before each form is evaluated.</param>
    /// <returns>The value of the final form.</returns>
    public object LoadFileWithProgress(string path, Action<int, object> onForm)
    {
        List<object> forms = SchemeReader.ReadAll(HostFile.ReadAllText(path), path);
        object result = Unspecified.Instance;
        for (int i = 0; i < forms.Count; i++)
        {
            onForm?.Invoke(i, forms[i]);
            result = Evaluator.Eval(forms[i], null, CurrentModule);
        }

        return result;
    }

    /// <summary>
    /// Runs an action on a thread with a large stack. Deeply recursive Scheme — psyntax
    /// above all — overflows the CLR's default stack, and the limit is per thread rather
    /// than per process, so a dedicated thread is the fix.
    /// <para>
    /// A failure on that thread is re-thrown to the caller AS ITSELF, with its original
    /// stack trace, rather than wrapped in an exception of this method's own. The
    /// big-stack thread is an implementation detail and callers must be able to catch
    /// what the interpreter actually raised; a wrapper hid every real message behind one
    /// that said nothing about the cause.
    /// </para>
    /// </summary>
    /// <param name="action">The work to run.</param>
    public static void RunWithLargeStack(Action action)
    {
        Exception failure = null;
        Thread thread = new Thread(
            () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            LargeStackBytes);
        //Background: the caller always Joins, so the flag only matters at process
        //  exit - a GUI host quitting mid-evaluation must not be held open by it
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Runs a function on a thread with a large stack and returns its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="function">The work to run.</param>
    /// <returns>The function's result.</returns>
    public static T RunWithLargeStack<T>(Func<T> function)
    {
        T result = default;
        RunWithLargeStack(() => { result = function(); });
        return result;
    }
}
