// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// Loads the vendored Guile Scheme layer on top of the C# core.
/// <para>
/// The critical step is psyntax. Guile ships its macro expander twice: once as
/// <c>psyntax.scm</c>, written in <c>syntax-case</c>, and once as
/// <c>psyntax-pp.scm</c>, the same expander already macro-expanded into core Scheme so
/// that it can be loaded by an implementation which does not yet have a macro expander.
/// Loading the pre-expanded copy is how Guile bootstraps itself, and it is how
/// LilyScheme gets full <c>syntax-case</c> without hand-writing an expander.
/// </para>
/// </summary>
public static class SchemeBootstrap
{
    /// <summary>
    /// The ten names psyntax assigns into rather than defines. Guile pre-creates these
    /// in the core module and psyntax fills them in with <c>set!</c> as it loads, so the
    /// variables must already exist or the assignment fails with an unbound-variable
    /// error. Discovered by scanning <c>psyntax-pp.scm</c> for top-level <c>set!</c>.
    /// </summary>
    public static readonly string[] PsyntaxAssignedNames =
    {
        "$sc-dispatch",
        "bindings",
        "bound-identifier=?",
        "datum->syntax",
        "free-identifier=?",
        "generate-temporaries",
        "identifier?",
        "macroexpand",
        "syntax->datum",
        "syntax-violation",
    };

    /// <summary>The file name of the vendored pre-expanded psyntax.</summary>
    public const string PsyntaxResourceName = "psyntax-pp.scm";

    /// <summary>
    /// Creates the placeholder bindings psyntax expects to assign into. Each is bound to
    /// a procedure that raises if called before psyntax has replaced it, which turns a
    /// bootstrap ordering mistake into a clear message instead of a mysterious value.
    /// </summary>
    /// <param name="interpreter">The interpreter to prepare.</param>
    public static void PrepareForPsyntax(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach (string name in PsyntaxAssignedNames)
        {
            Symbol symbol = Symbol.Intern(name);
            if (interpreter.GuileModule.LookupLocal(symbol) != null)
            {
                // Already provided by the C# primitive layer; psyntax will overwrite it.
                continue;
            }

            string captured = name;
            interpreter.GuileModule.Define(
                symbol,
                new Primitive(
                    name,
                    0,
                    -1,
                    _ => throw new SchemeEvaluationException(
                        "'" + captured + "' was called before psyntax finished loading.")));
        }
    }

    /// <summary>Reads the vendored <c>psyntax-pp.scm</c> from the assembly's resources.</summary>
    /// <returns>The Scheme source text.</returns>
    public static string ReadPsyntaxSource() => ReadVendoredSource(PsyntaxResourceName);

    /// <summary>
    /// Reads a vendored Scheme file from the assembly's embedded resources, matching on
    /// file name. MSBuild rewrites characters that are illegal in identifiers when it
    /// derives resource names from paths -- "ice-9" becomes "ice_9" -- so matching the
    /// full path would be fragile across SDK versions and platforms.
    /// </summary>
    /// <param name="fileName">The vendored file name, for example <c>srfi-1.scm</c>.</param>
    /// <returns>The Scheme source text.</returns>
    public static string ReadVendoredSource(string fileName)
    {
        Assembly assembly = typeof(SchemeBootstrap).Assembly;
        string match = null;
        foreach (string candidate in assembly.GetManifestResourceNames())
        {
            if (candidate.EndsWith("." + fileName, StringComparison.Ordinal)
                || candidate.EndsWith("/" + fileName, StringComparison.Ordinal)
                || string.Equals(candidate, fileName, StringComparison.Ordinal))
            {
                match = candidate;
                break;
            }
        }

        if (match == null)
        {
            throw new SchemeEvaluationException(
                "Embedded Scheme resource '" + fileName + "' is missing from the assembly.");
        }

        using (Stream stream = assembly.GetManifestResourceStream(match))
        using (StreamReader reader = new StreamReader(stream))
        {
            return NormalizeLineEndings(reader.ReadToEnd());
        }
    }

