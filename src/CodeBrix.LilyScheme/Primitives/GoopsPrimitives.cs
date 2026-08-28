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

/// <summary>A slot in a GOOPS class: a name plus the options declared for it.</summary>
public sealed class SlotDefinition
{
    /// <summary>Initializes a slot definition.</summary>
    /// <param name="name">The slot name.</param>
    public SlotDefinition(Symbol name)
    {
        SlotName = name;
    }

    /// <summary>Gets the slot name.</summary>
    public Symbol SlotName { get; }

    /// <summary>Gets or sets the value used when no initializer is supplied.</summary>
    public object InitialValue { get; set; } = false;

    /// <summary>Gets or sets the keyword that initializes this slot in <c>make</c>.</summary>
    public Keyword InitKeyword { get; set; }

    /// <summary>Gets or sets a thunk called to produce the initial value, if declared.</summary>
    public object InitThunk { get; set; }
}

/// <summary>A GOOPS class: a name, an ordered slot list, and its direct superclasses.</summary>
public sealed class SchemeClass
{
    /// <summary>Initializes a class.</summary>
    /// <param name="name">The class name, conventionally written <c>&lt;name&gt;</c>.</param>
    /// <param name="superclasses">The direct superclasses.</param>
    public SchemeClass(Symbol name, IReadOnlyList<SchemeClass> superclasses)
    {
        ClassName = name;
        Superclasses = superclasses ?? Array.Empty<SchemeClass>();
    }

    /// <summary>Gets the class name.</summary>
    public Symbol ClassName { get; }

    /// <summary>Gets the direct superclasses.</summary>
    public IReadOnlyList<SchemeClass> Superclasses { get; }

    /// <summary>
    /// Gets how far this class sits below the root of its hierarchy. Method dispatch uses
    /// it to score specificity, so a method on <c>&lt;integer&gt;</c> outranks one on
    /// <c>&lt;number&gt;</c> for the same argument.
    /// </summary>
    public int Depth
    {
        get
        {
            int deepest = 0;
            foreach (SchemeClass super in Superclasses)
            {
                int candidate = super.Depth + 1;
                if (candidate > deepest)
                {
                    deepest = candidate;
                }
            }

            return deepest;
        }
    }

    /// <summary>Gets the slots this class declares, in declaration order.</summary>
    public List<SlotDefinition> Slots { get; } = new List<SlotDefinition>();

    /// <summary>Gets every slot, including those inherited from superclasses.</summary>
    /// <returns>The full slot list, superclass slots first.</returns>
    public List<SlotDefinition> AllSlots()
    {
        List<SlotDefinition> all = new List<SlotDefinition>();
        foreach (SchemeClass super in Superclasses)
        {
            all.AddRange(super.AllSlots());
        }

        all.AddRange(Slots);
        return all;
    }

