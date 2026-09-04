# CodeBrix.LilyScheme

A managed, cross-platform Scheme language implementation for .NET, for applications that need
to run Scheme source - or to embed a scriptable Scheme layer of their own - without leaving
managed code.
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
* Assembly and primary namespace: `CodeBrix.LilyScheme` - i.e. `using CodeBrix.LilyScheme;`

The `.LgplLicenseForever` suffix is part of the package ID only, chosen so that the LGPL-3.0-or-later license identification travels with the package name.

XML documentation (IntelliSense) ships alongside the assembly.

The package has no NuGet dependencies and no native libraries - the whole implementation, including the Scheme source it loads at bootstrap, ships inside one managed assembly as embedded resources. It depends only on the .NET base class library, and runs on Windows, Linux and macOS.

**Read the License section below before taking this dependency.** Unlike the rest of the CodeBrix family, this package is copyleft: LGPL-3.0-or-later permits linking from a differently-licensed application, but it attaches conditions - notably that your recipients must be able to relink your application against a modified build of this library, so it must not be ILMerged, ILRepack'd or shipped only as a trimmed or single-file artifact from which it cannot be replaced. If your project cannot accept those conditions, do not reference this package.

## CodeBrix.LilyScheme supports:

* Full `syntax-case` macro expansion - hygienic macros, `syntax-rules`, `quasisyntax` and the rest
* Tree-IL, the expander's intermediate representation - all eighteen node types, evaluated directly
* The module system: `define-module`, `use-modules` (including `#:select` renaming), public interfaces and exports, submodules, autoloading, and the anonymous-module naming that macro hygiene depends on
* Module privacy that matches the language's own: `use-modules` without `#:select` imports a module's **public interface**, and setting `Interpreter.NarrowModuleImports = false` asks instead for the wide import - the whole module, private names included
* GOOPS: classes, generic functions, `define-method` dispatch, and extension of generic-capable primitives
* The full numeric tower - fixnum, bignum, rational, real and complex
* A dialect reader: keywords, extended symbols, `#nil`, block and datum comments, array literals, fixed-width string escapes, and reader hash extensions. It records source locations, which is what lets the expander attach them to procedures and error messages
* All three R7RS line endings in source text, so a file checked out or authored with CRLF endings loads
* Ports - soft ports, string ports, file ports and the `#:encoding` keyword - each tracking its own line and column, readable and settable from Scheme
* An expansion cache that records each file's expanded Tree-IL and replays it, so the cost of a cold expansion is paid once rather than on every start
* A POSIX-flavored surface: `(ice-9 rdelim)`, `(ice-9 popen)` with `system`/`system*`, `stat`, `strftime`/`localtime`/`gmtime`, `(ice-9 getopt-long)`, POSIX regular expressions with `(ice-9 regex)`, and `(srfi srfi-43)`
* The modern exception API: exception objects, `raise-exception`, `with-exception-handler` (`#:unwind?` and `#:unwind-for-type`), `(ice-9 exceptions)` with its standard exception types, `define-exception-type`, `raise-continuable` and R7RS `guard` - fully interoperable with classic `catch`/`throw` in both directions, on a single-inheritance record model (`make-record-type` with `#:parent`)
* Embedding from C#: register host primitives and values, redirect the output, error and input streams, install your own module loader, and read every Scheme value back as a .NET object

Everything is interpreted. There is no VM, no compiler, no bytecode and no FFI, and
`call-with-current-continuation` is not implemented - prompts are escape-only. The
`AGENT-README.txt` described below carries the complete list of what is and is not there.

## Sample Code

### Evaluate an Expression

```csharp
using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;

Interpreter interpreter = new Interpreter();
Interpreter.RunWithLargeStack(() =>
{
    SchemeBootstrap.LoadCore(interpreter);   // the macro expander and the prelude
    object form = SchemeReader.ReadAll("(+ 1 2)", "<input>")[0];
    object value = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
    Console.WriteLine(Printer.Write(value)); // 3
});
```

Deeply recursive Scheme - loading the expander above all - overflows the CLR's default thread
stack, which is why evaluation runs inside `Interpreter.RunWithLargeStack`.

### Define a Host Primitive and Call It from Scheme

```csharp
using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

Interpreter interpreter = new Interpreter();
Interpreter.RunWithLargeStack(() =>
{
    SchemeBootstrap.LoadCore(interpreter);

    // Two required arguments: (host-join symbol string) -> string.
    // TypeChecks and StringPrimitives raise a catchable Scheme wrong-type-arg
    // rather than letting an InvalidCastException escape to the host.
    interpreter.DefinePrimitive("host-join", 2, 2, a =>
    {
        Symbol prefix = TypeChecks.AsSymbol(a[0], "host-join", 1);
        string suffix = StringPrimitives.Text(a[1], "host-join");
        return new MutableString(prefix.Name + ":" + suffix);
    });

    object result = interpreter.EvalString("(host-join 'greeting \"hello\")", "<host>");
    Console.WriteLine(Printer.Display(result));   // greeting:hello
});
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library. It is equally the human reference, and it ships alongside this file and `THIRD-PARTY-NOTICES.txt` inside the package.

Additional sample code and usage examples are available in the `CodeBrix.LilyScheme.Tests` project:
https://github.com/ellisnet/CodeBrix.LilyScheme/tree/main/tests/CodeBrix.LilyScheme.Tests

That suite is the executable specification for everything the library does. It is long-running by design, so it is meant to be read rather than run.

## License

CodeBrix.LilyScheme is licensed under the LGPL-3.0-or-later - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.LilyScheme/blob/main/LICENSE) file.

The LGPL-3.0 supplements the GPL-3.0 by reference, so the GPL-3.0 text must travel with the work as well; it is in
[LICENSE.GPL](https://github.com/ellisnet/CodeBrix.LilyScheme/blob/main/LICENSE.GPL). Both texts are shipped inside the NuGet package.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.LilyScheme/blob/main/THIRD-PARTY-NOTICES.txt).
