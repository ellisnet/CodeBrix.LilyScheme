// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>A closure: a lambda body captured together with its defining environment.</summary>
public sealed class Closure : Procedure
{
    /// <summary>Initializes a closure.</summary>
    /// <param name="signature">The parsed parameter list.</param>
    /// <param name="body">The body forms as a Scheme list.</param>
    /// <param name="environment">The captured lexical environment.</param>
    /// <param name="module">The module the closure was defined in.</param>
    public Closure(LambdaSignature signature, object body, LexicalEnvironment environment, SchemeModule module)
    {
        Signature = signature;
        Body = body;
        Environment = environment;
        Module = module;
    }

    /// <summary>Gets the parameter list.</summary>
    public LambdaSignature Signature { get; }

    /// <summary>Gets the body forms.</summary>
    public object Body { get; }

    /// <summary>Gets the captured lexical environment.</summary>
    public LexicalEnvironment Environment { get; }

    /// <summary>Gets the module the closure was defined in.</summary>
    public SchemeModule Module { get; }
}
