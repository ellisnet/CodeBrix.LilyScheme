// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// Modules, variables, structs and syntax objects — the substrate psyntax is built on.
/// Everything here exists because <c>psyntax-pp.scm</c> reaches for it during load.
/// </summary>
public static class ModulePrimitives
{
    /// <summary>Installs the module, struct and syntax primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallModules(interpreter);
        InstallVariables(interpreter);
        InstallStructs(interpreter);
        InstallSyntax(interpreter);
        InstallObjectProperties(interpreter);
        InstallPreludeSupport(interpreter);
    }

    /// <summary>
    /// Primitives the LilyScheme prelude is written against. These are the pieces of
    /// Guile's boot-9 that cannot be expressed as derived syntax and so are provided
    /// from C# instead of vendored.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPreludeSupport(Interpreter interpreter)
    {
        interpreter.DefineValue("*unspecified*", Unspecified.Instance);

        interpreter.DefinePrimitive("make-promise-thunk", 1, 1, a => new LazyPromise(a[0]));
        interpreter.DefinePrimitive("promise?", 1, 1, a => a[0] is LazyPromise || a[0] is Promise);

        // (use-one-module '(ice-9 match)) -- import a module into the current one.
        interpreter.DefinePrimitive("use-one-module", 1, 1, a =>
        {
            object spec = a[0];
            object options = Nil.Instance;

            // A use-modules clause may be (mod) or (mod #:select ...); take the head.
            if (spec is Pair pair && pair.Car is Pair inner)
            {
                spec = inner;
                options = pair.Cdr;
            }

            interpreter.CurrentModule.AddUse(ResolveInterface(interpreter, spec, options));
            return Unspecified.Instance;
        });

        // (define-module* '(lily) '(clauses ...)) -- create the module, make it current,
        // and honour any #:use-module clauses it carries. A clause keyword may be
        // spelled #:export or as the keyword-like SYMBOL :export -- boot-9's
        // define-module normalizes the latter with keyword-like-symbol->keyword, and
        // the vendored srfi-1.scm uses exactly that spelling. Skipping it silently
        // left srfi-1's whole export list unrecorded, which the wide import hid.
        interpreter.DefinePrimitive("define-module*", 1, 2, a =>
        {
            SchemeModule module = interpreter.Modules.Resolve(a[0]);
            if (a.Length > 1)
            {
                List<object> clauses = Pair.ToList(a[1]);
                for (int i = 0; i < clauses.Count; i++)
                {
                    clauses[i] = NormalizeClauseKeyword(clauses[i]);
                    if (clauses[i] is Keyword keyword
                        && string.Equals(keyword.Name.Name, "use-module", StringComparison.Ordinal)
                        && i + 1 < clauses.Count)
                    {
                        // The clause is either a bare name list, (ice-9 control), or a
                        // spec carrying options, ((ice-9 control) #:select (let/ec)) --
                        // the same two shapes use-one-module takes, and the second one
                        // resolved as a module NAME before this was split out, which
                        // named a module after the whole clause and imported nothing.
                        object clause = clauses[i + 1];
                        object useSpec = clause;
                        object useOptions = Nil.Instance;
                        if (clause is Pair clausePair && clausePair.Car is Pair clauseName)
                        {
                            useSpec = clauseName;
                            useOptions = clausePair.Cdr;
                        }

                        module.AddUse(ResolveInterface(interpreter, useSpec, useOptions));
                    }
                    else if (clauses[i] is Keyword exportKeyword
                             && i + 1 < clauses.Count
                             && IsExportClause(exportKeyword))
                    {
                        // #:export, #:re-export, #:export-syntax and #:replace all put
                        // their names in the public interface; only the shadow-warning
                        // and syntax-vs-value distinctions differ, and neither is
                        // modelled. A name may be given bare or as a (internal . external)
                        // rename pair, in which case the EXTERNAL name is the export.
                        foreach (object name in Pair.ToList(clauses[i + 1]))
                        {
                            module.Export(name is Pair rename ? rename.Cdr as Symbol : name as Symbol);
                        }
                    }
                }
            }

            interpreter.CurrentModule = module;
            return module;
        });

        // Exports are RECORDED, because the public interface is derived from them: a
        // plain `define' stays out of it while a `define-public' goes in. What is still
        // not modelled is the IMPORT side -- a `use-modules' without #:select puts the
        // whole module on the use list, so visible scope stays wider than Guile's and
        // never narrower (the divergence the AGENT-README records).
        interpreter.DefinePrimitive("export-one", 1, 1, a =>
        {
            interpreter.CurrentModule.Export(a[0] as Symbol);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-export!", 2, 2, a =>
        {
            ExportNames(AsModule(a[0], interpreter), a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-re-export!", 2, 3, a =>
        {
            ExportNames(AsModule(a[0], interpreter), a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-export-all!", 1, 1, a =>
        {
            SchemeModule module = AsModule(a[0], interpreter);
            foreach (Symbol name in new List<Symbol>(module.Bindings.Keys))
            {
                module.Export(name);
            }

            return Unspecified.Instance;
        });

        // module-replace! exports the name AND marks it as intentionally shadowing an
        // imported binding. The shadow warning is Guile's own diagnostic and has no
        // analogue here, so only the export half is recorded.
        interpreter.DefinePrimitive("module-replace!", 2, 2, a =>
        {
            ExportNames(AsModule(a[0], interpreter), a[1]);
            return Unspecified.Instance;
        });

        // Guile's SRFI modules announce themselves with cond-expand-provide; we accept
        // and ignore it, since LilyScheme has a single fixed feature set.
        interpreter.DefinePrimitive("cond-expand-provide", 2, 2, a => Unspecified.Instance);

        // The Guile version LilyScheme presents itself as. LilyPond and the SRFI
        // modules use this to locate version-specific paths.
        interpreter.DefinePrimitive("effective-version", 0, 0, a => new MutableString("3.0"));
        interpreter.DefinePrimitive("version", 0, 0, a => new MutableString("3.0.11"));
        interpreter.DefinePrimitive("major-version", 0, 0, a => new MutableString("3"));
        interpreter.DefinePrimitive("minor-version", 0, 0, a => new MutableString("0"));
        interpreter.DefineValue("the-root-module", interpreter.GuileModule);
        interpreter.DefineValue("the-scm-module", interpreter.GuileModule);

        // The public interface holds the EXPORTED names only. LilyPond's manual is
        // generated by walking this, so a module returning itself here documents every
        // private helper the file happens to define.
        interpreter.DefinePrimitive("module-public-interface", 1, 1, a => AsModule(a[0], interpreter).Interface());
        interpreter.DefinePrimitive("set-module-public-interface!", 2, 2, a =>
        {
            AsModule(a[0], interpreter).PublicInterface = a[1] as SchemeModule;
            return Unspecified.Instance;
        });
        interpreter.DefinePrimitive("module-defined?", 2, 2, a =>
        {
            Variable variable = AsModule(a[0], interpreter).Lookup((Symbol)a[1]);
            return variable != null && variable.IsBound;
        });
        interpreter.DefinePrimitive("provided?", 1, 1, a => false);

        // load-extension pulls in a C shared library. Every extension Guile would load
        // this way is already implemented in C# here, so loading one is a no-op rather
        // than an error -- the bindings the module expects afterwards already exist.
        interpreter.DefinePrimitive("load-extension", 1, 2, a => Unspecified.Instance);

        interpreter.DefinePrimitive("module-set!", 3, 3, a =>
        {
            SchemeModule module = AsModule(a[0], interpreter);
            Symbol name = (Symbol)a[1];
            Variable variable = module.Lookup(name) ?? module.EnsureVariable(name);
            variable.SetValue(a[2]);
            return Unspecified.Instance;
        });

        // Hooks: an ordered list of procedures run for effect.
        interpreter.DefinePrimitive("make-hook", 0, 1, a => new SchemeHook());
        interpreter.DefinePrimitive("hook?", 1, 1, a => a[0] is SchemeHook);
        interpreter.DefinePrimitive("add-hook!", 2, 3, a =>
        {
            if (a[0] is SchemeHook hook)
            {
                bool append = a.Length > 2 && Evaluator.IsTrue(a[2]);
                if (append)
                {
                    hook.Procedures.Add(a[1]);
                }
                else
                {
                    hook.Procedures.Insert(0, a[1]);
                }
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("remove-hook!", 2, 2, a =>
        {
            (a[0] as SchemeHook)?.Procedures.Remove(a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("reset-hook!", 1, 1, a =>
        {
            (a[0] as SchemeHook)?.Procedures.Clear();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("run-hook", 1, -1, a =>
        {
            if (a[0] is SchemeHook hook)
            {
                object[] rest = new object[a.Length - 1];
                Array.Copy(a, 1, rest, 0, rest.Length);
                foreach (object procedure in new List<object>(hook.Procedures))
                {
                    interpreter.Evaluator.Apply(procedure, rest);
                }
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("hook->list", 1, 1, a =>
            a[0] is SchemeHook hook ? Pair.ListFrom(hook.Procedures) : (object)Nil.Instance);

        // include-from-path resolves against the vendored Scheme resources, which is
        // where the only files that use it live.
        interpreter.DefinePrimitive("load-vendored", 1, 1, a =>
        {
            string name = StringPrimitives.Text(a[0], "load-vendored");
            int slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            // include-from-path names a file without its extension, exactly as
            // boot-9.scm does with "ice-9/quasisyntax".
            if (!name.EndsWith(".scm", StringComparison.Ordinal))
            {
                name += ".scm";
            }

            string source = SchemeBootstrap.ReadVendoredSource(name);
            SchemeBootstrap.LoadExpanded(interpreter, source, name);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("with-parameters*", 3, 3, a =>
        {
            List<object> parameters = Pair.ToList(a[0]);
            List<object> values = Pair.ToList(a[1]);
            object[] saved = new object[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                saved[i] = interpreter.Evaluator.Apply(parameters[i], Array.Empty<object>());
                interpreter.Evaluator.Apply(parameters[i], new[] { i < values.Count ? values[i] : false });
            }

            try
            {
                return interpreter.Evaluator.Apply(a[2], Array.Empty<object>());
            }
            finally
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    interpreter.Evaluator.Apply(parameters[i], new[] { saved[i] });
                }
            }
        });

        interpreter.DefinePrimitive("make-parameter", 1, 2, a =>
        {
            object[] cell = { a[0] };
            object converter = a.Length > 1 ? a[1] : null;
            if (converter != null)
            {
                cell[0] = interpreter.Evaluator.Apply(converter, new[] { cell[0] });
            }

            return new Primitive("parameter", 0, 1, args =>
            {
                if (args.Length == 0)
                {
                    return cell[0];
                }

                object incoming = args[0];
                if (converter != null)
                {
                    incoming = interpreter.Evaluator.Apply(converter, new[] { incoming });
                }

                cell[0] = incoming;
                return Unspecified.Instance;
            });
        });
    }

    private static void InstallModules(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("current-module", 0, 0, a => interpreter.CurrentModule);

        interpreter.DefinePrimitive("set-current-module", 1, 1, a =>
        {
            SchemeModule previous = interpreter.CurrentModule;
            if (a[0] is SchemeModule module)
            {
                interpreter.CurrentModule = module;
            }

            return previous;
        });

        interpreter.DefinePrimitive("resolve-module", 1, -1, a => interpreter.Modules.Resolve(a[0]));

        // An interface is the module's exported face. We do not model separate
        // interfaces yet, so a module stands for its own interface.
        interpreter.DefinePrimitive("resolve-interface", 1, -1, a => interpreter.Modules.Resolve(a[0]));

        interpreter.DefinePrimitive("module?", 1, 1, a => a[0] is SchemeModule);

        // Guile's module-name NAMES an anonymous module on first ask and registers
        // it — psyntax reads the name here and later resolves it back, so an
        // unnameable module cannot host imported macros. See SchemeModule.EnsureName.
        interpreter.DefinePrimitive("module-name", 1, 1, a =>
            ((SchemeModule)a[0]).EnsureName(interpreter.Modules));

        // Guile's module record carries a `kind' field — 'module for an ordinary one,
        // 'interface for the export view a `use-modules' creates, 'directory for a
        // namespace node. boot-9 sets it in three places and reads it back when printing
        // a module; LilyPond's session-save reads it to pick the INTERFACES out of
        // (module-uses (current-module)), which is how a session knows which imports to
        // reinstate. Without the accessor that line raises, and the raise happens at the
        // very end of declarations-init.ly where it is easy to mistake for a shutdown
        // problem rather than a missing binding.
        interpreter.DefinePrimitive("module-kind", 1, 1, a => ((SchemeModule)a[0]).Kind);

        interpreter.DefinePrimitive("set-module-kind!", 2, 2, a =>
        {
            ((SchemeModule)a[0]).Kind = a[1];
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-remove!", 2, 2, a =>
        {
            ((SchemeModule)a[0]).Remove((Symbol)a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-submodules", 1, 1, a => ((SchemeModule)a[0]).Submodules);

        interpreter.DefinePrimitive("set-module-submodules!", 2, 2, a =>
        {
            if (a[1] is SchemeHashTable table)
            {
                ((SchemeModule)a[0]).Submodules = table;
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-ref", 2, 3, a =>
        {
            SchemeModule module = AsModule(a[0], interpreter);
            Variable variable = module.Lookup((Symbol)a[1]);
            if (variable != null && variable.IsBound)
            {
                return variable.GetValue();
            }

            if (a.Length > 2)
            {
                return a[2];
            }

            throw new SchemeThrow(
                Symbol.Intern("unbound-variable"),
                Pair.List(false, new MutableString("Unbound variable: ~S"), Pair.List(a[1]), false));
        });

        interpreter.DefinePrimitive("module-define!", 3, 3, a =>
        {
            AsModule(a[0], interpreter).Define((Symbol)a[1], a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-variable", 2, 2, a =>
        {
            Variable variable = AsModule(a[0], interpreter).Lookup((Symbol)a[1]);
            return variable ?? (object)false;
        });

        // (defined? sym [module]) — is the symbol bound? Guile resolves against the
        // current module when none is given, and a variable that exists but carries
        // no value answers #f. LilyPond's ly/init.ly leans on this to find the
        // default-toplevel-book-handler escape hatch.
        interpreter.DefinePrimitive("defined?", 1, 2, a =>
        {
            Symbol symbol = (Symbol)a[0];
            SchemeModule module = a.Length > 1 && a[1] is SchemeModule given
                ? given
                : interpreter.CurrentModule;
            Variable variable = module.Lookup(symbol);
            return variable != null && variable.IsBound;
        });

        interpreter.DefinePrimitive("module-local-variable", 2, 2, a =>
        {
            Variable variable = AsModule(a[0], interpreter).LookupLocal((Symbol)a[1]);
            return variable ?? (object)false;
        });

        interpreter.DefinePrimitive("module-ensure-local-variable!", 2, 2, a =>
            AsModule(a[0], interpreter).EnsureVariable((Symbol)a[1]));

        // Guile's older spelling of the same operation; LilyPond's session machinery
        // uses it to create a variable it can restore between sessions.
        interpreter.DefinePrimitive("module-make-local-var!", 2, 2, a =>
            AsModule(a[0], interpreter).EnsureVariable((Symbol)a[1]));

        // module-add! takes a VARIABLE, not a value: boot-9's body is
        //
        //     (if (not (variable? var)) (error "Bad variable to module-add!" var))
        //     (module-obarray-set! (module-obarray m) v var)
        //
        // so the caller's own cell becomes the module's binding and the two names share
        // one location. Wrapping the argument in a fresh variable instead makes
        // module-add! a silent alias for module-define!, and every reader of the name
        // then gets the VARIABLE OBJECT back as the value — which is how
        // `#all-grob-descriptions' reached LilyPond's \grobdescriptions as
        // `#<variable bound>' rather than as the alist.
        interpreter.DefinePrimitive("module-add!", 3, 3, a =>
        {
            if (!(a[2] is Variable variable))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("module-add!"),
                        new MutableString("Bad variable to module-add!: ~S"),
                        Pair.List(a[2]),
                        false));
            }

            AsModule(a[0], interpreter).AddVariable((Symbol)a[1], variable);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-for-each", 2, 2, a =>
        {
            SchemeModule module = AsModule(a[1], interpreter);
            foreach (KeyValuePair<Symbol, Variable> entry in new List<KeyValuePair<Symbol, Variable>>(module.Bindings))
            {
                interpreter.Evaluator.Apply(a[0], new object[] { entry.Key, entry.Value });
            }

            return Unspecified.Instance;
        });

        // (module-map proc module) -- boot-9's definition is (hash-map->list proc
        // (module-obarray module)): proc over every symbol/variable binding, the
        // results collected into a list. ly/context-mods-init.ly walks output-def
        // scopes with it to gather \accepts entries.
        interpreter.DefinePrimitive("module-map", 2, 2, a =>
        {
            SchemeModule module = AsModule(a[1], interpreter);
            List<object> results = new List<object>();
            foreach (KeyValuePair<Symbol, Variable> entry in new List<KeyValuePair<Symbol, Variable>>(module.Bindings))
            {
                results.Add(interpreter.Evaluator.Apply(a[0], new object[] { entry.Key, entry.Value }));
            }

            return Pair.ListFrom(results);
        });

        interpreter.DefinePrimitive("module-use!", 2, 2, a =>
        {
            AsModule(a[0], interpreter).AddUse(AsModule(a[1], interpreter));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("module-uses", 1, 1, a =>
        {
            List<object> uses = new List<object>();
            foreach (SchemeModule used in AsModule(a[0], interpreter).Uses)
            {
                uses.Add(used);
            }

            return Pair.ListFrom(uses);
        });

        interpreter.DefinePrimitive("module-gensym", 0, 2, a =>
            Symbol.Generate(a.Length > 0 && a[0] is MutableString prefix ? prefix.ToString() : " m"));

        interpreter.DefinePrimitive("module-generate-unique-id!", 1, 1, a =>
            AsModule(a[0], interpreter).GenerateUniqueId());

        // (define! name value) installs into the current module regardless of the
        // lexical environment. psyntax uses it to publish its own bindings.
        interpreter.DefinePrimitive("define!", 2, 2, a =>
        {
            Symbol name = a[0] as Symbol ?? Symbol.Intern(StringPrimitives.Text(a[0], "define!"));
            interpreter.CurrentModule.Define(name, a[1]);
            if (a[1] is Procedure procedure && procedure.Name == null)
            {
                procedure.Name = name.Name;
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("interaction-environment", 0, 0, a => interpreter.CurrentModule);

        interpreter.DefinePrimitive("nested-ref", 2, 2, a =>
        {
            SchemeModule module = AsModule(a[0], interpreter);
            foreach (object part in Pair.ToList(a[1]))
            {
                Variable variable = module.Lookup((Symbol)part);
                if (variable == null || !variable.IsBound)
                {
                    return false;
                }

                return variable.GetValue();
            }

            return false;
        });
    }

    private static void InstallVariables(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("make-variable", 1, 1, a => new Variable(a[0]));
        interpreter.DefinePrimitive("make-undefined-variable", 0, 0, a => new Variable());
        interpreter.DefinePrimitive("variable?", 1, 1, a => a[0] is Variable);
        interpreter.DefinePrimitive("variable-bound?", 1, 1, a => ((Variable)a[0]).IsBound);
        interpreter.DefinePrimitive("variable-ref", 1, 1, a => ((Variable)a[0]).GetValue());
        interpreter.DefinePrimitive("variable-set!", 2, 2, a =>
        {
            ((Variable)a[0]).SetValue(a[1]);
            return Unspecified.Instance;
        });
    }

    private static void InstallStructs(Interpreter interpreter)
    {
        // %expanded-vtables is how psyntax reaches the Tree-IL node constructors. The
        // eighteen vtables and their field order mirror libguile/expand.h exactly.
        interpreter.DefineValue("%expanded-vtables", ExpandedVtables.BuildSchemeVector());

        interpreter.DefinePrimitive("make-struct/simple", 1, -1, a =>
        {
            StructVtable vtable = a[0] as StructVtable
                                  ?? throw new SchemeThrow(
                                      Symbol.Intern("wrong-type-arg"),
                                      Pair.List(
                                          new MutableString("make-struct/simple"),
                                          new MutableString("Not a vtable: ~S"),
                                          Pair.List(a[0]),
                                          false));

            object[] fields = new object[a.Length - 1];
            Array.Copy(a, 1, fields, 0, fields.Length);
            return new SchemeStruct(vtable, fields);
        });

        interpreter.DefinePrimitive("make-struct", 2, -1, a =>
        {
            StructVtable vtable = (StructVtable)a[0];
            object[] fields = new object[Math.Max(0, a.Length - 2)];
            Array.Copy(a, 2, fields, 0, fields.Length);
            return new SchemeStruct(vtable, fields);
        });

        // A record IS a struct in Guile (records are built on structs), so the struct
        // family accepts record instances too: struct-vtable answers the RecordType and
        // struct-ref counts fields from 0, skipping the type slot — the indexing the
        // vendored ice-9/exceptions.scm's format-simple-exception relies on.
        interpreter.DefinePrimitive("struct?", 1, 1, a =>
            a[0] is SchemeStruct
            || (a[0] is object[] record && record.Length > 0 && record[0] is RecordType));

        interpreter.DefinePrimitive("struct-vtable", 1, 1, a =>
            a[0] is object[] record && record.Length > 0 && record[0] is RecordType type
                ? (object)type
                : ((SchemeStruct)a[0]).Vtable);

        interpreter.DefinePrimitive("struct-vtable?", 1, 1, a => a[0] is StructVtable);

        interpreter.DefinePrimitive("struct-ref", 2, 2, a =>
        {
            int index = (int)SchemeNumber.ToBigInteger(a[1]);
            if (a[0] is object[] record && record.Length > 0 && record[0] is RecordType)
            {
                return index >= 0 && index + 1 < record.Length ? record[index + 1] : false;
            }

            SchemeStruct instance = (SchemeStruct)a[0];
            return index >= 0 && index < instance.Fields.Length ? instance.Fields[index] : false;
        });

        interpreter.DefinePrimitive("struct-set!", 3, 3, a =>
        {
            int index = (int)SchemeNumber.ToBigInteger(a[1]);
            if (a[0] is object[] record && record.Length > 0 && record[0] is RecordType)
            {
                if (index >= 0 && index + 1 < record.Length)
                {
                    record[index + 1] = a[2];
                }

                return a[2];
            }

            SchemeStruct instance = (SchemeStruct)a[0];
            if (index >= 0 && index < instance.Fields.Length)
            {
                instance.Fields[index] = a[2];
            }

            return a[2];
        });

        interpreter.DefinePrimitive("struct-vtable-name", 1, 1, a =>
            Symbol.Intern(((StructVtable)a[0]).Name));
    }

    private static void InstallSyntax(Interpreter interpreter)
    {
        // (make-syntax exp wrap module [source]) -- Guile's arity is 3 required, 1 optional.
        interpreter.DefinePrimitive("make-syntax", 3, 4, a =>
            new SyntaxObject(a[0], a[1], a[2], a.Length > 3 ? a[3] : false));

        interpreter.DefinePrimitive("syntax?", 1, 1, a => a[0] is SyntaxObject);
        interpreter.DefinePrimitive("syntax-expression", 1, 1, a => ((SyntaxObject)a[0]).Expression);
        interpreter.DefinePrimitive("syntax-wrap", 1, 1, a => ((SyntaxObject)a[0]).Wrap);
        interpreter.DefinePrimitive("syntax-module", 1, 1, a => ((SyntaxObject)a[0]).Module);
        interpreter.DefinePrimitive("syntax-sourcev", 1, 1, a => ((SyntaxObject)a[0]).SourceVector);
        interpreter.DefinePrimitive("syntax-source", 1, 1, a => ((SyntaxObject)a[0]).SourceVector);

        // A psyntax wrap is a (marks . substs) pair, so the empty wrap is (() . ()).
        // libguile/syntax.c builds it as scm_cons (SCM_EOL, SCM_EOL) and shares one cell
        // globally; getting this shape wrong silently breaks every identifier lookup,
        // because psyntax then walks a substitution list that is not there and falls
        // back to treating every lexical reference as a top-level one.
        interpreter.DefineValue("syntax-empty-wrap", new Pair(Nil.Instance, Nil.Instance));

        interpreter.DefinePrimitive("make-syntax-transformer", 3, 3, a =>
            new SyntaxTransformer(a[0], a[1], a[2]));

        interpreter.DefinePrimitive("macro?", 1, 1, a => a[0] is SyntaxTransformer);
        interpreter.DefinePrimitive("macro-type", 1, 1, a => ((SyntaxTransformer)a[0]).TransformerType);
        interpreter.DefinePrimitive("macro-name", 1, 1, a => ((SyntaxTransformer)a[0]).Name);
        interpreter.DefinePrimitive("macro-binding", 1, 1, a => ((SyntaxTransformer)a[0]).Binding);
        interpreter.DefinePrimitive("macro-transformer", 1, 1, a => ((SyntaxTransformer)a[0]).Binding);
    }

    private static void InstallObjectProperties(Interpreter interpreter)
    {
        Primitive objectProperty = interpreter.DefinePrimitive("object-property", 2, 2, a =>
        {
            if (!(a[1] is Symbol key) || !interpreter.ObjectProperties.TryGetValue(key, out object table))
            {
                return false;
            }

            foreach (object entry in Pair.ToList(table))
            {
                if (entry is Pair pair && CorePrimitives.Eq(pair.Car, a[0]))
                {
                    return pair.Cdr;
                }
            }

            return false;
        });

        Primitive setObjectProperty = interpreter.DefinePrimitive("set-object-property!", 3, 3, a =>
        {
            if (a[1] is Symbol key)
            {
                interpreter.ObjectProperties.TryGetValue(key, out object table);
                interpreter.ObjectProperties[key] = new Pair(new Pair(a[0], a[2]), table ?? Nil.Instance);
            }

            return a[2];
        });

        objectProperty.Setter = setObjectProperty;

        // (make-object-property) returns a procedure usable as both getter and
        // setter over a private weak table -- Guile's per-object property idiom.
        interpreter.DefinePrimitive("make-object-property", 0, 1, a =>
        {
            Dictionary<object, object> table = new Dictionary<object, object>(ReferenceComparer.Instance);
            Primitive accessor = new Primitive("object-property", 1, 2, args =>
            {
                if (args.Length == 1)
                {
                    return table.TryGetValue(args[0], out object found) ? found : false;
                }

                table[args[0]] = args[1];
                return Unspecified.Instance;
            });

            // The idiom is (set! (prop object) value), which expands to
            // ((setter prop) object value) -- so the accessor has to carry a setter,
            // not merely accept a second argument.
            accessor.Setter = new Primitive("set-object-property!", 2, 2, args =>
            {
                table[args[0]] = args[1];
                return Unspecified.Instance;
            });

            return accessor;
        });

        // The reader is fixed, so a custom '#' dispatch cannot be installed. LilyPond
        // registers one for its own syntax but only uses it from the .ly parser, which
        // this port replaces outright.
        interpreter.DefinePrimitive("read-hash-extend", 2, 2, a => Unspecified.Instance);

        // Source properties are what psyntax expands FROM: datum-sourcev
        // (ice-9/psyntax.scm:307-312) reads this alist and nothing else, and turns it into
        // the #(filename line column) vector that reaches every Tree-IL node's src field.
        // While these answered '() / #f the expander had nothing to propagate, so every
        // procedure printed as anonymous and no error carried a location.
        interpreter.DefinePrimitive("source-properties", 1, 1, a => SourceProperties.Get(a[0]));
        interpreter.DefinePrimitive("set-source-properties!", 2, 2, a =>
        {
            SourceProperties.Set(a[0], a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("supports-source-properties?", 1, 1, a => SourceProperties.Supports(a[0]));
        interpreter.DefinePrimitive("source-property", 2, 2, a => SourceProperties.Property(a[0], a[1]));
        interpreter.DefinePrimitive("set-source-property!", 3, 3, a =>
        {
            SourceProperties.SetProperty(a[0], a[1], a[2]);
            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Normalizes a define-module clause keyword: a <c>#:</c> keyword passes through,
    /// and a keyword-like SYMBOL such as <c>:export</c> becomes the keyword it spells —
    /// boot-9's <c>keyword-like-symbol-&gt;keyword</c>. Anything else is returned as it
    /// stands.
    /// </summary>
    /// <param name="clause">The clause element to normalize.</param>
    /// <returns>The normalized element.</returns>
    private static object NormalizeClauseKeyword(object clause)
    {
        if (clause is Symbol symbol && symbol.Name.Length > 1 && symbol.Name[0] == ':')
        {
            return Keyword.Get(symbol.Name.Substring(1));
        }

        return clause;
    }

    private static SchemeModule ResolveInterface(Interpreter interpreter, object spec, object options)
    {
        SchemeModule used = interpreter.Modules.Resolve(spec);

        object selection = null;
        List<object> parts = Pair.ToList(options);
        for (int i = 0; i + 1 < parts.Count; i++)
        {
            if (parts[i] is Keyword keyword
                && string.Equals(keyword.Name.Name, "select", StringComparison.Ordinal))
            {
                selection = parts[i + 1];
                break;
            }
        }

        if (selection == null)
        {
            // The recorded import-side divergence, and its OPT-IN closure: wide (the
            // whole module) by default, the used module's live public-interface view
            // when the embedder asks for Guile's documented narrowing.
            return interpreter.NarrowModuleImports ? used.LiveInterfaceView() : used;
        }

        SchemeModule interfaceModule = new SchemeModule(Nil.Instance)
        {
            Kind = Symbol.Intern("interface"),
        };

        foreach (object selected in Pair.ToList(selection))
        {
            Symbol original;
            Symbol local;

            if (selected is Pair rename && rename.Car is Symbol from && rename.Cdr is Symbol to)
            {
                original = from;
                local = to;
            }
            else if (selected is Symbol bare)
            {
                original = bare;
                local = bare;
            }
            else
            {
                continue;
            }

            Variable variable = used.Lookup(original);
            if (variable != null)
            {
                interfaceModule.AddVariable(local, variable);
            }
        }

        return interfaceModule;
    }

    /// <summary>
    /// Determines whether a <c>define-module</c> keyword clause names exports.
    /// <para>
    /// All four spellings put their names in the public interface. <c>#:replace</c> also
    /// tells Guile the shadowing is deliberate so it can suppress a warning, and
    /// <c>#:export-syntax</c> only distinguishes syntax from values for the compiler;
    /// neither distinction exists here.
    /// </para>
    /// </summary>
    private static bool IsExportClause(Keyword keyword)
    {
        string name = keyword.Name.Name;
        return string.Equals(name, "export", StringComparison.Ordinal)
               || string.Equals(name, "re-export", StringComparison.Ordinal)
               || string.Equals(name, "export-syntax", StringComparison.Ordinal)
               || string.Equals(name, "replace", StringComparison.Ordinal);
    }

    /// <summary>Records a list of names — bare or <c>(internal . external)</c> — as exports.</summary>
    private static void ExportNames(SchemeModule module, object names)
    {
        foreach (object name in Pair.ToList(names))
        {
            module.Export(name is Pair rename ? rename.Cdr as Symbol : name as Symbol);
        }
    }

    private static SchemeModule AsModule(object value, Interpreter interpreter)
    {
        if (value is SchemeModule module)
        {
            return module;
        }

        // Guile accepts a module name list wherever a module is expected.
        if (value is Pair || value is Nil)
        {
            return interpreter.Modules.Resolve(value);
        }

        if (value is bool flag && !flag)
        {
            return interpreter.CurrentModule;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString("module operation"),
                new MutableString("Not a module: ~S"),
                Pair.List(value),
                false));
    }
}
