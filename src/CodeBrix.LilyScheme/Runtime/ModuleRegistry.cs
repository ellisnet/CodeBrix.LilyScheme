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
/// The module registry. Guile keeps a global tree of modules keyed by name; this is the
/// flat equivalent, which is sufficient because module names are compared as whole lists.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, SchemeModule> _modules = new Dictionary<string, SchemeModule>(StringComparer.Ordinal);

    private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the root module, <c>(guile)</c>.</summary>
    public SchemeModule RootModule { get; set; }

    /// <summary>
    /// Gets or sets the hook that populates a module the first time it is resolved.
    /// <para>
    /// Guile autoloads a module's source the first time it is named, so
    /// <c>(use-modules (srfi srfi-1))</c> is enough to get <c>fold</c> and friends.
    /// Without this hook a newly resolved module is simply empty, and every name it
    /// would have supplied comes out unbound -- which looks like dozens of unrelated
    /// failures rather than one missing mechanism.
    /// </para>
    /// <para>
    /// The hook returns <see langword="true"/> when it loaded something. Returning
    /// <see langword="false"/> is normal: a module with no source behind it, such as a
    /// module the host program creates itself, stays empty.
    /// </para>
    /// </summary>
    public Func<object, SchemeModule, bool> ModuleLoader { get; set; }

    /// <summary>Resolves a module by name, creating and autoloading it when absent.</summary>
    /// <param name="name">The module name as a Scheme list.</param>
    /// <returns>The module.</returns>
    public SchemeModule Resolve(object name)
    {
        string key = Printer.Write(name);
        if (_modules.TryGetValue(key, out SchemeModule existing))
        {
            return existing;
        }

        SchemeModule created = new SchemeModule(name);
        if (RootModule != null && !ReferenceEquals(created, RootModule))
        {
            // Every module sees the core bindings, exactly as Guile's (guile) module is
            // implicitly available everywhere.
            created.AddUse(RootModule);
        }

        // Register BEFORE loading: a module's own source opens with (define-module ...),
        // which resolves the same name again. Registering first turns that into a lookup
        // instead of unbounded recursion.
        _modules[key] = created;
        LinkIntoParent(created);

        if (ModuleLoader != null && _loading.Add(key))
        {
            try
            {
                ModuleLoader(name, created);
            }
            finally
            {
                _loading.Remove(key);
            }
        }

        return created;
    }

    /// <summary>Registers a module under its own name.</summary>
    /// <param name="module">The module to register.</param>
    public void Register(SchemeModule module)
    {
        _modules[Printer.Write(module.ModuleName)] = module;
        LinkIntoParent(module);
    }

    /// <summary>
    /// Records a module in its parent's <see cref="SchemeModule.Submodules"/> table, so
    /// that <c>module-submodules</c> answers the way Guile's does.
    /// <para>The parent is looked up rather than RESOLVED: resolving would autoload it,
    /// and the parent's own load is frequently what is creating this child in the first
    /// place. A child whose parent is not registered yet simply is not linked, which is
    /// the same answer Guile gives for a module reached before its namespace exists.</para>
    /// </summary>
    /// <param name="module">The module to link.</param>
    private void LinkIntoParent(SchemeModule module)
    {
        if (!(module.ModuleName is Pair))
        {
            return;
        }

        List<object> components = new List<object>();
        for (object p = module.ModuleName; p is Pair pair; p = pair.Cdr)
        {
            components.Add(pair.Car);
        }

        object parentName = Nil.Instance;
        for (int i = components.Count - 2; i >= 0; i--)
        {
            parentName = new Pair(components[i], parentName);
        }

        if (_modules.TryGetValue(Printer.Write(parentName), out SchemeModule parent))
        {
            parent.Submodules.Set(components[components.Count - 1], module);
        }
    }

    /// <summary>Gets the number of registered modules.</summary>
    public int Count => _modules.Count;

    /// <summary>Gets the registered modules.</summary>
    public IEnumerable<SchemeModule> All => _modules.Values;
}
