// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// The core evaluator. Evaluates Scheme source forms directly, with proper tail calls
/// implemented by looping rather than recursing — which matters because psyntax is
/// written in a heavily tail-recursive style and would otherwise exhaust the stack.
/// </summary>
public sealed class Evaluator
{
    private readonly Interpreter _interpreter;

    /// <summary>Initializes an evaluator bound to an interpreter.</summary>
    /// <param name="interpreter">The owning interpreter.</param>
    public Evaluator(Interpreter interpreter)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
    }

    /// <summary>Evaluates an expression.</summary>
    /// <param name="expression">The form to evaluate.</param>
    /// <param name="environment">The lexical environment, or <see langword="null"/> at top level.</param>
    /// <param name="module">The module providing top-level bindings.</param>
    /// <returns>The value of the expression.</returns>
    public object Eval(object expression, LexicalEnvironment environment, SchemeModule module)
    {
        while (true)
        {
            // ---- self-evaluating and variable references --------------------------
            if (expression is Symbol symbol)
            {
                Variable variable = environment?.Lookup(symbol) ?? module.Lookup(symbol);
                if (variable == null && environment != null)
                {
                    variable = module.Lookup(symbol);
                }

                if (variable == null)
                {
                    throw new SchemeThrow(
                        Symbol.Intern("unbound-variable"),
                        Pair.List(false, new MutableString("Unbound variable: ~S"), Pair.List(symbol), false));
                }

                if (!variable.IsBound)
                {
                    throw new SchemeThrow(
                        Symbol.Intern("unbound-variable"),
                        Pair.List(false, new MutableString("Unbound variable: ~S"), Pair.List(symbol), false));
                }

                return variable.GetValue();
            }

            if (!(expression is Pair form))
            {
                // Numbers, strings, booleans, characters, keywords, vectors and the empty
                // list all evaluate to themselves.
                return expression;
            }

            // ---- special forms ------------------------------------------------------
            if (form.Car is Symbol head)
            {
                // A lexical or module binding shadows a special form, matching Guile:
                // (let ((if list)) (if 1 2 3)) calls the procedure.
                bool shadowed = environment?.Lookup(head) != null;
                if (!shadowed)
                {
                    if (ReferenceEquals(head, Symbol.Quote))
                    {
                        return Second(form);
                    }

                    if (ReferenceEquals(head, Symbol.If))
                    {
                        object test = Eval(Second(form), environment, module);
                        object rest = ((Pair)form.Cdr).Cdr;
                        if (IsTrue(test))
                        {
                            expression = ((Pair)rest).Car;
                            continue;
                        }

                        object alternate = ((Pair)rest).Cdr;
                        if (alternate is Pair alternatePair)
                        {
                            expression = alternatePair.Car;
                            continue;
                        }

                        return Unspecified.Instance;
                    }

                    if (ReferenceEquals(head, Symbol.Lambda))
                    {
                        return MakeClosure(Second(form), Cddr(form), environment, module, false);
                    }

                    if (ReferenceEquals(head, Symbol.LambdaStar))
                    {
                        return MakeClosure(Second(form), Cddr(form), environment, module, true);
                    }

                    if (ReferenceEquals(head, Symbol.Define))
                    {
                        return EvalDefine(form, environment, module);
                    }

                    if (ReferenceEquals(head, Symbol.SetBang))
                    {
                        Symbol target = (Symbol)Second(form);
                        object value = Eval(Third(form), environment, module);
                        Variable variable = environment?.Lookup(target) ?? module.Lookup(target);
                        if (variable == null)
                        {
                            variable = module.Lookup(target);
                        }

                        if (variable == null)
                        {
                            throw new SchemeThrow(
                                Symbol.Intern("unbound-variable"),
                                Pair.List(false, new MutableString("Unbound variable: ~S"), Pair.List(target), false));
                        }

                        variable.SetValue(value);
                        return Unspecified.Instance;
                    }

                    if (ReferenceEquals(head, Symbol.Begin))
                    {
                        object body = form.Cdr;
                        if (!(body is Pair))
                        {
                            return Unspecified.Instance;
                        }

                        while (((Pair)body).Cdr is Pair)
                        {
                            Eval(((Pair)body).Car, environment, module);
                            body = ((Pair)body).Cdr;
                        }

                        expression = ((Pair)body).Car;
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.Let))
                    {
                        // Named let binds a procedure visible in its own body.
                        if (Second(form) is Symbol loopName)
                        {
                            object bindings = Third(form);
                            object loopBody = Cdddr(form);
                            List<Symbol> names = new List<Symbol>();
                            List<object> initials = new List<object>();
                            foreach (object binding in Pair.ToList(bindings))
                            {
                                names.Add((Symbol)First(binding));
                                initials.Add(Eval(Second(binding), environment, module));
                            }

                            LexicalEnvironment loopFrame = new LexicalEnvironment(environment, 1);
                            LambdaSignature signature = new LambdaSignature(names, null, null, null, false);
                            Closure loop = new Closure(signature, loopBody, loopFrame, module) { Name = loopName.Name };
                            loopFrame.Define(loopName, loop);
                            LexicalEnvironment callFrame = new LexicalEnvironment(loopFrame, names.Count);
                            for (int i = 0; i < names.Count; i++)
                            {
                                callFrame.Define(names[i], initials[i]);
                            }

                            environment = callFrame;
                            expression = new Pair(Symbol.Begin, loopBody);
                            continue;
                        }

                        LexicalEnvironment letFrame = new LexicalEnvironment(environment, 4);
                        foreach (object binding in Pair.ToList(Second(form)))
                        {
                            if (binding is Symbol bare)
                            {
                                letFrame.Define(bare, Unspecified.Instance);
                            }
                            else
                            {
                                letFrame.Define((Symbol)First(binding), Eval(Second(binding), environment, module));
                            }
                        }

                        environment = letFrame;
                        expression = new Pair(Symbol.Begin, Cddr(form));
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.LetStar))
                    {
                        LexicalEnvironment frame = new LexicalEnvironment(environment, 4);
                        foreach (object binding in Pair.ToList(Second(form)))
                        {
                            if (binding is Symbol bare)
                            {
                                frame.Define(bare, Unspecified.Instance);
                            }
                            else
                            {
                                // Each initializer sees the bindings before it, so evaluate
                                // in the frame being built rather than the outer one.
                                frame.Define((Symbol)First(binding), Eval(Second(binding), frame, module));
                            }
                        }

                        environment = frame;
                        expression = new Pair(Symbol.Begin, Cddr(form));
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.Letrec) || ReferenceEquals(head, Symbol.LetrecStar))
                    {
                        LexicalEnvironment frame = new LexicalEnvironment(environment, 4);
                        List<object> bindings = Pair.ToList(Second(form));

                        // All names are visible to all initializers, so create the slots
                        // first and fill them afterwards.
                        foreach (object binding in bindings)
                        {
                            Symbol name = binding is Symbol bare ? bare : (Symbol)First(binding);
                            frame.Define(name, Unspecified.Instance);
                        }

                        foreach (object binding in bindings)
                        {
                            if (binding is Symbol)
                            {
                                continue;
                            }

                            Symbol name = (Symbol)First(binding);
                            object value = Eval(Second(binding), frame, module);
                            if (value is Procedure procedure && procedure.Name == null)
                            {
                                procedure.Name = name.Name;
                            }

                            frame.Lookup(name).SetValue(value);
                        }

                        environment = frame;
                        expression = new Pair(Symbol.Begin, Cddr(form));
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.And))
                    {
                        object clauses = form.Cdr;
                        if (!(clauses is Pair))
                        {
                            return true;
                        }

                        while (((Pair)clauses).Cdr is Pair)
                        {
                            if (!IsTrue(Eval(((Pair)clauses).Car, environment, module)))
                            {
                                return false;
                            }

                            clauses = ((Pair)clauses).Cdr;
                        }

                        expression = ((Pair)clauses).Car;
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.Or))
                    {
                        object clauses = form.Cdr;
                        if (!(clauses is Pair))
                        {
                            return false;
                        }

                        while (((Pair)clauses).Cdr is Pair)
                        {
                            object value = Eval(((Pair)clauses).Car, environment, module);
                            if (IsTrue(value))
                            {
                                return value;
                            }

                            clauses = ((Pair)clauses).Cdr;
                        }

                        expression = ((Pair)clauses).Car;
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.Cond))
                    {
                        object result = null;
                        bool matched = false;
                        foreach (object clause in Pair.ToList(form.Cdr))
                        {
                            object testForm = First(clause);
                            if (testForm is Symbol elseSymbol && ReferenceEquals(elseSymbol, Symbol.Else))
                            {
                                expression = new Pair(Symbol.Begin, ((Pair)clause).Cdr);
                                matched = true;
                                break;
                            }

                            object testValue = Eval(testForm, environment, module);
                            if (!IsTrue(testValue))
                            {
                                continue;
                            }

                            object body = ((Pair)clause).Cdr;
                            if (!(body is Pair))
                            {
                                // (cond (test)) yields the test value itself.
                                return testValue;
                            }

                            if (((Pair)body).Car is Symbol arrow && ReferenceEquals(arrow, Symbol.Arrow))
                            {
                                object receiver = Eval(Second(body), environment, module);
                                result = Apply(receiver, new[] { testValue });
                                matched = true;
                                return result;
                            }

                            expression = new Pair(Symbol.Begin, body);
                            matched = true;
                            break;
                        }

                        if (matched)
                        {
                            continue;
                        }

                        return Unspecified.Instance;
                    }

                    if (ReferenceEquals(head, Symbol.When) || ReferenceEquals(head, Symbol.Unless))
                    {
                        bool wanted = ReferenceEquals(head, Symbol.When);
                        if (IsTrue(Eval(Second(form), environment, module)) == wanted)
                        {
                            expression = new Pair(Symbol.Begin, Cddr(form));
                            continue;
                        }

                        return Unspecified.Instance;
                    }

                    if (ReferenceEquals(head, Symbol.EvalWhen))
                    {
                        // A pure interpreter has no separate compilation phase, so every
                        // situation collapses to "now".
                        expression = new Pair(Symbol.Begin, Cddr(form));
                        continue;
                    }

                    if (ReferenceEquals(head, Symbol.CaseLambda))
                    {
                        return MakeCaseLambda(form.Cdr, environment, module);
                    }

                    if (ReferenceEquals(head, Symbol.Quasiquote))
                    {
                        return Quasiquote(Second(form), environment, module, 1);
                    }

                    if (ReferenceEquals(head, Symbol.Delay))
                    {
                        LambdaSignature thunkSignature = new LambdaSignature(Array.Empty<Symbol>(), null, null, null, false);
                        return new Promise(new Closure(thunkSignature, form.Cdr, environment, module));
                    }

                    if (ReferenceEquals(head, Symbol.DefineSyntax))
                    {
                        // Until psyntax is loaded there is no macro layer; record the
                        // definition so a later expander can find it.
                        Symbol macroName = Second(form) as Symbol;
                        if (macroName != null)
                        {
                            object transformer = Eval(Third(form), environment, module);
                            module.Define(macroName, new SyntaxTransformer(macroName, Symbol.Intern("macro"), transformer));
                        }

                        return Unspecified.Instance;
                    }
                }
            }

            // ---- application --------------------------------------------------------
            object @operator = Eval(form.Car, environment, module);
            List<object> argumentList = new List<object>(4);
            object cursor = form.Cdr;
            while (cursor is Pair argument)
            {
                argumentList.Add(Eval(argument.Car, environment, module));
                cursor = argument.Cdr;
            }

            object[] arguments = argumentList.ToArray();

            if (@operator is Closure closure)
            {
                // Tail call: rebind and loop instead of recursing.
                environment = BindArguments(closure, arguments);
                module = closure.Module;
                object body = closure.Body;
                if (!(body is Pair))
                {
                    return Unspecified.Instance;
                }

                while (((Pair)body).Cdr is Pair)
                {
                    Eval(((Pair)body).Car, environment, module);
                    body = ((Pair)body).Cdr;
                }

                expression = ((Pair)body).Car;
                continue;
            }

            return ApplyNonClosure(@operator, arguments);
        }
    }

    /// <summary>Applies a procedure to already-evaluated arguments.</summary>
    /// <param name="procedure">The procedure to call.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <returns>The result value.</returns>
    public object Apply(object procedure, object[] arguments)
    {
        if (procedure is Closure closure)
        {
            LexicalEnvironment frame = BindArguments(closure, arguments);
            return Eval(new Pair(Symbol.Begin, closure.Body), frame, closure.Module);
        }

        return ApplyNonClosure(procedure, arguments);
    }

    private object ApplyNonClosure(object procedure, object[] arguments)
    {
        if (procedure is Primitive primitive)
        {
            // A primitive declared generic-capable dispatches to the generic that
            // enable-primitive-generic! hung off it, when the arguments fail to apply to
            // the primitive itself. Selecting first and invoking otherwise is how that
            // reads here: LilyPond's methods all carry real specializers, so numbers never
            // match a <Moment> or <Pitch> method and ordinary arithmetic goes straight
            // through. The check precedes the arity check because a method may legitimately
            // accept a shape the primitive does not.
            if (primitive.AttachedGeneric is Primitives.GenericFunction attached)
            {
                Primitives.GenericMethod specialized = attached.Select(arguments);
                if (specialized != null)
                {
                    return Apply(specialized.Implementation, arguments);
                }
            }

            if (arguments.Length < primitive.MinimumArgumentCount
                || (primitive.MaximumArgumentCount >= 0 && arguments.Length > primitive.MaximumArgumentCount))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-number-of-args"),
                    Pair.List(
                        new MutableString(primitive.Name ?? "primitive"),
                        new MutableString("Wrong number of arguments"),
                        Nil.Instance,
                        false));
            }

            return primitive.Invoke(arguments);
        }

        if (procedure is TreeIl.TreeIlClosure treeIlClosure)
        {
            // Procedures produced by psyntax-expanded code have Tree-IL bodies, and can
            // reach C# primitives such as map and sort. Hand them to the Tree-IL
            // evaluator rather than failing as an unknown callable.
            return _interpreter.TreeIlEvaluator.ApplyClosure(treeIlClosure, arguments);
        }

        if (procedure is Primitives.GenericFunction generic)
        {
            Primitives.GenericMethod method = generic.Select(arguments);
            if (method != null)
            {
                return Apply(method.Implementation, arguments);
            }

            // Falling back is the normal case, not an error: specializing '-' on moments
            // must leave ordinary subtraction working for everything else.
            if (generic.Fallback != null)
            {
                return Apply(generic.Fallback, arguments);
            }

            throw new SchemeThrow(
                Symbol.Intern("goops-error"),
                Pair.List(
                    new MutableString(generic.Name ?? "generic"),
                    new MutableString("No applicable method for ~S"),
                    Pair.List(arguments.Length > 0 ? arguments[0] : (object)false),
                    false));
        }

        if (procedure is CaseLambdaProcedure caseLambda)
        {
            Closure selected = caseLambda.Select(arguments.Length);
            if (selected == null)
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-number-of-args"),
                    Pair.List(
                        new MutableString(caseLambda.Name ?? "case-lambda"),
                        new MutableString("No matching clause"),
                        Nil.Instance,
                        false));
            }

            return Apply(selected, arguments);
        }

        // An embedder's own applicable object — Guile's smob apply hook. Last, so nothing
        // built in is shadowed by it.
        if (procedure is IApplicable applicable)
        {
            return applicable.Apply(arguments);
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                false,
                new MutableString("Wrong type to apply: ~S"),
                Pair.List(procedure),
                false));
    }

    private LexicalEnvironment BindArguments(Closure closure, object[] arguments)
    {
        LambdaSignature signature = closure.Signature;
        LexicalEnvironment frame = new LexicalEnvironment(closure.Environment, signature.Required.Count + 2);

        int index = 0;
        foreach (Symbol required in signature.Required)
        {
            if (index >= arguments.Length)
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-number-of-args"),
                    Pair.List(
                        new MutableString(closure.Name ?? "procedure"),
                        new MutableString("Wrong number of arguments to ~A"),
                        Pair.List(new MutableString(closure.Name ?? "anonymous")),
                        false));
            }

            frame.Define(required, arguments[index++]);
        }

        foreach (OptionalParameter optional in signature.Optionals)
        {
            if (index < arguments.Length && !(arguments[index] is Keyword))
            {
                frame.Define(optional.ParameterName, arguments[index++]);
            }
            else
            {
                object fallback = optional.DefaultExpression == null
                    ? (object)false
                    : Eval(optional.DefaultExpression, frame, closure.Module);
                frame.Define(optional.ParameterName, fallback);
            }
        }

        if (signature.Keywords.Count > 0)
        {
            // Keyword arguments are scanned from the remaining positional tail, which is
            // how Guile's lambda* works: #:key consumes (keyword value) pairs in any order.
            Dictionary<Keyword, object> supplied = new Dictionary<Keyword, object>();
            int scan = index;
            while (scan < arguments.Length)
            {
                if (arguments[scan] is Keyword keyword && scan + 1 < arguments.Length)
                {
                    supplied[keyword] = arguments[scan + 1];
                    scan += 2;
                }
                else
                {
                    scan++;
                }
            }

            foreach (OptionalParameter keywordParameter in signature.Keywords)
            {
                Keyword selector = keywordParameter.SelectingKeyword
                                   ?? Keyword.Get(keywordParameter.ParameterName);
                if (supplied.TryGetValue(selector, out object value))
                {
                    frame.Define(keywordParameter.ParameterName, value);
                }
                else
                {
                    object fallback = keywordParameter.DefaultExpression == null
                        ? (object)false
                        : Eval(keywordParameter.DefaultExpression, frame, closure.Module);
                    frame.Define(keywordParameter.ParameterName, fallback);
                }
            }
        }

        if (signature.RestParameter != null)
        {
            object rest = Nil.Instance;
            for (int i = arguments.Length - 1; i >= index; i--)
            {
                rest = new Pair(arguments[i], rest);
            }

            frame.Define(signature.RestParameter, rest);
        }
        else if (index < arguments.Length && signature.Keywords.Count == 0 && signature.Optionals.Count == 0)
        {
            throw new SchemeThrow(
                Symbol.Intern("wrong-number-of-args"),
                Pair.List(
                    new MutableString(closure.Name ?? "procedure"),
                    new MutableString("Too many arguments to ~A"),
                    Pair.List(new MutableString(closure.Name ?? "anonymous")),
                    false));
        }

        return frame;
    }

    private object MakeClosure(
        object parameterList,
        object body,
        LexicalEnvironment environment,
        SchemeModule module,
        bool extended)
    {
        LambdaSignature signature = ParseSignature(parameterList, extended);
        return new Closure(signature, body, environment, module);
    }

    private object MakeCaseLambda(object clauses, LexicalEnvironment environment, SchemeModule module)
    {
        List<Closure> alternatives = new List<Closure>();
        foreach (object clause in Pair.ToList(clauses))
        {
            LambdaSignature signature = ParseSignature(First(clause), false);
            alternatives.Add(new Closure(signature, ((Pair)clause).Cdr, environment, module));
        }

        return new CaseLambdaProcedure(alternatives);
    }

    /// <summary>
    /// Parses a lambda parameter list. Handles the plain forms — a symbol, a proper list,
    /// or a dotted list — plus <c>lambda*</c>'s <c>#:optional</c>, <c>#:key</c>,
    /// <c>#:rest</c> and <c>#:allow-other-keys</c> markers.
    /// </summary>
    /// <param name="parameterList">The parameter list form.</param>
    /// <param name="extended">Whether <c>lambda*</c> markers are recognised.</param>
    /// <returns>The parsed signature.</returns>
    public static LambdaSignature ParseSignature(object parameterList, bool extended)
    {
        List<Symbol> required = new List<Symbol>();
        List<OptionalParameter> optionals = new List<OptionalParameter>();
        List<OptionalParameter> keywords = new List<OptionalParameter>();
        Symbol rest = null;
        bool allowOtherKeys = false;

        if (parameterList is Symbol single)
        {
            return new LambdaSignature(required, optionals, keywords, single, false);
        }

        int section = 0; // 0 required, 1 optional, 2 keyword, 3 rest
        object cursor = parameterList;
        while (cursor is Pair pair)
        {
            object item = pair.Car;

            if (extended && item is Keyword marker)
            {
                switch (marker.Name.Name)
                {
                    case "optional": section = 1; cursor = pair.Cdr; continue;
                    case "key": section = 2; cursor = pair.Cdr; continue;
                    case "rest": section = 3; cursor = pair.Cdr; continue;
                    case "allow-other-keys": allowOtherKeys = true; cursor = pair.Cdr; continue;
                    default: break;
                }
            }

            switch (section)
            {
                case 0:
                    required.Add((Symbol)item);
                    break;

                case 1:
                    optionals.Add(ParseOptional(item, false));
                    break;

                case 2:
                    keywords.Add(ParseOptional(item, true));
                    break;

                default:
                    rest = (Symbol)item;
                    break;
            }

            cursor = pair.Cdr;
        }

        // A dotted tail is the rest parameter.
        if (cursor is Symbol dotted)
        {
            rest = dotted;
        }

        return new LambdaSignature(required, optionals, keywords, rest, allowOtherKeys);
    }

    private static OptionalParameter ParseOptional(object item, bool isKeyword)
    {
        if (item is Symbol plain)
        {
            return new OptionalParameter(plain, null, isKeyword ? Keyword.Get(plain) : null);
        }

        // (name default) or, for keywords, (name default #:selector)
        List<object> parts = Pair.ToList(item);
        Symbol name = (Symbol)parts[0];
        object defaultExpression = parts.Count > 1 ? parts[1] : null;
        Keyword selector = null;
        if (isKeyword)
        {
            selector = parts.Count > 2 && parts[2] is Keyword explicitSelector
                ? explicitSelector
                : Keyword.Get(name);
        }

        return new OptionalParameter(name, defaultExpression, selector);
    }

    private object EvalDefine(Pair form, LexicalEnvironment environment, SchemeModule module)
    {
        object target = Second(form);

        // (define (name . args) body ...) is shorthand for a lambda binding.
        if (target is Pair signatureForm)
        {
            Symbol name = (Symbol)signatureForm.Car;
            object closure = MakeClosure(signatureForm.Cdr, Cddr(form), environment, module, false);
            ((Procedure)closure).Name = name.Name;
            DefineIn(name, closure, environment, module);
            return Symbol.Intern(name.Name);
        }

        Symbol variableName = (Symbol)target;
        object value = Cddr(form) is Pair
            ? Eval(Third(form), environment, module)
            : Unspecified.Instance;

        if (value is Procedure procedure && procedure.Name == null)
        {
            procedure.Name = variableName.Name;
        }

        DefineIn(variableName, value, environment, module);
        return Symbol.Intern(variableName.Name);
    }

    private static void DefineIn(Symbol name, object value, LexicalEnvironment environment, SchemeModule module)
    {
        if (environment != null)
        {
            environment.Define(name, value);
        }
        else
        {
            module.Define(name, value);
        }
    }

    private object Quasiquote(object template, LexicalEnvironment environment, SchemeModule module, int depth)
    {
        if (!(template is Pair pair))
        {
            return template;
        }

        if (pair.Car is Symbol head)
        {
            if (ReferenceEquals(head, Symbol.Unquote))
            {
                if (depth == 1)
                {
                    return Eval(Second(pair), environment, module);
                }

                return Pair.List(Symbol.Unquote, Quasiquote(Second(pair), environment, module, depth - 1));
            }

            if (ReferenceEquals(head, Symbol.Quasiquote))
            {
                return Pair.List(Symbol.Quasiquote, Quasiquote(Second(pair), environment, module, depth + 1));
            }
        }

        // Splicing has to be handled at the list level, since it contributes several
        // elements to the enclosing list rather than one.
        if (pair.Car is Pair inner
            && inner.Car is Symbol innerHead
            && ReferenceEquals(innerHead, Symbol.UnquoteSplicing)
            && depth == 1)
        {
            object spliced = Eval(Second(inner), environment, module);
            object tail = Quasiquote(pair.Cdr, environment, module, depth);
            List<object> items = Pair.ToList(spliced);
            object result = tail;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                result = new Pair(items[i], result);
            }

            return result;
        }

        return new Pair(
            Quasiquote(pair.Car, environment, module, depth),
            Quasiquote(pair.Cdr, environment, module, depth));
    }

    /// <summary>
    /// Scheme truth: only <c>#f</c> is false. The empty list, zero and the empty string
    /// are all true, which is the usual trap for anyone coming from other Lisps.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> unless the value is <c>#f</c>.</returns>
    public static bool IsTrue(object value)
    {
        if (value is bool b)
        {
            return b;
        }

        // #nil is false as well as null -- Guile's one concession to Elisp semantics.
        return !(value is ElispNil);
    }

    private static object First(object form) => ((Pair)form).Car;

    private static object Second(object form) => ((Pair)((Pair)form).Cdr).Car;

    private static object Third(object form) => ((Pair)((Pair)((Pair)form).Cdr).Cdr).Car;

    private static object Cddr(object form) => ((Pair)((Pair)form).Cdr).Cdr;

    private static object Cdddr(object form) => ((Pair)Cddr(form)).Cdr;
}

