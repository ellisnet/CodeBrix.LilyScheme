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
/// A lexical environment frame. Frames form a chain from the innermost binding contour
/// out to the module, where lookup hands off to <see cref="SchemeModule"/>.
/// </summary>
public sealed class LexicalEnvironment
{
    private readonly Dictionary<Symbol, Variable> _slots;

    /// <summary>Initializes a frame.</summary>
    /// <param name="parent">The enclosing frame, or <see langword="null"/> at the top.</param>
    /// <param name="capacity">An initial capacity hint.</param>
    public LexicalEnvironment(LexicalEnvironment parent, int capacity)
    {
        Parent = parent;
        _slots = new Dictionary<Symbol, Variable>(capacity);
    }

    /// <summary>Gets the enclosing frame, or <see langword="null"/> at the top.</summary>
    public LexicalEnvironment Parent { get; }

    /// <summary>Binds a name in this frame.</summary>
    /// <param name="name">The name to bind.</param>
    /// <param name="value">The initial value.</param>
    /// <returns>The variable created.</returns>
    public Variable Define(Symbol name, object value)
    {
        Variable variable = new Variable(value);
        _slots[name] = variable;
        return variable;
    }

    /// <summary>Looks a name up through the frame chain.</summary>
    /// <param name="name">The name to find.</param>
    /// <returns>The variable, or <see langword="null"/> when not lexically bound.</returns>
    public Variable Lookup(Symbol name)
    {
        LexicalEnvironment frame = this;
        while (frame != null)
        {
            if (frame._slots.TryGetValue(name, out Variable variable))
            {
                return variable;
            }

            frame = frame.Parent;
        }

        return null;
    }
}