    /// <summary>Determines whether this class is, or inherits from, another.</summary>
    /// <param name="other">The class to test against.</param>
    /// <returns><see langword="true"/> when this class is a subtype.</returns>
    public bool IsSubclassOf(SchemeClass other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        foreach (SchemeClass super in Superclasses)
        {
            if (super.IsSubclassOf(other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the class name.</returns>
    public override string ToString() => "#<class " + ClassName.Name + ">";
}

/// <summary>An instance of a <see cref="SchemeClass"/>.</summary>
public sealed class SchemeObject
{
    /// <summary>Initializes an instance.</summary>
    /// <param name="objectClass">The class being instantiated.</param>
    public SchemeObject(SchemeClass objectClass)
    {
        ObjectClass = objectClass;
    }

    /// <summary>Gets the instance's class.</summary>
    public SchemeClass ObjectClass { get; }

    /// <summary>Gets the slot storage, keyed by slot name.</summary>
    public Dictionary<Symbol, object> Slots { get; } = new Dictionary<Symbol, object>();

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description including the class name.</returns>
    public override string ToString() => "#<" + ObjectClass.ClassName.Name + ">";
}

/// <summary>
/// A generic function: a set of methods selected by the class of the first argument.
/// </summary>
public sealed class GenericFunction : Procedure
{
    /// <summary>Gets the methods, most recently added first within each specificity.</summary>
    public List<GenericMethod> Methods { get; } = new List<GenericMethod>();

    /// <summary>
    /// Gets or sets the procedure a generic falls back to when no method applies.
    /// <para>
    /// This is what makes <c>(define-method (+ (a &lt;Moment&gt;) (b &lt;Moment&gt;)) ...)</c>
    /// safe. Adding a method to a name that already holds an ordinary procedure turns
    /// that procedure into the generic's default, so ordinary addition keeps working;
    /// dropping it would silently break every other caller of <c>+</c>.
    /// </para>
    /// </summary>
    public object Fallback { get; set; }

    /// <summary>Chooses the most specific method for an argument list.</summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The selected method, or <see langword="null"/> when none applies.</returns>
    public GenericMethod Select(object[] arguments)
    {
        GenericMethod best = null;
        int bestDepth = -1;

        foreach (GenericMethod method in Methods)
        {
            if (!method.Accepts(arguments, out int depth))
            {
                continue;
            }

            if (depth > bestDepth)
            {
                best = method;
                bestDepth = depth;
            }
        }

        return best;
    }
}

/// <summary>One method of a <see cref="GenericFunction"/>, with its parameter specializers.</summary>
public sealed class GenericMethod
{
    /// <summary>Initializes a method.</summary>
    /// <param name="specializers">The class each parameter is specialized on; null means any.</param>
    /// <param name="implementation">The procedure to invoke.</param>
    public GenericMethod(IReadOnlyList<SchemeClass> specializers, object implementation)
    {
        Specializers = specializers ?? Array.Empty<SchemeClass>();
        Implementation = implementation;
    }

    /// <summary>Gets the parameter specializers.</summary>
    public IReadOnlyList<SchemeClass> Specializers { get; }

    /// <summary>Gets the procedure to invoke.</summary>
    public object Implementation { get; }

    /// <summary>Determines whether this method applies to an argument list.</summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="specificity">Receives a score; more specific methods score higher.</param>
    /// <returns><see langword="true"/> when the method is applicable.</returns>
    public bool Accepts(object[] arguments, out int specificity)
    {
        specificity = 0;
        if (arguments.Length < Specializers.Count)
        {
            return false;
        }

        for (int i = 0; i < Specializers.Count; i++)
        {
            SchemeClass required = Specializers[i];
            if (required == null)
            {
                continue;
            }

            SchemeClass actual = BuiltinClasses.ClassOf(arguments[i]);
            if (actual == null || !actual.IsSubclassOf(required))
            {
                return false;
            }

            // Deeper matches win: a method on <integer> must beat one on <number> for the
            // same argument, so score by how far down the hierarchy the specializer sits.
            specificity += 1 + required.Depth;
        }

        return true;
    }
}

/// <summary>
/// A minimal GOOPS, Guile's object system.
/// <para>
/// This is NEW-IN-FAMILY, written against the documented GOOPS interface rather than
/// translated from <c>oop/goops.scm</c> — which is 3,551 lines and builds a full CLOS
/// with a metaobject protocol, class redefinition and multi-method dispatch on Guile's
/// low-level struct and vtable machinery.
/// </para>
/// <para>
/// LilyPond's actual use is tiny and was measured before this was written: five
/// <c>define-class</c>, twenty-seven <c>define-method</c>, eight <c>slot-ref</c>, two
/// <c>slot-set!</c>, and zero each of <c>define-generic</c>, <c>make-instance</c> and
/// class redefinition, across five files. That is what this implements.
/// </para>
/// </summary>
public static class GoopsPrimitives
{
    /// <summary>Installs the GOOPS primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("%make-class", 2, 3, arguments =>
        {
            Symbol name = arguments[0] as Symbol ?? Symbol.Intern("<anonymous>");
            List<SchemeClass> supers = new List<SchemeClass>();
            foreach (object super in Pair.ToList(arguments[1]))
            {
                if (super is SchemeClass superClass)
                {
                    supers.Add(superClass);
                }
            }

            SchemeClass created = new SchemeClass(name, supers);
            if (arguments.Length > 2)
            {
                foreach (object slotSpec in Pair.ToList(arguments[2]))
                {
                    created.Slots.Add(ParseSlot(slotSpec));
                }
            }

            return created;
        });

        interpreter.DefinePrimitive("class?", 1, 1, a => a[0] is SchemeClass);
        interpreter.DefinePrimitive("instance?", 1, 1, a => a[0] is SchemeObject);

        interpreter.DefinePrimitive("class-of", 1, 1, a => BuiltinClasses.ClassOf(a[0]));

        interpreter.DefinePrimitive("class-name", 1, 1, a =>
            a[0] is SchemeClass declared ? (object)declared.ClassName : false);

        interpreter.DefinePrimitive("is-a?", 2, 2, a =>
            a[1] is SchemeClass required && BuiltinClasses.ClassOf(a[0]).IsSubclassOf(required));

        // (make <class> #:slot value ...)
        interpreter.DefinePrimitive("make", 1, -1, arguments =>
        {
            if (!(arguments[0] is SchemeClass target))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("make"),
                        new MutableString("Not a class: ~S"),
                        Pair.List(arguments[0]),
                        false));
            }

            SchemeObject instance = new SchemeObject(target);
            List<SlotDefinition> slots = target.AllSlots();

            foreach (SlotDefinition slot in slots)
            {
                object initial = slot.InitialValue;
                if (slot.InitThunk != null)
                {
                    initial = interpreter.Evaluator.Apply(slot.InitThunk, Array.Empty<object>());
                }

                instance.Slots[slot.SlotName] = initial;
            }

            // Keyword initializers override the declared defaults.
            for (int i = 1; i + 1 < arguments.Length; i += 2)
            {
                if (!(arguments[i] is Keyword keyword))
                {
                    continue;
                }

                foreach (SlotDefinition slot in slots)
                {
                    if (slot.InitKeyword != null && ReferenceEquals(slot.InitKeyword, keyword))
                    {
                        instance.Slots[slot.SlotName] = arguments[i + 1];
                        break;
                    }
                }
            }

            return instance;
        });