/// <summary>A <c>case-lambda</c> procedure: several closures selected by argument count.</summary>
public sealed class CaseLambdaProcedure : Procedure
{
    private readonly List<Closure> _alternatives;

    /// <summary>Initializes a case-lambda.</summary>
    /// <param name="alternatives">The clauses, in declaration order.</param>
    public CaseLambdaProcedure(List<Closure> alternatives)
    {
        _alternatives = alternatives ?? new List<Closure>();
    }

    /// <summary>Gets the clauses.</summary>
    public IReadOnlyList<Closure> Alternatives => _alternatives;

    /// <summary>Chooses the first clause that accepts the given argument count.</summary>
    /// <param name="argumentCount">The number of arguments at the call site.</param>
    /// <returns>The matching closure, or <see langword="null"/> when none matches.</returns>
    public Closure Select(int argumentCount)
    {
        foreach (Closure alternative in _alternatives)
        {
            LambdaSignature signature = alternative.Signature;
            int minimum = signature.Required.Count;
            if (argumentCount < minimum)
            {
                continue;
            }

            if (signature.RestParameter != null)
            {
                return alternative;
            }

            if (argumentCount <= minimum + signature.Optionals.Count)
            {
                return alternative;
            }
        }

        return null;
    }
}

/// <summary>A delayed computation created by <c>delay</c> and forced by <c>force</c>.</summary>
public sealed class Promise
{
    /// <summary>Initializes a promise.</summary>
    /// <param name="thunk">The closure producing the value.</param>
    public Promise(Closure thunk)
    {
        Thunk = thunk;
    }

    /// <summary>Gets the closure producing the value.</summary>
    public Closure Thunk { get; }

    /// <summary>Gets or sets a value indicating whether the promise has been forced.</summary>
    public bool IsForced { get; set; }

    /// <summary>Gets or sets the cached value once forced.</summary>
    public object Value { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The string <c>#&lt;promise&gt;</c>.</returns>
    public override string ToString() => "#<promise>";
}
