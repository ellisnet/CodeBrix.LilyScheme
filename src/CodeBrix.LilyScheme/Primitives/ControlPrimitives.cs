// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.TreeIl;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>Application, multiple values, error signalling, and procedure metadata.</summary>
public static class ControlPrimitives
{
    /// <summary>Installs the control primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallApplication(interpreter);
        InstallValues(interpreter);
        InstallErrors(interpreter);
        InstallProcedureMetadata(interpreter);
        InstallFluids(interpreter);
    }

    private static void InstallApplication(Interpreter interpreter)
    {
        // (apply f a b ... rest) spreads only the final argument.
        interpreter.DefinePrimitive("apply", 1, -1, a =>
        {
            object procedure = a[0];
            List<object> arguments = new List<object>();
            for (int i = 1; i < a.Length - 1; i++)
            {
                arguments.Add(a[i]);
            }

            if (a.Length > 1)
            {
                arguments.AddRange(Pair.ToList(a[a.Length - 1]));
            }

            return interpreter.Evaluator.Apply(procedure, arguments.ToArray());
        });

        // psyntax calls (primitive-eval x) with x ALREADY EXPANDED to Tree-IL --
        // its top-level-eval and local-eval hooks are both (lambda (x mod)
        // (primitive-eval x)). So this has to accept Tree-IL as well as source, or
        // every macro definition silently stores an unevaluated struct.
        interpreter.DefinePrimitive("primitive-eval", 1, 1, a =>
            EvalAny(interpreter, a[0], interpreter.CurrentModule));

        // Guile's eval is `(save-module-excursion (lambda () (set-current-module module)
        // (primitive-eval exp)))` — the target module is CURRENT for the whole call, not
        // merely the module the result is evaluated in. That is load-bearing rather than
        // incidental: psyntax resolves free identifiers at EXPANSION time against
        // (current-module), so expanding in one module and evaluating in another yields
        // references bound to the wrong namespace and every one of them fails as unbound
        // no matter what the target module holds.
        //
        // scm/paper.scm is where LilyPond depends on it: lookup-paper-name evaluates
        // `(cons (* 210 mm) (* 297 mm))' in a PAPER DEFINITION's own scope, and `mm'
        // exists nowhere else. Without the excursion that read as unbound, while
        // module-defined? — two lines earlier, inside eval-carefully — correctly answered
        // that the scope does define it. Two mechanisms disagreeing about one module is
        // the signature.
        interpreter.DefinePrimitive("eval", 1, 2, a =>
        {
            if (!(a.Length > 1 && a[1] is SchemeModule explicitModule))
            {
                return EvalAny(interpreter, a[0], interpreter.CurrentModule);
            }

            SchemeModule saved = interpreter.CurrentModule;
            interpreter.CurrentModule = explicitModule;
            try
            {
                return EvalAny(interpreter, a[0], explicitModule);
            }
            finally
            {
                interpreter.CurrentModule = saved;
            }
        });

        // eval-string reads EVERY form in the string and answers the last one's value.
        // The module excursion is the same one `eval` needs and for the same reason:
        // free identifiers are resolved when the text is expanded, so the target module
        // has to be current for the read-and-expand as well as for the evaluation.
        interpreter.DefinePrimitive("eval-string", 1, 2, a =>
        {
            string text = StringPrimitives.Text(a[0], "eval-string");
            SchemeModule target = a.Length > 1 && a[1] is SchemeModule explicitModule
                ? explicitModule
                : interpreter.CurrentModule;

            SchemeModule saved = interpreter.CurrentModule;
            interpreter.CurrentModule = target;
            try
            {
                return interpreter.EvalString(text, "eval-string");
            }
            finally
            {
                interpreter.CurrentModule = saved;
            }
        });

        interpreter.DefinePrimitive("force", 1, 1, a =>
        {
            if (a[0] is LazyPromise lazy)
            {
                if (!lazy.IsForced)
                {
                    lazy.Value = interpreter.Evaluator.Apply(lazy.Thunk, Array.Empty<object>());
                    lazy.IsForced = true;
                }

                return lazy.Value;
            }

            if (!(a[0] is Promise promise))
            {
                return a[0];
            }

            if (!promise.IsForced)
            {
                promise.Value = interpreter.Evaluator.Apply(promise.Thunk, Array.Empty<object>());
                promise.IsForced = true;
            }

            return promise.Value;
        });

        interpreter.DefinePrimitive("make-promise", 1, 1, a => a[0]);
    }

    /// <summary>
    /// Evaluates either a Tree-IL node or a source form. Tree-IL goes straight to the
    /// Tree-IL evaluator; source is expanded first when psyntax is available.
    /// </summary>
    /// <param name="interpreter">The interpreter to evaluate in.</param>
    /// <param name="expression">A Tree-IL node or a source form.</param>
    /// <param name="module">The module to evaluate in.</param>
    /// <returns>The value of the expression.</returns>
    public static object EvalAny(Interpreter interpreter, object expression, SchemeModule module)
    {
        if (expression is SchemeStruct candidate && ExpandedVtables.IndexOf(candidate.Vtable) >= 0)
        {
            return interpreter.TreeIlEvaluator.Eval(expression, null, module);
        }

        if (interpreter.IsPsyntaxLoaded)
        {
            return interpreter.TreeIlEvaluator.ExpandAndEval(expression, module);
        }

        return interpreter.Evaluator.Eval(expression, null, module);
    }

    private static void InstallValues(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("values", 0, -1, a =>
            a.Length == 1 ? a[0] : new MultipleValues((object[])a.Clone()));

        interpreter.DefinePrimitive("call-with-values", 2, 2, a =>
        {
            object produced = interpreter.Evaluator.Apply(a[0], Array.Empty<object>());
            object[] spread = produced is MultipleValues multiple
                ? multiple.Items
                : new[] { produced };
            return interpreter.Evaluator.Apply(a[1], spread);
        });
    }

    private static void InstallErrors(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("throw", 1, -1, a =>
        {
            object[] rest = new object[a.Length - 1];
            Array.Copy(a, 1, rest, 0, rest.Length);
            throw new SchemeThrow(a[0], Pair.List(rest));
        });

        interpreter.DefinePrimitive("error", 0, -1, a =>
        {
            object message = a.Length > 0 ? a[0] : new MutableString("error");
            object[] rest = a.Length > 1 ? new object[a.Length - 1] : Array.Empty<object>();
            if (a.Length > 1)
            {
                Array.Copy(a, 1, rest, 0, rest.Length);
            }

            throw new SchemeThrow(
                Symbol.Intern("misc-error"),
                Pair.List(false, message, Pair.List(rest), false));
        });

        interpreter.DefinePrimitive("scm-error", 5, 5, a =>
            throw new SchemeThrow(a[0], Pair.List(a[1], a[2], a[3], a[4])));

        interpreter.DefinePrimitive("syntax-violation", 3, 4, a =>
        {
            object who = a[0];
            object message = a[1];
            object form = a[2];
            object subform = a.Length > 3 ? a[3] : false;
            throw new SchemeThrow(
                Symbol.Intern("syntax-error"),
                Pair.List(who, message, form, subform));
        });

        interpreter.DefinePrimitive("warn", 0, -1, a =>
        {
            List<string> parts = new List<string>();
            foreach (object item in a)
            {
                parts.Add(Printer.Display(item));
            }

            interpreter.ErrorWriter.WriteLine(";;; WARNING: " + string.Join(" ", parts));
            return Unspecified.Instance;
        });

        // (make-prompt-tag [stem]) -- boot-9's definition returns a fresh
        // one-element list: "the only property that prompt tags need have is
        // uniqueness in the sense of eq?", and eq? on a fresh pair is reference
        // identity here too.
        interpreter.DefinePrimitive("make-prompt-tag", 0, 1, a =>
            Pair.List(a.Length > 0 ? a[0] : new MutableString("prompt")));

        // Escape-only call-with-prompt: aborting OUT of the thunk to the handler --
        // the only use the vendored Scheme and LilyPond's own make -- works in
        // full. The continuation Guile would hand the handler cannot be captured
        // here, so the handler receives a stand-in that fails loudly if it is
        // ever applied; pretty-print's truncating writer binds it and ignores it.
        interpreter.DefinePrimitive("call-with-prompt", 3, 3, a =>
        {
            try
            {
                return interpreter.Evaluator.Apply(a[1], Array.Empty<object>());
            }
            catch (PromptAbort abort) when (ReferenceEquals(abort.Tag, a[0]))
            {
                object[] handlerArguments = new object[abort.Arguments.Length + 1];
                handlerArguments[0] = new Primitive("prompt-continuation", 0, -1, _ =>
                    throw new SchemeThrow(
                        Symbol.Intern("misc-error"),
                        Pair.List(
                            new MutableString("call-with-prompt"),
                            new MutableString("re-entering an aborted prompt is not supported"),
                            false,
                            false)));
                Array.Copy(abort.Arguments, 0, handlerArguments, 1, abort.Arguments.Length);
                return interpreter.Evaluator.Apply(a[2], handlerArguments);
            }
        });

        // (abort-to-prompt tag args ...) -- unwinds to the nearest dynamically
        // enclosing call-with-prompt on the same tag. With no such prompt the
        // abort surfaces to the host as itself, which is the loud analogue of
        // Guile's "Abort to unknown prompt" error.
        interpreter.DefinePrimitive("abort-to-prompt", 1, -1, a =>
        {
            object[] rest = new object[a.Length - 1];
            Array.Copy(a, 1, rest, 0, rest.Length);
            throw new PromptAbort(a[0], rest);
        });

        // (catch key thunk handler [pre-unwind-handler])
        // boot-9.scm:1856-1858 defines the pre-unwind variant by wrapping the thunk in
        // with-throw-handler, so both primitives share RunWithThrowHandler. #f as the
        // fourth argument means absent, exactly as Guile's #:optional default.
        //
        // The frame registered on the exception-handler stack is what lets
        // raise-exception's CONTINUABLE dispatch see this catch in its correct
        // innermost-first position (see ExceptionPrimitives.RaiseException); the
        // .NET catch itself is unchanged. The frame is popped BEFORE the handler
        // runs — "handler is invoked outside the scope of its own catch".
        interpreter.DefinePrimitive("catch", 3, 4, a =>
        {
            object key = a[0];
            object preUnwindHandler = a.Length > 3 ? a[3] : null;
            bool hasPreUnwindHandler = preUnwindHandler != null
                && !(preUnwindHandler is bool absent && !absent);
            List<ExceptionHandlerFrame> handlers = interpreter.Exceptions.Handlers;
            handlers.Add(new ExceptionHandlerFrame(
                ExceptionHandlerFrameKind.CatchFrame, null, key));
            SchemeThrow caught;
            try
            {
                try
                {
                    return hasPreUnwindHandler
                        ? RunWithThrowHandler(interpreter, key, a[1], preUnwindHandler)
                        : interpreter.Evaluator.Apply(a[1], Array.Empty<object>());
                }
                catch (SchemeThrow thrown)
                {
                    if (!ThrowKeyMatches(key, thrown))
                    {
                        throw;
                    }

                    caught = thrown;
                }
            }
            finally
            {
                handlers.RemoveAt(handlers.Count - 1);
            }

            return interpreter.Evaluator.Apply(a[2], ThrowHandlerArguments(caught));
        });

        // (with-throw-handler key thunk handler) -- boot-9.scm:1770-1806: validate the
        // key, run the thunk, and on a matching throw run the handler BEFORE the stack
        // unwinds, then let the throw keep propagating. ice-9/format.scm depends on the
        // handler actually running: format-error installs its report-with-bare-display
        // base case through this primitive.
        interpreter.DefinePrimitive("with-throw-handler", 3, 3, a =>
        {
            if (!(a[0] is Symbol) && !(a[0] is bool anyKey && anyKey))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("with-throw-handler"),
                        new MutableString("Wrong type argument in position ~a: ~a"),
                        Pair.List(1, a[0]),
                        Pair.List(a[0])));
            }

            return RunWithThrowHandler(interpreter, a[0], a[1], a[2]);
        });

        // dynamic-wind without full continuations reduces to try/finally, which is
        // correct for every non-local exit this interpreter can currently produce.
        interpreter.DefinePrimitive("dynamic-wind", 3, 3, a =>
        {
            interpreter.Evaluator.Apply(a[0], Array.Empty<object>());
            try
            {
                return interpreter.Evaluator.Apply(a[1], Array.Empty<object>());
            }
            finally
            {
                interpreter.Evaluator.Apply(a[2], Array.Empty<object>());
            }
        });
    }

    // The .NET exception filter gives Guile's pre-unwind contract almost literally: it
    // runs during the first pass, before any finally block or catch frame between the
    // throw point and here has unwound, and returning false declines the catch so the
    // throw continues propagating with the stack intact.
    //
    // Deliberate divergences from boot-9.scm, both bounded:
    //  - Non-reentrancy uses a per-invocation captured flag rather than Guile's
    //    running? fluid (boot-9.scm:1777-1785).
    //  - The %exception-epoch restart behaviour (boot-9.scm:1787-1796) is not
    //    implemented: a throw escaping the handler is discarded and the ORIGINAL throw
    //    continues, rather than restarting exception dispatch from the top.
    private static object RunWithThrowHandler(
        Interpreter interpreter, object key, object thunk, object handler)
    {
        bool running = false;
        List<ExceptionHandlerFrame> handlers = interpreter.Exceptions.Handlers;
        handlers.Add(new ExceptionHandlerFrame(
            ExceptionHandlerFrameKind.ThrowHandlerFrame, null, key));
        try
        {
            return interpreter.Evaluator.Apply(thunk, Array.Empty<object>());
        }
        catch (SchemeThrow thrown)
            when (InvokeThrowHandler(interpreter, key, handler, thrown, ref running))
        {
            // The filter always declines, so this body is unreachable; the rethrow
            // keeps the primitive correct even if that ever changes.
            throw;
        }
        finally
        {
            handlers.RemoveAt(handlers.Count - 1);
        }
    }

    private static bool InvokeThrowHandler(
        Interpreter interpreter, object key, object handler, SchemeThrow thrown, ref bool running)
    {
        if (ThrowKeyMatches(key, thrown) && !running)
        {
            running = true;
            try
            {
                interpreter.Evaluator.Apply(handler, ThrowHandlerArguments(thrown));
            }
            catch (SchemeThrow)
            {
                // Guile would restart exception dispatch from the top here; see the
                // divergence note above -- the original throw continues instead.
            }
            finally
            {
                running = false;
            }
        }

        return false;
    }

    private static bool ThrowKeyMatches(object key, SchemeThrow thrown)
    {
        bool matches = key is bool flag && flag;
        if (!matches)
        {
            matches = CorePrimitives.Eq(key, thrown.Key);
        }

        return matches;
    }

    private static object[] ThrowHandlerArguments(SchemeThrow thrown)
    {
        List<object> handlerArguments = new List<object> { thrown.Key };
        handlerArguments.AddRange(Pair.ToList(thrown.Arguments));
        return handlerArguments.ToArray();
    }

    /// <summary>
    /// Builds Guile's <c>'arglist</c> property value —
    /// <c>(required optional keyword allow-other-keys? rest)</c> — from a procedure's
    /// actual shape. Required and optional may be name lists or plain counts; the
    /// vendored <c>ice-9/session.scm</c> accepts both.
    /// </summary>
    /// <param name="procedure">The procedure to describe.</param>
    /// <returns>The five-element arglist, or <see langword="false"/> when the shape
    /// is unknown.</returns>
    private static object SynthesizeArglist(Procedure procedure)
    {
        switch (procedure)
        {
            case Primitive primitive:
            {
                bool variadic = primitive.MaximumArgumentCount < 0;
                long optional = variadic
                    ? 0
                    : primitive.MaximumArgumentCount - primitive.MinimumArgumentCount;
                return Pair.List(
                    (long)primitive.MinimumArgumentCount,
                    optional,
                    Nil.Instance,
                    false,
                    variadic ? (object)Symbol.Intern("rest") : false);
            }

            case Closure closure:
            {
                LambdaSignature signature = closure.Signature;
                List<object> required = new List<object>(signature.Required);
                List<object> optionals = new List<object>();
                foreach (OptionalParameter parameter in signature.Optionals)
                {
                    optionals.Add(parameter.ParameterName);
                }

                List<object> keywords = new List<object>();
                foreach (OptionalParameter parameter in signature.Keywords)
                {
                    keywords.Add(new Pair(parameter.SelectingKeyword, parameter.ParameterName));
                }

                return Pair.List(
                    Pair.ListFrom(required),
                    Pair.ListFrom(optionals),
                    Pair.ListFrom(keywords),
                    signature.AllowOtherKeys,
                    signature.RestParameter ?? (object)false);
            }

            case TreeIlClosure treeIlClosure:
            {
                // lambda-case fields: (src req opt rest kw inits gensyms body alternate).
                // kw is #f or (allow-other-keys? (keyword name gensym) ...).
                object[] fields = treeIlClosure.LambdaCase.Fields;
                object allowOtherKeys = false;
                object keywords = Nil.Instance;
                if (fields[4] is Pair kw)
                {
                    allowOtherKeys = kw.Car;
                    keywords = kw.Cdr;
                }

                return Pair.List(
                    fields[1],
                    fields[2] is Pair || fields[2] is Nil ? fields[2] : Nil.Instance,
                    keywords,
                    allowOtherKeys,
                    fields[3] is Symbol restName ? restName : (object)false);
            }

            default:
                // In real Guile every procedure is a VM program and introspectable, so
                // procedure-arguments never answers #f there. The closest honest shape
                // for a procedure kind with no recorded signature is "variadic,
                // nothing known": callers get well-formed lists rather than #f.
                return Pair.List(0L, 0L, Nil.Instance, false, Symbol.Intern("rest"));
        }
    }

    private static void InstallProcedureMetadata(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("procedure-name", 1, 1, a =>
        {
            // The `name' procedure PROPERTY wins over the definition-time name, as it
            // does in scm_procedure_name -- see Procedure.EffectiveName.
            string name = (a[0] as Procedure)?.EffectiveName;
            return name == null ? (object)false : Symbol.Intern(name);
        });

        interpreter.DefinePrimitive("procedure-property", 2, 2, a =>
        {
            if (!(a[0] is Procedure procedure))
            {
                return false;
            }

            foreach (object entry in Pair.ToList(procedure.Properties))
            {
                if (entry is Pair pair && CorePrimitives.Eq(pair.Car, a[1]))
                {
                    return pair.Cdr;
                }
            }

            // Guile publishes each procedure's signature as the 'arglist property —
            // (required optional keyword allow-other-keys? rest) — and the vendored
            // ice-9/session.scm builds procedure-arguments from it. LilyScheme
            // synthesizes it on demand from what the procedure actually is.
            if (a[1] is Symbol key && key.Name == "arglist")
            {
                return SynthesizeArglist(procedure);
            }

            return false;
        });

        interpreter.DefinePrimitive("set-procedure-property!", 3, 3, a =>
        {
            if (a[0] is Procedure procedure)
            {
                procedure.Properties = new Pair(new Pair(a[1], a[2]), procedure.Properties);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("procedure-properties", 1, 1, a =>
            (a[0] as Procedure)?.Properties ?? (object)Nil.Instance);

        interpreter.DefinePrimitive("procedure-source", 1, 1, a => false);
        // An applicable smob answers procedure? in Guile too — scm_procedure_p accepts
        // anything the apply path accepts — so IApplicable counts here as well.
        interpreter.DefinePrimitive("procedure?", 1, 1, a => a[0] is Procedure || a[0] is IApplicable);

        // make-procedure-with-setter and procedure-with-setter? live in
        // GuileCorePrimitives.InstallSetters, which runs later and so has always won.
        // Placeholders answering "no setter" used to sit here as well; they were removed
        // rather than left, because the pair only worked by install order -- reordering the
        // two Install calls would have made every accessor silently DISCARD its setter.
    }

    private static void InstallFluids(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("make-fluid", 0, 1, a => new Fluid(a.Length > 0 ? a[0] : false));
        interpreter.DefinePrimitive("fluid?", 1, 1, a => a[0] is Fluid);
        interpreter.DefinePrimitive("fluid-ref", 1, 1, a => ((Fluid)a[0]).Value);
        interpreter.DefinePrimitive("fluid-set!", 2, 2, a =>
        {
            ((Fluid)a[0]).Value = a[1];
            return Unspecified.Instance;
        });

        // (with-fluid* fluid value thunk) rebinds for the dynamic extent of the call.
        interpreter.DefinePrimitive("with-fluid*", 3, 3, a =>
        {
            Fluid fluid = (Fluid)a[0];
            object saved = fluid.Value;
            fluid.Value = a[1];
            try
            {
                return interpreter.Evaluator.Apply(a[2], Array.Empty<object>());
            }
            finally
            {
                fluid.Value = saved;
            }
        });

        interpreter.DefinePrimitive("with-fluids*", 3, 3, a =>
        {
            List<object> fluids = Pair.ToList(a[0]);
            List<object> values = Pair.ToList(a[1]);
            object[] saved = new object[fluids.Count];
            for (int i = 0; i < fluids.Count; i++)
            {
                saved[i] = ((Fluid)fluids[i]).Value;
                ((Fluid)fluids[i]).Value = i < values.Count ? values[i] : false;
            }

            try
            {
                return interpreter.Evaluator.Apply(a[2], Array.Empty<object>());
            }
            finally
            {
                for (int i = 0; i < fluids.Count; i++)
                {
                    ((Fluid)fluids[i]).Value = saved[i];
                }
            }
        });
    }
}
