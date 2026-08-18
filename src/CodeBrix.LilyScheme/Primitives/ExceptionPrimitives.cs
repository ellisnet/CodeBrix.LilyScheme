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

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Guile 3's modern exception API — exception objects, <c>raise-exception</c> and
/// <c>with-exception-handler</c> — built to boot-9.scm:1448-1861 and interoperating
/// with the classic <c>catch</c>/<c>throw</c> machinery in both directions: a
/// <c>catch</c> sees a raised exception through its kind and args, and an exception
/// handler sees a plain <c>throw</c> (including every SchemeThrow a C# primitive
/// raises) as the object <c>make-exception-from-throw</c> answers.
/// <para>
/// The dispatch design differs from boot-9's fluid walk because the classic side here
/// is .NET exceptions rather than prompts: a non-continuable
/// <c>raise-exception</c> simply throws the <see cref="SchemeThrow"/> its exception
/// object decodes to, and .NET propagation visits every intervening frame innermost
/// first — which IS boot-9's handler-stack order. Only the continuable case walks the
/// explicit handler stack, because a non-unwinding handler's return value must flow
/// back to the raise point, which no .NET throw can do.
/// </para>
/// <para>
/// DIVERGENCE, recorded: a non-local exit from a non-unwinding handler continues from
/// the <c>with-exception-handler</c> frame rather than from the raise point, so frames
/// BETWEEN the two do not see it. This is the same bounded shape as the
/// with-throw-handler divergence noted in ControlPrimitives; the vendored
/// <c>guard</c> is unaffected because its prompt sits outside its handler.
/// </para>
/// </summary>
public static class ExceptionPrimitives
{
    /// <summary>Installs the modern exception API's primitives and the standard
    /// exception record types.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallTypes(interpreter);
        InstallObjectProcedures(interpreter);
        InstallRaiseAndHandle(interpreter);
        InstallPrinting(interpreter);
    }

    private static void InstallTypes(Interpreter interpreter)
    {
        ExceptionRuntime state = interpreter.Exceptions;

        // boot-9.scm:1448-1554, layout for layout: &compound-exception is deliberately
        // NOT a subtype of &exception; &exception-with-kind-and-args and
        // &quit-exception are SEALED; the &error chain is extensible.
        state.ExceptionType = new RecordType(
            "&exception", Array.Empty<object>(), null, true, null);
        state.CompoundExceptionType = new RecordType(
            "&compound-exception",
            new object[] { Symbol.Intern("components") },
            null,
            false,
            new[] { false });
        state.KindAndArgsType = new RecordType(
            "&exception-with-kind-and-args",
            new object[] { Symbol.Intern("kind"), Symbol.Intern("args") },
            state.ExceptionType,
            false,
            new[] { false, false });
        state.QuitExceptionType = new RecordType(
            "&quit-exception",
            new object[] { Symbol.Intern("code") },
            state.ExceptionType,
            false,
            new[] { false });
        state.ErrorType = new RecordType(
            "&error", Array.Empty<object>(), state.ExceptionType, true, null);
        state.ProgrammingErrorType = new RecordType(
            "&programming-error", Array.Empty<object>(), state.ErrorType, true, null);
        state.NonContinuableType = new RecordType(
            "&non-continuable", Array.Empty<object>(), state.ProgrammingErrorType, true, null);

        interpreter.DefineValue("&exception", state.ExceptionType);
        interpreter.DefineValue("&compound-exception", state.CompoundExceptionType);
        interpreter.DefineValue("&exception-with-kind-and-args", state.KindAndArgsType);
        interpreter.DefineValue("&quit-exception", state.QuitExceptionType);
        interpreter.DefineValue("&error", state.ErrorType);
        interpreter.DefineValue("&programming-error", state.ProgrammingErrorType);
        interpreter.DefineValue("&non-continuable", state.NonContinuableType);
    }

    private static void InstallObjectProcedures(Interpreter interpreter)
    {
        ExceptionRuntime state = interpreter.Exceptions;

        interpreter.DefinePrimitive("exception?", 1, 1, a => IsExceptionObject(interpreter, a[0]));

        interpreter.DefinePrimitive("exception-type?", 1, 1, a =>
            a[0] is RecordType type && type.HasParent(state.ExceptionType));

        interpreter.DefinePrimitive("make-exception-type", 3, 3, a =>
        {
            if (!(a[1] is RecordType parent && parent.HasParent(state.ExceptionType)))
            {
                throw MiscError(
                    "make-exception-type", "parent is not a exception type: ~S", a[1]);
            }

            List<object> fields = Pair.ToList(a[2]);
            foreach (object field in fields)
            {
                if (!(field is Symbol))
                {
                    throw MiscError(
                        "make-exception-type",
                        "field names should be a list of symbols: ~S",
                        a[2]);
                }
            }

            return new RecordType(
                StringPrimitives.Text(a[0], "make-exception-type"), fields, parent, true, null);
        });

        interpreter.DefinePrimitive("make-exception", 0, -1, a => MakeException(interpreter, a));

        interpreter.DefinePrimitive("simple-exceptions", 1, 1, a =>
            Pair.ListFrom(SimpleExceptionsOf(interpreter, a[0])));

        interpreter.DefinePrimitive("exception-predicate", 1, 1, a =>
        {
            if (!(a[0] is RecordType type))
            {
                throw MiscError("exception-predicate", "not a record type: ~S", a[0]);
            }

            return new Primitive(type.Name + "?", 1, 1, arguments =>
                MatchesExceptionType(interpreter, arguments[0], type));
        });

        interpreter.DefinePrimitive("exception-accessor", 2, 2, a =>
        {
            if (!(a[0] is RecordType type))
            {
                throw MiscError("exception-accessor", "not a record type: ~S", a[0]);
            }

            object accessor = a[1];
            return new Primitive(type.Name + "-ref", 1, 1, arguments =>
            {
                object component = FindComponent(interpreter, arguments[0], type);
                if (component == null)
                {
                    throw MiscError(
                        "exception-accessor",
                        "object is not an exception of the right type: ~S",
                        arguments[0]);
                }

                return interpreter.Evaluator.Apply(accessor, new[] { component });
            });
        });

        interpreter.DefinePrimitive("exception-kind", 1, 1, a => KindOf(interpreter, a[0]));
        interpreter.DefinePrimitive("exception-args", 1, 1, a => ArgsOf(interpreter, a[0]));

        // The BOOT definition; the vendored ice-9/exceptions.scm upgrades this binding
        // with set! to its converter table, exactly as it does over Guile's own boot
        // version. Conversion sites read the variable LIVE for that reason.
        interpreter.DefinePrimitive("make-exception-from-throw", 2, 2, a =>
            BootMakeExceptionFromThrow(interpreter, a[0], a[1]));
        state.MakeExceptionFromThrow
            = interpreter.GuileModule.LookupLocal(Symbol.Intern("make-exception-from-throw"));
    }

    private static void InstallRaiseAndHandle(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("raise-exception", 1, 3, a =>
        {
            bool continuable = false;
            for (int i = 1; i + 1 < a.Length; i += 2)
            {
                if (a[i] is Keyword keyword && keyword.Name.Name == "continuable?")
                {
                    continuable = Truthy(a[i + 1]);
                }
                else
                {
                    throw UnrecognizedKeyword("raise-exception", a[i]);
                }
            }

            return RaiseException(interpreter, a[0], continuable);
        });

        interpreter.DefinePrimitive("with-exception-handler", 2, 6, a =>
        {
            object handler = a[0];
            object thunk = a[1];
            if (!(handler is Procedure || handler is IApplicable))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("with-exception-handler"),
                        new MutableString("Wrong type argument in position ~a: ~a"),
                        Pair.List(1L, handler),
                        Pair.List(handler)));
            }

            bool unwind = false;
            object unwindForType = true;
            for (int i = 2; i + 1 < a.Length; i += 2)
            {
                if (!(a[i] is Keyword keyword))
                {
                    throw UnrecognizedKeyword("with-exception-handler", a[i]);
                }

                switch (keyword.Name.Name)
                {
                    case "unwind?":
                        unwind = Truthy(a[i + 1]);
                        break;
                    case "unwind-for-type":
                        unwindForType = a[i + 1];
                        break;
                    default:
                        throw UnrecognizedKeyword("with-exception-handler", keyword);
                }
            }

            if (!unwind)
            {
                return RunNonUnwinding(interpreter, handler, thunk);
            }

            bool validType = (unwindForType is bool anyType && anyType)
                || unwindForType is Symbol
                || (unwindForType is RecordType candidate
                    && candidate.HasParent(interpreter.Exceptions.ExceptionType));
            if (!validType)
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("with-exception-handler"),
                        new MutableString("Wrong type argument for #:unwind-for-type: ~a"),
                        Pair.List(unwindForType),
                        Pair.List(unwindForType)));
            }

            return RunUnwinding(interpreter, handler, thunk, unwindForType);
        });
    }

    private static void InstallPrinting(Interpreter interpreter)
    {
        ExceptionRuntime state = interpreter.Exceptions;
        state.Display = interpreter.GuileModule.LookupLocal(Symbol.Intern("display"));

        // boot-9.scm:1885-1887: acons, so a re-registration shadows.
        interpreter.DefinePrimitive("set-exception-printer!", 2, 2, a =>
        {
            state.ExceptionPrinters = new Pair(new Pair(a[0], a[1]), state.ExceptionPrinters);
            return Unspecified.Instance;
        });

        // boot-9.scm:1889-1912. There are no stack frames here, so the frame argument
        // is accepted and ignored; everything else is that procedure: consult the
        // per-key printer, fall back to the default line, guard against a printer that
        // itself errors, and end with a newline.
        interpreter.DefinePrimitive("print-exception", 4, 4, a =>
        {
            object port = a[0];
            object key = a[2];
            object args = a[3];
            Primitive defaultPrinter = new Primitive("default-printer", 0, 0, _ =>
            {
                WriteTo(
                    interpreter,
                    port,
                    "Throw to key `" + Printer.Display(key) + "' with args `"
                    + Printer.Write(args) + "'.");
                return Unspecified.Instance;
            });

            object printer = AssqRef(state.ExceptionPrinters, key);
            try
            {
                if (Truthy(printer))
                {
                    interpreter.Evaluator.Apply(printer, new[] { port, key, args, defaultPrinter });
                }
                else
                {
                    interpreter.Evaluator.Apply(defaultPrinter, Array.Empty<object>());
                }
            }
            catch (SchemeThrow)
            {
                WriteTo(interpreter, port, "Error while printing exception.");
            }

            WriteTo(interpreter, port, "\n");
            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Raises an exception object. The non-continuable case throws the
    /// <see cref="SchemeThrow"/> the object decodes to, and .NET propagation reaches
    /// every frame in boot-9's dispatch order. The continuable case walks the handler
    /// stack: when the innermost live interceptor is a non-unwinding handler it is
    /// called in place and its value returned — otherwise the value could never come
    /// back, which is also true in Guile, so the throw path is taken.
    /// </summary>
    /// <param name="interpreter">The interpreter whose handler stack applies.</param>
    /// <param name="exception">The exception object (any value, as in Guile).</param>
    /// <param name="continuable">Whether the handler's value returns to the raise point.</param>
    /// <returns>The handler's value, for a continuable raise a non-unwinding handler
    /// answered.</returns>
    internal static object RaiseException(Interpreter interpreter, object exception, bool continuable)
    {
        if (continuable)
        {
            List<ExceptionHandlerFrame> handlers = interpreter.Exceptions.Handlers;
            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                ExceptionHandlerFrame frame = handlers[i];
                if (frame.Disabled)
                {
                    continue;
                }

                if (frame.Kind == ExceptionHandlerFrameKind.NonUnwinding)
                {
                    frame.Disabled = true;
                    try
                    {
                        return interpreter.Evaluator.Apply(frame.Handler, new[] { exception });
                    }
                    finally
                    {
                        frame.Disabled = false;
                    }
                }

                bool intercepts = frame.Kind == ExceptionHandlerFrameKind.Unwinding
                    ? ExceptionHasType(interpreter, exception, frame.TypeOrKey)
                    : KeyMatches(frame.TypeOrKey, KindOf(interpreter, exception));
                if (intercepts)
                {
                    break;
                }
            }
        }

        throw ThrowFor(interpreter, exception);
    }

    /// <summary>
    /// Recovers the exception OBJECT a <see cref="SchemeThrow"/> carries, converting a
    /// plain throw through the live <c>make-exception-from-throw</c> binding and
    /// caching the answer on the throw. Never raises: it runs inside .NET exception
    /// filters, where an escaping exception would be silently swallowed by the CLR, so
    /// a failing converter falls back to the boot conversion, which cannot fail.
    /// </summary>
    /// <param name="interpreter">The interpreter whose converter binding applies.</param>
    /// <param name="thrown">The in-flight throw.</param>
    /// <returns>The exception object.</returns>
    internal static object ExceptionObjectOf(Interpreter interpreter, SchemeThrow thrown)
    {
        if (thrown.ExceptionObject != null)
        {
            return thrown.ExceptionObject;
        }

        object exception;
        try
        {
            Variable converter = interpreter.Exceptions.MakeExceptionFromThrow;
            exception = converter == null
                ? BootMakeExceptionFromThrow(interpreter, thrown.Key, thrown.Arguments)
                : interpreter.Evaluator.Apply(
                    converter.GetValue(), new[] { thrown.Key, thrown.Arguments });
        }
        catch (Exception)
        {
            exception = BootMakeExceptionFromThrow(interpreter, thrown.Key, thrown.Arguments);
        }

        thrown.ExceptionObject = exception;
        return exception;
    }

    /// <summary>Builds the <see cref="SchemeThrow"/> an exception object travels as:
    /// its kind and args, with the object attached so a handler recovers it identically.</summary>
    /// <param name="interpreter">The interpreter whose exception types apply.</param>
    /// <param name="exception">The exception object.</param>
    /// <returns>The throw to raise.</returns>
    internal static SchemeThrow ThrowFor(Interpreter interpreter, object exception)
        => new SchemeThrow(KindOf(interpreter, exception), ArgsOf(interpreter, exception))
        {
            ExceptionObject = exception,
        };

    private static object RunNonUnwinding(Interpreter interpreter, object handler, object thunk)
    {
        List<ExceptionHandlerFrame> handlers = interpreter.Exceptions.Handlers;
        ExceptionHandlerFrame frame = new ExceptionHandlerFrame(
            ExceptionHandlerFrameKind.NonUnwinding, handler, null);
        handlers.Add(frame);
        Exception[] replacement = new Exception[1];
        try
        {
            try
            {
                return interpreter.Evaluator.Apply(thunk, Array.Empty<object>());
            }
            catch (SchemeThrow thrown)
                when (HandleNonUnwinding(interpreter, frame, thrown, replacement))
            {
                throw replacement[0];
            }
        }
        finally
        {
            handlers.RemoveAt(handlers.Count - 1);
        }
    }

    // Runs during the FIRST pass, before anything between the throw point and this
    // frame has unwound — Guile's "dynamic environment of the raise-exception call".
    // The handler's own non-local exits and the &non-continuable a returning handler
    // provokes are carried out through the catch block, because an exception escaping
    // a filter is silently swallowed by the CLR.
    private static bool HandleNonUnwinding(
        Interpreter interpreter,
        ExceptionHandlerFrame frame,
        SchemeThrow thrown,
        Exception[] replacement)
    {
        if (frame.Disabled)
        {
            return false;
        }

        frame.Disabled = true;
        object exception = ExceptionObjectOf(interpreter, thrown);
        try
        {
            interpreter.Evaluator.Apply(frame.Handler, new[] { exception });

            // The handler RETURNED on a non-continuable raise: a fresh
            // &non-continuable propagates from here, with this handler out of play —
            // boot-9.scm:1676-1679.
            replacement[0] = ThrowFor(
                interpreter, new object[] { interpreter.Exceptions.NonContinuableType });
        }
        catch (Exception escaped)
        {
            replacement[0] = escaped;
        }

        return true;
    }

    private static object RunUnwinding(
        Interpreter interpreter, object handler, object thunk, object unwindForType)
    {
        List<ExceptionHandlerFrame> handlers = interpreter.Exceptions.Handlers;
        ExceptionHandlerFrame frame = new ExceptionHandlerFrame(
            ExceptionHandlerFrameKind.Unwinding, null, unwindForType);
        handlers.Add(frame);
        SchemeThrow caught = null;
        try
        {
            try
            {
                return interpreter.Evaluator.Apply(thunk, Array.Empty<object>());
            }
            catch (SchemeThrow thrown)
                when (MatchesForUnwind(interpreter, thrown, unwindForType))
            {
                caught = thrown;
            }
        }
        finally
        {
            handlers.RemoveAt(handlers.Count - 1);
        }

        // The stack HAS unwound and the frame is gone: the handler runs with the
        // continuation of the with-exception-handler call, as boot-9's prompt gives it.
        return interpreter.Evaluator.Apply(
            handler, new[] { ExceptionObjectOf(interpreter, caught) });
    }

    private static bool MatchesForUnwind(
        Interpreter interpreter, SchemeThrow thrown, object unwindForType)
    {
        switch (unwindForType)
        {
            case bool any:
                return any;
            case Symbol kind:
                return CorePrimitives.Eq(thrown.Key, kind);
            case RecordType type:
                return MatchesExceptionType(
                    interpreter, ExceptionObjectOf(interpreter, thrown), type);
            default:
                return false;
        }
    }

    /// <summary>Answers boot-9's <c>exception-has-type?</c>: <c>#t</c> matches
    /// everything, a symbol matches the exception's kind, an exception type matches
    /// through the predicate.</summary>
    /// <param name="interpreter">The interpreter whose exception types apply.</param>
    /// <param name="exception">The exception object.</param>
    /// <param name="type">The type condition.</param>
    /// <returns>Whether the exception matches.</returns>
    internal static bool ExceptionHasType(Interpreter interpreter, object exception, object type)
    {
        switch (type)
        {
            case bool any:
                return any;
            case Symbol kind:
                return CorePrimitives.Eq(KindOf(interpreter, exception), kind);
            case RecordType recordType:
                return MatchesExceptionType(interpreter, exception, recordType);
            default:
                return false;
        }
    }

    private static bool KeyMatches(object key, object kind)
        => (key is bool any && any) || CorePrimitives.Eq(key, kind);

    private static object KindOf(Interpreter interpreter, object exception)
    {
        object[] component = FindComponent(
            interpreter, exception, interpreter.Exceptions.KindAndArgsType);
        return component != null ? component[1] : Symbol.Intern("%exception");
    }

    private static object ArgsOf(Interpreter interpreter, object exception)
    {
        object[] component = FindComponent(
            interpreter, exception, interpreter.Exceptions.KindAndArgsType);
        return component != null ? component[2] : Pair.List(exception);
    }

    private static bool IsExceptionObject(Interpreter interpreter, object value)
        => interpreter.Exceptions.ExceptionType.IsInstance(value)
           || interpreter.Exceptions.CompoundExceptionType.IsInstance(value);

    private static bool MatchesExceptionType(
        Interpreter interpreter, object value, RecordType type)
        => FindComponent(interpreter, value, type) != null;

    private static object[] FindComponent(Interpreter interpreter, object value, RecordType type)
    {
        if (type.IsInstance(value))
        {
            return (object[])value;
        }

        if (interpreter.Exceptions.CompoundExceptionType.IsInstance(value))
        {
            foreach (object component in Pair.ToList(((object[])value)[1]))
            {
                if (type.IsInstance(component))
                {
                    return (object[])component;
                }
            }
        }

        return null;
    }

    private static List<object> SimpleExceptionsOf(Interpreter interpreter, object value)
    {
        if (interpreter.Exceptions.CompoundExceptionType.IsInstance(value))
        {
            return Pair.ToList(((object[])value)[1]);
        }

        if (interpreter.Exceptions.ExceptionType.IsInstance(value))
        {
            return new List<object> { value };
        }

        throw MiscError("simple-exceptions", "not a exception: ~S", value);
    }

    private static object MakeException(Interpreter interpreter, object[] exceptions)
    {
        List<object> simple = new List<object>();
        foreach (object exception in exceptions)
        {
            simple.AddRange(SimpleExceptionsOf(interpreter, exception));
        }

        if (simple.Count == 1)
        {
            return simple[0];
        }

        return new object[]
        {
            interpreter.Exceptions.CompoundExceptionType, Pair.ListFrom(simple),
        };
    }

    private static object BootMakeExceptionFromThrow(
        Interpreter interpreter, object key, object arguments)
    {
        ExceptionRuntime state = interpreter.Exceptions;
        object[] kindAndArgs = { state.KindAndArgsType, key, arguments };
        if (!(key is Symbol symbol && symbol.Name == "quit"))
        {
            return kindAndArgs;
        }

        // boot-9.scm:1565-1573: a quit throw pairs a &quit-exception carrying the exit
        // code with the kind-and-args record.
        object code;
        if (!(arguments is Pair pair))
        {
            code = 0L;
        }
        else if (pair.Car is long exitCode)
        {
            code = exitCode;
        }
        else if (pair.Car is bool flag && !flag)
        {
            code = 1L;
        }
        else
        {
            code = 0L;
        }

        object[] quit = { state.QuitExceptionType, code };
        return MakeException(interpreter, new object[] { quit, kindAndArgs });
    }

    private static void WriteTo(Interpreter interpreter, object port, string text)
    {
        Variable display = interpreter.Exceptions.Display;
        if (display != null)
        {
            interpreter.Evaluator.Apply(display.GetValue(), new object[] { new MutableString(text), port });
        }
    }

    private static object AssqRef(object alist, object key)
    {
        object current = alist;
        while (current is Pair pair)
        {
            if (pair.Car is Pair entry && CorePrimitives.Eq(entry.Car, key))
            {
                return entry.Cdr;
            }

            current = pair.Cdr;
        }

        return false;
    }

    private static bool Truthy(object value) => !(value is bool flag && !flag);

    private static SchemeThrow MiscError(string procedureName, string message, object irritant)
        => new SchemeThrow(
            Symbol.Intern("misc-error"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString(message),
                Pair.List(irritant),
                false));

    private static SchemeThrow UnrecognizedKeyword(string procedureName, object keyword)
        => new SchemeThrow(
            Symbol.Intern("keyword-argument-error"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Unrecognized keyword"),
                Nil.Instance,
                Pair.List(keyword)));
}