    /// <summary>
    /// Rewrites CRLF to LF in vendored Scheme source.
    /// <para>
    /// The .gitattributes at the repo root pins these files to LF, but that is POLICY:
    /// it governs a checkout and nothing else. These files are embedded resources, so
    /// whatever bytes are on disk at build time are baked into the assembly and SHIPPED
    /// -- a source zip, a contributor whose git is configured differently, or an editor
    /// that saves CRLF all produce a package that is broken for every consumer on every
    /// platform. This is the enforcement.
    /// </para>
    /// <para>
    /// A CR is harmless between forms, where the reader counts it as whitespace, and NOT
    /// harmless inside a string literal, where it is simply part of the string. That is
    /// how it bites: the CRs ride inside the multi-line format-directive literals of
    /// ice-9/format.scm, whose parser then runs off the end of its string and recurses
    /// through format-error until the process dies with an uncatchable stack overflow.
    /// </para>
    /// <para>
    /// Only the CRLF PAIR is rewritten. A lone CR is left exactly where it is: nothing
    /// vendored has one, and silently rewriting a deliberate carriage return inside a
    /// string literal would be the same class of quiet corruption this exists to stop.
    /// </para>
    /// </summary>
    /// <param name="source">The source text as read from the resource.</param>
    /// <returns>The source with every CRLF reduced to LF.</returns>
    private static string NormalizeLineEndings(string source)
        => source.IndexOf('\r') < 0 ? source : source.Replace("\r\n", "\n");

    /// <summary>
    /// Loads psyntax into an interpreter, giving it <c>syntax-case</c>,
    /// <c>syntax-rules</c> and <c>define-syntax</c>.
    /// </summary>
    /// <param name="interpreter">The interpreter to bootstrap.</param>
    /// <returns>The number of top-level forms evaluated.</returns>
    public static int LoadPsyntax(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        PrepareForPsyntax(interpreter);
        int forms = LoadSource(interpreter, ReadPsyntaxSource(), "psyntax-pp.scm");
        interpreter.IsPsyntaxLoaded = true;
        return forms;
    }

    /// <summary>
    /// Loads psyntax and then the LilyScheme prelude, leaving the interpreter able to
    /// evaluate ordinary Guile-dialect Scheme. Module autoloading is enabled once the
    /// prelude is in place.
    /// </summary>
    /// <param name="interpreter">The interpreter to bootstrap.</param>
    /// <returns>The total number of top-level forms evaluated.</returns>
    public static int LoadCore(Interpreter interpreter)
    {
        // Guile's own source is read with Guile's own syntax. A host reader extension --
        // LilyPond registers one for #{ embedded music #} -- would otherwise capture the
        // extended symbols in psyntax-pp.scm and break the expander silently.
        IReadOnlyDictionary<char, Reader.SchemeReader.HashExtension> extensions
            = Reader.SchemeReader.SuspendHashExtensions();
        try
        {
            int forms = LoadPsyntax(interpreter);
            forms += LoadExpanded(interpreter, ReadVendoredSource("prelude.scm"), "prelude.scm");
            EnableModuleAutoload(interpreter);
            InstallVmProgramShim(interpreter);
            InstallIconvShim(interpreter);
            InstallPopenShim(interpreter);
            Primitives.SoftPortPrimitives.InstallShim(interpreter);
            Primitives.UnicodePrimitives.InstallShim(interpreter);
            return forms;
        }
        finally
        {
            Reader.SchemeReader.RestoreHashExtensions(extensions);
        }
    }

    /// <summary>
    /// The modules LilyScheme implements itself, and which must therefore never be
    /// autoloaded from the vendored Guile source even though that source is present.
    /// <para>
    /// <c>(oop goops)</c> is superseded by the C# GOOPS; <c>(ice-9 optargs)</c> and
    /// <c>(ice-9 and-let-star)</c> are superseded by the prelude, which supplies
    /// <c>lambda*</c> from the evaluator rather than as a macro; and <c>(ice-9 boot-9)</c>
    /// cannot load at all -- see the boot-9 note in AGENT-README.txt.
    /// </para>
    /// </summary>
    public static readonly string[] SelfProvidedModules =
    {
        "(oop goops)",
        "(ice-9 optargs)",
        "(ice-9 and-let-star)",
        "(ice-9 boot-9)",
        "(guile)",
        "(guile-user)",
        "(system vm program)",
        "(ice-9 iconv)",
        "(ice-9 soft-ports)",
        "(ice-9 unicode)",
        "(ice-9 popen)",
    };

