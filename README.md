# CodeBrix.LilyScheme

A managed, cross-platform Scheme language implementation for .NET, derived from the GNU Guile project.
CodeBrix.LilyScheme has no dependencies other than .NET, and is provided as a .NET 10 library and associated `CodeBrix.LilyScheme.LgplLicenseForever` NuGet package.

CodeBrix.LilyScheme supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.LilyScheme.LgplLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.LilyScheme`:

* NuGet package ID: `CodeBrix.LilyScheme.LgplLicenseForever`
* Assembly and root namespace: `CodeBrix.LilyScheme` - i.e. `using CodeBrix.LilyScheme;`

The `.LgplLicenseForever` suffix is part of the package ID only, chosen so that the LGPL-3.0-or-later license identification travels with the package name. The package has no NuGet dependencies and no native libraries; it depends only on the .NET base class library, and runs on Windows, Linux and macOS. XML documentation (IntelliSense) ships alongside the assembly.

**Read the License section below before taking this dependency.** Unlike the rest of the CodeBrix family, this package is copyleft: LGPL-3.0-or-later permits linking from a differently-licensed application, but it attaches conditions - notably that your recipients must be able to relink your application against a modified build of this library, so it must not be ILMerged, ILRepack'd or shipped only as a trimmed or single-file artifact from which it cannot be replaced. If your project cannot accept those conditions, do not reference this package.

## Status

CodeBrix.LilyScheme is working software. It runs the Scheme layer of a full music-engraving
application: GNU LilyPond's, through the sibling `CodeBrix.LilyPort` project, which loads more
than ninety vendored `.scm` files on top of it and drives them to produce engraved output.

What is implemented:

- **Full `syntax-case` macro expansion**, by bootstrapping Guile's own expander. `psyntax-pp.scm`
  is vendored verbatim and loaded by a small core evaluator; from there the library has hygienic
  macros, `syntax-rules`, `quasisyntax` and the rest, without anyone hand-writing an expander.
- **Guile's Tree-IL** — all eighteen node types, evaluated directly.
- **The module system**: `define-module`, `use-modules` (including `#:select` renaming), public
  interfaces and exports, submodules, autoloading of vendored modules, and the anonymous-module
  naming that macro hygiene depends on.
- **GOOPS**, generic functions, and extension of generic-capable primitives.
- **The numeric tower** — fixnum, bignum, rational, real and complex.
- **A Guile-dialect reader**: keywords, extended symbols, `#nil`, block and datum comments, array
  literals, Guile's fixed-width string escapes, and reader hash extensions. It records source
  locations, which is what lets the expander attach them to procedures and error messages.
- **Ports**, including soft ports, string ports and Guile's `#:encoding` keyword handling.
- **An expansion cache** that records each file's expanded Tree-IL and replays it, which takes a
  cold start of roughly half a minute down to milliseconds.
- **A POSIX-flavored surface**: `(ice-9 rdelim)`, `(ice-9 popen)` with `system`/`system*`,
  `stat`, `strftime`/`localtime`/`gmtime`, `(ice-9 getopt-long)`, POSIX regular expressions with
  `(ice-9 regex)`, and `(srfi srfi-43)`.
- **Guile 3's modern exception API**: exception objects, `raise-exception`,
  `with-exception-handler` (`#:unwind?` and `#:unwind-for-type`), `(ice-9 exceptions)` with its
  standard exception types, `define-exception-type`, `raise-continuable` and R7RS `guard` — fully
  interoperable with classic `catch`/`throw` in both directions, on Guile's single-inheritance
  record model (`make-record-type` with `#:parent`).
- **An opt-in module-privacy switch**: by default `use-modules` imports a whole module (visible
  scope wider than Guile's, never narrower); setting `Interpreter.NarrowModuleImports = true`
  imports public interfaces instead, exactly as Guile documents.

Twenty-nine `.scm` files from the GNU Guile source tree ship verbatim as embedded resources;
the C# is new-in-family, written against R7RS, the SRFI documents and Guile's published
interfaces rather than translated from Guile's C. See `THIRD-PARTY-NOTICES.txt` for the
per-file attribution ledger.

The scope is deliberate: this implements the subset of Guile that LilyPond needs, and its visible
scope is in places wider than Guile's rather than narrower. It is not a general-purpose Guile
replacement, and it has no VM, no compiler and no FFI.

## Quick start

```csharp
using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;

Interpreter interpreter = new Interpreter();
Interpreter.RunWithLargeStack(() =>
{
    SchemeBootstrap.LoadCore(interpreter);   // psyntax + prelude
    object form = SchemeReader.ReadAll("(+ 1 2)", "<input>")[0];
    object value = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
    Console.WriteLine(Printer.Write(value)); // 3
});
```

Deeply recursive Scheme — loading psyntax above all — overflows the CLR's default thread stack,
which is why evaluation runs inside `Interpreter.RunWithLargeStack`.

## Documentation

Full guidance — architecture, API reference, usage, and the sharp edges worth knowing before
changing anything — is in
[AGENT-README.txt](https://github.com/ellisnet/CodeBrix.LilyScheme/blob/main/AGENT-README.txt).
It is written for AI coding agents and is equally the human reference, and it ships inside the
NuGet package alongside this file and `THIRD-PARTY-NOTICES.txt`.

## License

The project is licensed under the LGPL-3.0-or-later License. see: https://en.wikipedia.org/wiki/GNU_Lesser_General_Public_License

The LGPL-3.0 supplements the GPL-3.0 by reference, so the GPL-3.0 text is also included; see `LICENSE` and `LICENSE.GPL` respectively. Both are shipped inside the NuGet package.

Attribution and licensing-compliance records for all third-party material are in `THIRD-PARTY-NOTICES.txt`.