        interpreter.DefinePrimitive("slot-ref", 2, 2, a =>
        {
            if (a[0] is SchemeObject instance
                && a[1] is Symbol slotName
                && instance.Slots.TryGetValue(slotName, out object value))
            {
                return value;
            }

            return false;
        });

        interpreter.DefinePrimitive("slot-set!", 3, 3, a =>
        {
            if (a[0] is SchemeObject instance && a[1] is Symbol slotName)
            {
                instance.Slots[slotName] = a[2];
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("slot-bound?", 2, 2, a =>
            a[0] is SchemeObject instance
            && a[1] is Symbol slotName
            && instance.Slots.ContainsKey(slotName));

        interpreter.DefinePrimitive("slot-exists?", 2, 2, a =>
            a[0] is SchemeObject instance
            && a[1] is Symbol slotName
            && instance.Slots.ContainsKey(slotName));

        InstallGenerics(interpreter);
    }

    private static void InstallGenerics(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("%make-generic", 0, 1, a =>
        {
            GenericFunction generic = new GenericFunction();
            if (a.Length > 0 && a[0] is Symbol name)
            {
                generic.Name = name.Name;
            }

            return generic;
        });

        interpreter.DefinePrimitive("generic?", 1, 1, a => a[0] is GenericFunction);

        // (%ensure-generic! 'name) -- return the generic bound to name in the current
        // module, creating and binding one when the name is unbound or holds something
        // else. This is what lets several define-methods with the same name accumulate.
        interpreter.DefinePrimitive("%ensure-generic!", 1, 1, a =>
        {
            Symbol name = TypeChecks.AsSymbol(a[0], "%ensure-generic!", 1);
            Variable existing = interpreter.CurrentModule.Lookup(name);
            object current = existing != null && existing.IsBound ? existing.GetValue() : null;
            if (current is GenericFunction found)
            {
                return found;
            }

            // A generic-capable PRIMITIVE is extended IN PLACE and never shadowed, which is
            // what oop/goops.scm's add-method! does through enable-primitive-generic!. The
            // generic then lives on the shared primitive object, so the extension is global
            // — see PrimitiveGenerics for why defining a fresh generic here instead left
            // every (- pitch pitch) throwing wrong-type-arg outside the defining module.
            if (current is Primitive capable && capable.IsGenericCapable)
            {
                return PrimitiveGenerics.Enable(capable);
            }

            // A name that already holds an ORDINARY procedure is refused, as GOOPS refuses
            // it: add-method! on a <procedure> that is not generic-capable falls through to
            // its <top> method, (goops-error #f "~S is not a valid generic function" (proc)
            // ()). MEASURED on the pinned 2.27.2 with a plain (define (qux x) ...) followed
            // by (define-method (qux (x <foo>)) ...).
            //
            //was previously: the procedure became the new generic's Fallback -- "keeps that
            // procedure as the generic's default" -- a leniency written before generic-capable
            // PRIMITIVES were extended in place (the case it was written for, LilyPond's
            // operators.scm on `+' and `*', is the branch above now). Changed 2026-08-28.
            if (current is Procedure plain)
            {
                throw new SchemeThrow(
                    Symbol.Intern("goops-error"),
                    Pair.List(
                        false,
                        new MutableString("~S is not a valid generic function"),
                        Pair.List(plain),
                        Nil.Instance));
            }

            GenericFunction generic = new GenericFunction { Name = name.Name };
            interpreter.CurrentModule.Define(name, generic);
            return generic;
        });

        // (%add-method! generic (specializer-or-#f ...) procedure)
        interpreter.DefinePrimitive("%add-method!", 3, 3, a =>
        {
            if (!(a[0] is GenericFunction generic))
            {
                return Unspecified.Instance;
            }

            List<SchemeClass> specializers = new List<SchemeClass>();
            foreach (object specializer in Pair.ToList(a[1]))
            {
                specializers.Add(specializer as SchemeClass);
            }

            generic.Methods.Add(new GenericMethod(specializers, a[2]));
            return Unspecified.Instance;
        });

        // (%invoke-generic generic args...) -- dispatch on the argument classes.
        interpreter.DefinePrimitive("%invoke-generic", 1, -1, a =>
        {
            GenericFunction generic = (GenericFunction)a[0];
            object[] arguments = new object[a.Length - 1];
            Array.Copy(a, 1, arguments, 0, arguments.Length);

            GenericMethod method = generic.Select(arguments);
            if (method != null)
            {
                return interpreter.Evaluator.Apply(method.Implementation, arguments);
            }

            if (generic.Fallback != null)
            {
                return interpreter.Evaluator.Apply(generic.Fallback, arguments);
            }

            //was previously: (goops-error "name" "No applicable method" () #f) -- Guile's shape
            // names the generic object and the whole call; see PrimitiveGenerics.NoApplicableMethod.
            throw PrimitiveGenerics.NoApplicableMethod(generic, generic.Name, arguments);
        });
    }

    private static SlotDefinition ParseSlot(object slotSpec)
    {
        // A slot is either a bare symbol or (name #:option value ...).
        if (slotSpec is Symbol bare)
        {
            return new SlotDefinition(bare);
        }

        List<object> parts = Pair.ToList(slotSpec);
        if (parts.Count == 0 || !(parts[0] is Symbol name))
        {
            return new SlotDefinition(Symbol.Intern("unnamed"));
        }

        SlotDefinition slot = new SlotDefinition(name);
        for (int i = 1; i + 1 < parts.Count; i += 2)
        {
            if (!(parts[i] is Keyword option))
            {
                continue;
            }

            switch (option.Name.Name)
            {
                case "init-value":
                    slot.InitialValue = parts[i + 1];
                    break;
                case "init-keyword":
                    slot.InitKeyword = parts[i + 1] as Keyword;
                    break;
                case "init-thunk":
                    slot.InitThunk = parts[i + 1];
                    break;
                default:
                    // accessor, getter and setter are handled by the define-class macro,
                    // which needs them at expansion time to emit the procedures.
                    break;
            }
        }

        return slot;
    }
}