    /// <summary>
    /// Installs the <c>(system vm program)</c> shim module.
    /// <para>
    /// Guile's module of that name introspects compiled VM procedures; LilyScheme has
    /// no VM, so no value is ever a program. The vendored <c>ice-9/session.scm</c>
    /// reaches for it as <c>((@ (system vm program) program?) proc)</c>, which is why
    /// the module must exist with <c>program?</c> bound — answering
    /// <see langword="false"/> for everything is the honest translation.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to install the shim into.</param>
    public static void InstallVmProgramShim(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        SchemeModule module = interpreter.Modules.Resolve(Pair.ListFrom(new object[]
        {
            Symbol.Intern("system"), Symbol.Intern("vm"), Symbol.Intern("program"),
        }));
        module.Define(Symbol.Intern("program?"), new Primitive("program?", 1, 1, a => false));
    }

    /// <summary>
    /// Installs the <c>(ice-9 iconv)</c> shim module: string/bytevector conversion
    /// over .NET's encodings instead of GNU iconv. LilyPond's QR-code generator
    /// imports <c>string->bytevector</c> from it.
    /// </summary>
    /// <param name="interpreter">The interpreter to install the shim into.</param>
    public static void InstallIconvShim(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        SchemeModule module = interpreter.Modules.Resolve(Pair.ListFrom(new object[]
        {
            Symbol.Intern("ice-9"), Symbol.Intern("iconv"),
        }));
        module.Define(
            Symbol.Intern("string->bytevector"),
            new Primitive("string->bytevector", 2, 3, a =>
                ResolveEncoding(TextOf(a[1], "string->bytevector"))
                    .GetBytes(TextOf(a[0], "string->bytevector"))));
        module.Define(
            Symbol.Intern("bytevector->string"),
            new Primitive("bytevector->string", 2, 3, a =>
                new MutableString(
                    ResolveEncoding(TextOf(a[1], "bytevector->string"))
                        .GetString((byte[])a[0]))));
    }

    /// <summary>
    /// Installs the <c>(ice-9 popen)</c> shim module.
    /// <para>
    /// Guile's popen.scm builds bidirectional custom binary ports over primitives from
    /// <c>scm_init_popen</c>, <c>(ice-9 threads)</c> and a waitpid table — machinery this
    /// port does not have — so the module is provided from C# with Guile's own surface:
    /// <c>open-pipe*</c>, <c>open-pipe</c>, <c>open-input-pipe</c>,
    /// <c>open-output-pipe</c> and <c>close-pipe</c>, over
    /// <see cref="System.Diagnostics.Process"/>. A read pipe captures the child's
    /// standard output and inherits its input; a write pipe the reverse; and
    /// <c>close-pipe</c> closes the port, waits, and answers the encoded wait status
    /// <c>status:exit-val</c> decodes. <c>OPEN_BOTH</c> ("r+") is REFUSED loudly — a
    /// port here is a reader or a writer and never both (the <c>open-file</c> rule) —
    /// and so is <c>open-input-output-pipe</c>, until something demands them.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    public static void InstallPopenShim(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // The mode strings are core bindings in Guile (libguile/ports.c) and are used
        // with this module: (open-pipe cmd OPEN_READ).
        interpreter.DefineValue("OPEN_READ", new MutableString("r"));
        interpreter.DefineValue("OPEN_WRITE", new MutableString("w"));
        interpreter.DefineValue("OPEN_BOTH", new MutableString("r+"));

        Dictionary<object, System.Diagnostics.Process> children
            = new Dictionary<object, System.Diagnostics.Process>();

        Func<string, string[], string, object> openPipe = (program, arguments, mode) =>
        {
            bool read = string.Equals(mode, "r", StringComparison.Ordinal);
            bool write = string.Equals(mode, "w", StringComparison.Ordinal);
            if (!read && !write)
            {
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("open-pipe"),
                        new MutableString("bidirectional pipes are not supported by this port: ~S"),
                        Pair.List(new MutableString(mode)),
                        false));
            }

            System.Diagnostics.Process child
                = Primitives.PosixPrimitives.StartProcess(program, arguments, write, read);
            object port = read
                ? (object)new Primitives.SchemeInputPort(child.StandardOutput, "#<pipe>")
                {
                    // Guile's pipe ports are fports, so file-port? answers #t for them.
                    IsFilePort = true,
                }
                : new Primitives.SchemeOutputPort(child.StandardInput);
            lock (children)
            {
                children[port] = child;
            }

            return port;
        };

        SchemeModule module = interpreter.Modules.Resolve(Pair.ListFrom(new object[]
        {
            Symbol.Intern("ice-9"), Symbol.Intern("popen"),
        }));

        module.Define(
            Symbol.Intern("open-pipe*"),
            new Primitive("open-pipe*", 2, -1, a =>
            {
                string mode = TextOf(a[0], "open-pipe*");
                string program = TextOf(a[1], "open-pipe*");
                string[] arguments = new string[a.Length - 2];
                for (int i = 2; i < a.Length; i++)
                {
                    arguments[i - 2] = TextOf(a[i], "open-pipe*");
                }

                return openPipe(program, arguments, mode);
            }));

        module.Define(
            Symbol.Intern("open-pipe"),
            new Primitive("open-pipe", 2, 2, a =>
                openPipe(
                    Primitives.PosixPrimitives.ShellPath(),
                    new[] { Primitives.PosixPrimitives.ShellCommandFlag(), TextOf(a[0], "open-pipe") },
                    TextOf(a[1], "open-pipe"))));

        module.Define(
            Symbol.Intern("open-input-pipe"),
            new Primitive("open-input-pipe", 1, 1, a =>
                openPipe(
                    Primitives.PosixPrimitives.ShellPath(),
                    new[] { Primitives.PosixPrimitives.ShellCommandFlag(), TextOf(a[0], "open-input-pipe") },
                    "r")));

        module.Define(
            Symbol.Intern("open-output-pipe"),
            new Primitive("open-output-pipe", 1, 1, a =>
                openPipe(
                    Primitives.PosixPrimitives.ShellPath(),
                    new[] { Primitives.PosixPrimitives.ShellCommandFlag(), TextOf(a[0], "open-output-pipe") },
                    "w")));

        module.Define(
            Symbol.Intern("open-input-output-pipe"),
            new Primitive("open-input-output-pipe", 1, 1, a =>
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("open-input-output-pipe"),
                        new MutableString("bidirectional pipes are not supported by this port"),
                        false,
                        false))));

        module.Define(
            Symbol.Intern("close-pipe"),
            new Primitive("close-pipe", 1, 1, a =>
            {
                System.Diagnostics.Process child;
                lock (children)
                {
                    if (!children.TryGetValue(a[0], out child))
                    {
                        throw new SchemeThrow(
                            Symbol.Intern("wrong-type-arg"),
                            Pair.List(
                                new MutableString("close-pipe"),
                                new MutableString("Not a pipe port: ~S"),
                                Pair.List(a[0]),
                                false));
                    }

                    children.Remove(a[0]);
                }

                switch (a[0])
                {
                    case Primitives.SchemeInputPort input:
                        input.IsClosed = true;
                        input.Stream.Dispose();
                        break;
                    case Primitives.SchemeOutputPort output:
                        output.Writer.Flush();
                        output.Writer.Dispose();
                        output.IsClosed = true;
                        break;
                }

                using (child)
                {
                    child.WaitForExit();
                    return ((long)(child.ExitCode & 0xff)) << 8;
                }
            }));
    }

    private static string TextOf(object value, string procedureName)
    {
        switch (value)
        {
            case MutableString text:
                return text.ToString();
            case string text:
                return text;
            default:
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString(procedureName),
                        new MutableString("Not a string: ~S"),
                        Pair.List(value),
                        false));
        }
    }

    /// <summary>
    /// Maps a Guile/iconv encoding name onto a .NET encoding. Shared with
    /// <see cref="Primitives.PortPrimitives"/>, whose <c>#:encoding</c> keyword must
    /// answer the same names this one does — LilyPond asks for "latin1" and "UTF-8".
    /// </summary>
    /// <param name="name">The encoding name as Scheme spells it.</param>
    /// <returns>The matching encoding.</returns>
    internal static System.Text.Encoding ResolveEncoding(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "latin1":
            case "latin-1":
            case "iso-8859-1":
                return System.Text.Encoding.Latin1;
            case "utf8":
            case "utf-8":
                return System.Text.Encoding.UTF8;
            case "ascii":
            case "us-ascii":
                return System.Text.Encoding.ASCII;
            default:
                return System.Text.Encoding.GetEncoding(name);
        }
    }

    /// <summary>
    /// Makes <c>(use-modules ...)</c> load the vendored Guile source for a module the
    /// first time it is named, the way Guile's own autoloading does.
    /// </summary>
    /// <param name="interpreter">The interpreter to enable autoloading on.</param>
    public static void EnableModuleAutoload(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        Func<object, SchemeModule, bool> previous = interpreter.Modules.ModuleLoader;
        interpreter.Modules.ModuleLoader = (name, module) =>
            AutoloadVendoredModule(interpreter, name, module)
            || (previous != null && previous(name, module));
    }

    /// <summary>
    /// Loads the vendored Guile source for a module, if there is any.
    /// </summary>
    /// <param name="interpreter">The interpreter to load into.</param>
    /// <param name="name">The module name as a Scheme list, for example <c>(srfi srfi-1)</c>.</param>
    /// <param name="module">The freshly created module to populate.</param>
    /// <returns><see langword="true"/> when a vendored file was found and loaded.</returns>
    public static bool AutoloadVendoredModule(Interpreter interpreter, object name, SchemeModule module)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // Before psyntax is up there is no expander, so nothing can be autoloaded --
        // and nothing needs to be: the bootstrap resolves only (guile).
        if (!interpreter.IsPsyntaxLoaded)
        {
            return false;
        }

        string printed = Printer.Write(name);
        foreach (string self in SelfProvidedModules)
        {
            if (string.Equals(printed, self, StringComparison.Ordinal))
            {
                return false;
            }
        }

        string fileName = LastComponent(name);
        if (fileName == null)
        {
            return false;
        }

        string source = TryReadVendoredSource(fileName + ".scm");
        if (source == null)
        {
            return false;
        }

        SchemeModule saved = interpreter.CurrentModule;
        interpreter.CurrentModule = module;
        try
        {
            LoadExpanded(interpreter, source, fileName + ".scm");
        }
        finally
        {
            interpreter.CurrentModule = saved;
        }

        return true;
    }

    /// <summary>Reads a vendored Scheme file, returning null rather than raising when absent.</summary>
    /// <param name="fileName">The vendored file name.</param>
    /// <returns>The source text, or <see langword="null"/> when there is no such resource.</returns>
    public static string TryReadVendoredSource(string fileName)
    {
        Assembly assembly = typeof(SchemeBootstrap).Assembly;
        foreach (string candidate in assembly.GetManifestResourceNames())
        {
            if (candidate.EndsWith("." + fileName, StringComparison.Ordinal)
                || candidate.EndsWith("/" + fileName, StringComparison.Ordinal)
                || string.Equals(candidate, fileName, StringComparison.Ordinal))
            {
                using (Stream stream = assembly.GetManifestResourceStream(candidate))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return NormalizeLineEndings(reader.ReadToEnd());
                }
            }
        }

        return null;
    }

    private static string LastComponent(object name)
    {
        object current = name;
        Symbol last = null;
        while (current is Pair pair)
        {
            if (pair.Car is Symbol symbol)
            {
                last = symbol;
            }

            current = pair.Cdr;
        }

        return last?.Name;
    }

    /// <summary>
    /// Reads and evaluates Scheme source through psyntax and the Tree-IL evaluator. Use
    /// this for anything loaded after the bootstrap, where macros are expected to work.
    /// </summary>
    /// <param name="interpreter">The interpreter to load into.</param>
    /// <param name="source">The Scheme source text.</param>
    /// <param name="fileName">A name used in error messages.</param>
    /// <returns>The number of top-level forms evaluated.</returns>
    public static int LoadExpanded(Interpreter interpreter, string source, string fileName)
    {
        Caching.ExpansionCache cache = interpreter.ExpansionCache;
        string sourceHash = cache == null ? null : Caching.ExpansionCache.HashSource(source);
        if (cache != null)
        {
            // Replay: substitute the recorded Tree-IL for read-and-expand, but still
            // EVALUATE each form live, in order, in the module current at its turn —
            // nested loads and module switches re-trigger through evaluation exactly as
            // they did when the recording was made.
            IReadOnlyList<object> recorded;
            if (cache.TryGetFile(fileName, sourceHash, out recorded))
            {
                for (int i = 0; i < recorded.Count; i++)
                {
                    try
                    {
                        interpreter.TreeIlEvaluator.Eval(recorded[i], null, interpreter.CurrentModule);
                    }
                    catch (Exception ex) when (!(ex is SchemeEvaluationException))
                    {
                        throw new SchemeEvaluationException(
                            fileName + ": top-level form " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " of " + recorded.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " failed: " + ex.Message,
                            ex);
                    }
                }

                return recorded.Count;
            }
        }

        long readStart = System.Diagnostics.Stopwatch.GetTimestamp();
        List<object> forms = Reader.SchemeReader.ReadAll(source, fileName);
        LoadDiagnostics.AddRead(System.Diagnostics.Stopwatch.GetTimestamp() - readStart);
        List<object> expandedForms = cache == null ? null : new List<object>(forms.Count);
        for (int i = 0; i < forms.Count; i++)
        {
            try
            {
                // Guile's curried definitions are rewritten before expansion; see
                // CurriedDefinitions for why this cannot be a macro.
                object form = CurriedDefinitions.Expand(forms[i]);
                if (expandedForms != null)
                {
                    // Recording runs in psyntax's c&e (compile-and-evaluate) mode —
                    // upstream's own file-compilation mode: the expander EVALUATES each
                    // top-level form itself and returns Tree-IL that rebuilds the same
                    // state (including define-syntax installations, which mode e leaves
                    // as expansion-time side effects only). The returned Tree-IL is the
                    // recording; evaluating it here a second time would re-run every
                    // form — re-creating modules out from under the expander — so it
                    // is recorded WITHOUT re-evaluation.
                    expandedForms.Add(interpreter.TreeIlEvaluator.Expand(form, true));
                }
                else
                {
                    interpreter.TreeIlEvaluator.Eval(
                        interpreter.TreeIlEvaluator.Expand(form, false),
                        null,
                        interpreter.CurrentModule);
                }
            }
            catch (Exception ex) when (!(ex is SchemeEvaluationException))
            {
                throw new SchemeEvaluationException(
                    fileName + ": top-level form " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " of " + forms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " failed: " + ex.Message,
                    ex);
            }
        }

        // Recorded only when the whole file loaded; a partially-loaded file must stay
        // unrecorded so a replayed boot fails exactly where a live one does.
        if (cache != null)
        {
            cache.RecordFile(fileName, sourceHash, expandedForms);
        }

        return forms.Count;
    }

    /// <summary>Reads and evaluates Scheme source, reporting the form count.</summary>
    /// <param name="interpreter">The interpreter to load into.</param>
    /// <param name="source">The Scheme source text.</param>
    /// <param name="fileName">A name used in error messages.</param>
    /// <returns>The number of top-level forms evaluated.</returns>
    public static int LoadSource(Interpreter interpreter, string source, string fileName)
    {
        long readStart = System.Diagnostics.Stopwatch.GetTimestamp();
        List<object> forms = Reader.SchemeReader.ReadAll(source, fileName);
        LoadDiagnostics.AddRead(System.Diagnostics.Stopwatch.GetTimestamp() - readStart);
        for (int i = 0; i < forms.Count; i++)
        {
            try
            {
                long evalStart = System.Diagnostics.Stopwatch.GetTimestamp();
                interpreter.Evaluator.Eval(forms[i], null, interpreter.CurrentModule);
                LoadDiagnostics.AddPlainEval(System.Diagnostics.Stopwatch.GetTimestamp() - evalStart);
            }
            catch (Exception ex) when (!(ex is SchemeEvaluationException))
            {
                throw new SchemeEvaluationException(
                    fileName + ": top-level form " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " of " + forms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " failed: " + ex.Message,
                    ex);
            }
        }

        return forms.Count;
    }
}
