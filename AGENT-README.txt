================================================================================
AGENT-README: CodeBrix.LilyScheme
A Guide for AI Coding Agents — CONSUMING the CodeBrix.LilyScheme.LgplLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.LilyScheme is a managed, cross-platform Scheme language implementation
for .NET, derived from the GNU Guile project. It implements the subset of Guile
that LilyPond's Scheme layer needs: full syntax-case macro expansion, Guile's
module system, GOOPS, the numeric tower, a Guile-dialect reader, ports, POSIX
and regular-expression surfaces, and Guile 3's modern exception API.

Target framework: .NET 10 or later. No native dependencies, no other NuGet
dependencies -- the whole implementation, including the vendored Guile Scheme it
loads, ships inside one managed assembly as embedded resources.

PROVENANCE. The Scheme source files this library loads are vendored VERBATIM
from the GNU Guile source tree, at the audited revision recorded in
THIRD-PARTY-NOTICES.txt; the C# is new-in-family, written against R7RS, the SRFI documents and Guile's published
interfaces rather than translated from Guile's C. The library's own namespaces
all start with CodeBrix.LilyScheme -- there are no Guile namespaces to use, and
nothing to alias.

THE CENTRAL IDEA, in one paragraph, because it explains the API's shape: Guile
ships its macro expander twice -- as psyntax.scm, written in syntax-case, and as
psyntax-pp.scm, the same expander already macro-expanded into core Scheme so it
can be loaded by an implementation that does not yet have a macro expander. That
is how Guile bootstraps itself, and it is how LilyScheme gets full syntax-case
without anyone hand-writing an expander. So there are TWO evaluators in the
public API: a small core s-expression evaluator that exists to load psyntax, and
a Tree-IL evaluator that runs everything psyntax expands. Guile's macroexpand
does not return s-expressions; it returns Tree-IL structs. Which evaluator you
call is the single most important decision an embedder makes -- see HOW
EVALUATION WORKS below.

INSTALLATION
============
NuGet package id: CodeBrix.LilyScheme.LgplLicenseForever

    dotnet add package CodeBrix.LilyScheme.LgplLicenseForever

The ROOT NAMESPACE is CodeBrix.LilyScheme, without the .LgplLicenseForever
suffix -- that suffix is part of the package id only, chosen so the
LGPL-3.0-or-later license identification travels with the package name. The
assembly is CodeBrix.LilyScheme.dll.

NuGet dependencies: none.
License: LGPL-3.0-or-later. This is a copyleft license and it constrains how you
may link and redistribute -- read LICENSING AND REDISTRIBUTION below BEFORE
choosing this package.
Requirements: .NET 10 or later. No native libraries. Windows, Linux and macOS;
the only OS-conditional behaviour is in the POSIX surface, and it is documented
where it occurs.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.LilyScheme;              // Interpreter
    using CodeBrix.LilyScheme.Caching;      // ExpansionCache, ExpansionCacheFile
    using CodeBrix.LilyScheme.Numeric;      // SchemeNumber, Ratio, ComplexNumber
    using CodeBrix.LilyScheme.Primitives;   // TypeChecks, SchemeHashTable, ports,
                                            //   ColumnTrackingWriter,
                                            //   GOOPS classes, BuiltinClasses
    using CodeBrix.LilyScheme.Reader;       // SchemeReader, SourceProperties,
                                            //   PortPosition
    using CodeBrix.LilyScheme.Runtime;      // SchemeBootstrap, Printer, Evaluator,
                                            //   SchemeModule, SchemeThrow
    using CodeBrix.LilyScheme.TreeIl;       // TreeIlEvaluator, TreeIlClosure
    using CodeBrix.LilyScheme.Unicode;      // UnicodeCharacterNames
    using CodeBrix.LilyScheme.Values;       // Pair, Symbol, MutableString, ...

Interpreter is the ONLY type in the root namespace; it is the entry point.

QUICK START
===========
    using System;
    using CodeBrix.LilyScheme;
    using CodeBrix.LilyScheme.Reader;
    using CodeBrix.LilyScheme.Runtime;

    Interpreter interpreter = new Interpreter();
    Interpreter.RunWithLargeStack(() =>
    {
        SchemeBootstrap.LoadCore(interpreter);        // psyntax + prelude
        object form = SchemeReader.ReadAll("(+ 1 2)", "<input>")[0];
        object value = interpreter.TreeIlEvaluator.ExpandAndEval(
            form, interpreter.CurrentModule);
        Console.WriteLine(Printer.Write(value));      // 3
    });

Three things in that snippet are not optional:

* SchemeBootstrap.LoadCore(interpreter) loads psyntax and the prelude. Until it
  has run, there are no macros, no `and'/`or'/`cond'/`case', no module forms and
  no vendored modules. A fresh Interpreter has ONLY the C# primitives.
* Interpreter.RunWithLargeStack wraps the work. psyntax recurses hard while
  expanding and overflows the CLR's default 1 MB stack; the limit is per thread,
  so a dedicated thread is the fix. Interpreter.LargeStackBytes is the size used.
  An exception raised on that thread reaches the caller AS ITSELF, with its
  original stack trace, so catch clauses read exactly as they would without the
  thread.
* TreeIlEvaluator.ExpandAndEval is the EXPLICIT macro-expanding evaluator, and it
  is what a form goes through when you hold the module yourself. Interpreter.Eval
  and EvalString expand too, once psyntax is loaded; LoadFile and
  LoadFileWithProgress never do. See the next section.

CORE API REFERENCE
==================
The API reference is organised by feature area in the sections that follow.

HOW EVALUATION WORKS: THE TWO EVALUATORS
========================================
    source text
       -> SchemeReader                  (text -> Scheme data)
       -> Evaluator                     (core s-expression evaluator; bootstrap)
       -> psyntax (vendored)            (macroexpand -> Tree-IL structs)
       -> TreeIlEvaluator               (evaluates the eighteen Tree-IL nodes)

Interpreter.Evaluator is the CORE evaluator. It knows quote, if, lambda, lambda*,
let, letrec, begin, set!, define, define-syntax (recorded, not expanded),
quasiquote, case-lambda, delay and eval-when, and nothing else. It has proper
tail calls. It exists so psyntax can be loaded. It DOES NOT EXPAND MACROS.

Interpreter.TreeIlEvaluator is what everything else runs on. ExpandAndEval calls
psyntax's macroexpand and evaluates the resulting Tree-IL.

    Interpreter.LoadFile(path)              -> core evaluator, NO macros
    Interpreter.LoadFileWithProgress(...)   -> core evaluator, NO macros

    Interpreter.Eval(form)                  -> full expansion once psyntax is loaded
    Interpreter.EvalString(text, fileName)  -> full expansion once psyntax is loaded
    interpreter.TreeIlEvaluator.ExpandAndEval(form, module)   -> full expansion
    SchemeBootstrap.LoadExpanded(interpreter, source, name)   -> full expansion

LoadFile and LoadFileWithProgress are the BOOTSTRAP path and are exactly right
for core-level forms; use them for anything that must run before or without
psyntax. Eval and EvalString go through psyntax as soon as IsPsyntaxLoaded is
set, exactly as Guile's `eval' and `eval-string' do, and fall back to the core
evaluator only before that -- so a macro defined by one EvalString is usable by
the next, and a bare (markup ...) evaluates. See pitfalls 1 and 52.

For ordinary consumer code -- anything containing `define-syntax', `when',
`cond', `use-modules', `define-module', a syntax-rules macro or a vendored
module -- use ExpandAndEval or LoadExpanded, which name the module explicitly, or
Eval/EvalString when Interpreter.CurrentModule is already where you want the form
to land. A macro use handed to the CORE evaluator does not error at definition
time; it fails later, where the macro is USED, as a wrong-type-arg saying the
transformer cannot be applied.

THE HOST API: Interpreter
=========================
    namespace CodeBrix.LilyScheme;
    public sealed class Interpreter

CONSTRUCTION AND STATE
----------------------
    Interpreter()
        Installs every C# primitive into the root (guile) module. Nothing
        Scheme-level is loaded -- call SchemeBootstrap.LoadCore next.

    const int LargeStackBytes
        The stack size RunWithLargeStack uses.

    ModuleRegistry Modules { get; }
    SchemeModule GuileModule { get; }          // the root (guile) module
    SchemeModule CurrentModule { get; set; }   // where top-level evaluation lands
    Evaluator Evaluator { get; }
    TreeIl.TreeIlEvaluator TreeIlEvaluator { get; }
    bool IsPsyntaxLoaded { get; set; }         // set by LoadPsyntax/LoadCore

EMBEDDING HOOKS
---------------
    Primitive DefinePrimitive(
        string name,
        int minimumArgumentCount,
        int maximumArgumentCount,       // -1 means variadic
        Func<object[], object> implementation)
        Registers a primitive procedure in the ROOT module, so every module sees
        it. Returns the Primitive, which you may keep (to set Properties, or to
        pass to PrimitiveGenerics.Enable).

    void DefineValue(string name, object value)
        Binds a non-procedure value in the root module.

    object Eval(object form)
    object EvalString(string text, string fileName)
        The EXPANDING entry points (see HOW EVALUATION WORKS): both run the form
        through psyntax once it is loaded, and through the core evaluator only
        before that. They evaluate in Interpreter.CurrentModule. EvalString reads
        every form and answers the last value, or Unspecified.Instance when there
        are none.

    object LoadFile(string path)
    object LoadFileWithProgress(string path, Action<int, object> onForm)
        The CORE-evaluator entry points, which never expand macros -- they are the
        boot paths that load psyntax itself. LoadFileWithProgress invokes onForm
        with the zero-based form index and the form itself BEFORE evaluating it --
        what you want when loading a large file and needing to know which form
        failed. For a consumer file, use SchemeBootstrap.LoadExpanded instead.

    TextWriter OutputWriter { get; set; }      // defaults to Console.Out
    TextWriter ErrorWriter { get; set; }       // defaults to Console.Error
    TextReader InputReader { get; set; }       // defaults to Console.In
        Behind display/write, warning output, and (current-input-port). Assign
        your own to capture or feed a run.

    TextWriter TrackedOutputWriter()
    TextWriter TrackedErrorWriter()
        The position-tracking writers standing in front of OutputWriter and
        ErrorWriter, created on first use and kept while the underlying writer is
        unchanged. Everything written through the default output and error ports
        goes through these, which is where port-line and port-column come from:
        current-output-port builds a FRESH SchemeOutputPort on every call, so the
        position cannot live on the port -- it would restart at zero each time --
        and lives on the one shared writer instead. Assigning OutputWriter or
        ErrorWriter is still the way to redirect; ask for the tracking writer only
        when you want the position, or when you are writing where Scheme writes
        and want the counters to stay honest. A writer that already tracks is
        handed back as itself rather than wrapped again.

    List<string> LoadPath { get; }
        Directories searched by %search-load-path and primitive-load-path, and
        therefore by (load-from-path "name"). Mutable; add to it before loading.

    Dictionary<Symbol, object> ObjectProperties { get; }
        The table behind object-property / set-object-property!. The value of
        each entry is a Scheme association list of (object . value) pairs.

    Caching.ExpansionCache ExpansionCache { get; set; }
        Null (the default) loads live. See THE EXPANSION CACHE.

    bool NarrowModuleImports { get; set; }
        TRUE BY DEFAULT since 2026-08-28: a use-modules WITHOUT #:select imports the
        module's PUBLIC INTERFACE, as Guile documents, through a live view that
        grows with the module's exports. Set it FALSE -- before loading anything it
        should govern -- to get the WIDE import (the whole module, private names
        included), which is what CodeBrix.LilyPort selects explicitly until its
        corpus has been swept under the narrow default. #:select clauses and the
        implicit core import behave identically either way. Both positions are
        fenced by NarrowImportTests.

RUNNING ON A BIG STACK
----------------------
    static void RunWithLargeStack(Action action)
    static T RunWithLargeStack<T>(Func<T> function)
        Runs the work on a dedicated thread with LargeStackBytes of stack. A
        failure is re-thrown to the caller AS ITSELF, with its original stack
        trace, rather than wrapped. Wrap essentially everything: loading the
        bootstrap, loading files, and any evaluation that can recurse.

BOOTSTRAP AND LOADING: SchemeBootstrap
======================================
    namespace CodeBrix.LilyScheme.Runtime;
    public static class SchemeBootstrap

    static int LoadCore(Interpreter interpreter)
        The one call a normal embedder makes. Loads psyntax, then the prelude,
        then enables module autoloading and installs the shim modules. Returns
        the number of top-level forms evaluated. Any registered reader hash
        extension is SUSPENDED for the duration and restored afterwards (see
        READER EXTENSIONS).

    static int LoadPsyntax(Interpreter interpreter)
        Just the expander. Sets IsPsyntaxLoaded. LoadCore calls it.

    static void PrepareForPsyntax(Interpreter interpreter)
        Installs the low-level names psyntax assigns into; LoadPsyntax calls it.

    static int LoadExpanded(Interpreter interpreter, string source, string fileName)
        Reads and evaluates Scheme source THROUGH psyntax and the Tree-IL
        evaluator. This is the loader for consumer code. Returns the form count.
        Consults Interpreter.ExpansionCache when one is assigned.

    static int LoadSource(Interpreter interpreter, string source, string fileName)
        Reads and evaluates through the CORE evaluator. Bootstrap use only.

    static void EnableModuleAutoload(Interpreter interpreter)
    static bool AutoloadVendoredModule(
        Interpreter interpreter, object name, SchemeModule module)
        Autoloading of the vendored Guile modules. LoadCore enables it.
        EnableModuleAutoload CHAINS: it keeps any ModuleLoader already installed
        and falls through to it, so ordering with your own loader is safe in
        either direction.

    static string ReadVendoredSource(string fileName)
    static string TryReadVendoredSource(string fileName)   // null when absent
    static string ReadPsyntaxSource()
    const string PsyntaxResourceName
    static readonly string[] PsyntaxAssignedNames
    static readonly string[] SelfProvidedModules

    static void InstallVmProgramShim(Interpreter interpreter)
    static void InstallIconvShim(Interpreter interpreter)
    static void InstallPopenShim(Interpreter interpreter)
        The C#-provided stand-ins for (system vm program), (ice-9 iconv) and
        (ice-9 popen). LoadCore installs all of them, plus the (ice-9 soft-ports)
        and (ice-9 unicode) shims. You do not normally call these yourself.

SelfProvidedModules is the list of module names that must NEVER be autoloaded
from the vendored source even though that source is present: (oop goops),
(ice-9 optargs), (ice-9 and-let-star), (ice-9 boot-9), (guile), (guile-user),
(system vm program), (ice-9 iconv), (ice-9 soft-ports), (ice-9 unicode) and
(ice-9 popen). If you install your own ModuleLoader, honour the same list.

READING SOURCE: SchemeReader AND SOURCE LOCATIONS
=================================================
    namespace CodeBrix.LilyScheme.Reader;
    public sealed class SchemeReader

    static List<object> ReadAll(string text, string fileName)
        Text to a list of data. fileName is what error messages and source
        properties name. This is the normal entry point.

    SchemeReader(string text, string fileName)
    object Read()          // next datum, or EofObject.Instance at end
    object ReadDatum()     // next datum, raising rather than answering EOF
    static object ParseNumber(string token)   // null when not a number

    bool IsAtEnd { get; }
    int Position { get; }
    int CurrentLine { get; }
    string SourceText { get; }
    string SourceFileName { get; }
    int PortLine { get; set; }      // ZERO-based, as port-line reports it
    int PortColumn { get; set; }    // as port-column reports it
    char PeekCharacter()
    char PeekCharacter(int offset)
    char ReadCharacterRaw()
    void RetreatPosition(char value)
        The character-level surface a hash-extension handler works over.
        PortLine and PortColumn are the counters a SchemeInputPort over this
        reader reports, and they are SETTABLE because set-port-line! and
        set-port-column! MOVE WHERE THE NEXT DATUM IS RECORDED. RetreatPosition
        walks the position back over a character being pushed back, which is what
        PushbackCharacter and unread-char need; ReadCharacterRaw advances it.

READER EXTENSIONS
-----------------
    delegate object HashExtension(SchemeReader reader);
    static void RegisterHashExtension(char dispatchCharacter, HashExtension extension)
    static IReadOnlyDictionary<char, HashExtension> SuspendHashExtensions()
    static void RestoreHashExtensions(IReadOnlyDictionary<char, HashExtension> extensions)

RegisterHashExtension is the equivalent of Guile's read-hash-extend: it takes
over one '#' dispatch character, and it takes PRECEDENCE over the built-in
syntax for that character. Registration is PROCESS-WIDE, not per interpreter.

SHARP EDGE: psyntax-pp.scm itself contains Guile extended symbols such as
#{ $sc-ellipsis }#. SchemeBootstrap.LoadCore therefore suspends every registered
extension while it reads Guile's own source and restores them afterwards. If you
write your own bootstrap sequence, do the same, or a second interpreter in the
same process reads psyntax through your extension and the expander is corrupted
with no error.

WHAT THE READER ACCEPTS
-----------------------
The Guile dialect: #:keywords, #{extended symbols}#, #nil, block (#| |#) and
datum (#;) comments, array literals (#1@1(...), #2((...) (...))), the full
numeric tower including rectangular and polar complex literals, character names,
and Guile's FIXED-WIDTH \uXXXX / \UXXXXXX string escapes (exactly four and six
hex digits). The quote family -- ' ` , -- are SYMBOL CONSTITUENTS once a token is
in progress: Hello' is one symbol, and quote syntax lives at datum start only.

A backslash at the end of a line continues a string literal across the break and
skips the leading blanks of the next line, and it accepts ALL THREE R7RS line
endings -- linefeed, carriage return, and carriage return followed by linefeed.
A consumer .scm file checked out or authored with CRLF endings therefore loads;
it is not yours to normalise, and the escape is legal input either way.

SOURCE LOCATIONS
----------------
    public sealed class SourceLocation
        SourceLocation(string fileName, int line, int column)
        string FileName { get; }
        int Line { get; }        // ZERO-based
        int Column { get; }      // ZERO-based

    public static class SourceProperties
        static bool Supports(object datum)          // Pair or object[] only
        static void Record(object datum, SourceLocation location)
        static object Get(object datum)             // Guile's alist, or #f
        static void Set(object datum, object properties)
        static object Property(object datum, object key)
        static void SetProperty(object datum, object key, object value)
        static void CopyTo(object source, object target)
        static void StampMissing(object form, SourceLocation location)
        static SourceLocation Located(object datum)

The reader records where it read each pair and vector into a WEAK table keyed by
object IDENTITY, and Scheme's source-properties reads it back as Guile's
((filename . F) (line . L) (column . C)) alist. This is not decoration: psyntax's
datum-sourcev asks source-properties and nothing else, then threads the answer
through expansion as the src field of every Tree-IL node. With the table empty
the expander has nothing to propagate, so every node carries #f, every procedure
prints as anonymous, and no error message can name a file -- all silently.

Two consequences for a host that rewrites forms before evaluating them:

* THE LINE IS ZERO-BASED AND THE COLUMN IS NOT ADJUSTED. Guile's own
  source-line-for-user is (1+ (source-line s)) and nothing adds anything to the
  column. Anything showing a location to a human adds the one back.
* A REWRITE THAT REBUILDS PAIRS DESTROYS LOCATIONS. Properties are keyed by
  object identity, so `new Pair(...)' over a form that already had one drops it.
  Return the ORIGINAL object when nothing changed, and SourceProperties.CopyTo
  across when a rebuild is genuinely needed. Forms a rewrite INVENTS should
  inherit the location of the form they came out of -- that is what Guile does
  for macro-introduced code.

THE VALUE MODEL: READING RESULTS FROM C#
========================================
Every Scheme value is an `object'. There is no wrapper type and no unboxing
step: you type-test what the evaluator hands back. This table is the whole
mapping an embedder needs.

    Scheme                 C# representation
    --------------------   ---------------------------------------------------
    #t / #f                bool
    the empty list ()      Values.Nil.Instance          (Values.Nil)
    a pair                 Values.Pair
    a symbol               Values.Symbol
    a keyword #:k          Values.Keyword
    a string               Values.MutableString
    a character            Values.SchemeChar
    a vector               object[]
    an exact integer       long, widening to System.Numerics.BigInteger
    an exact rational      Numeric.Ratio
    an inexact real        double
    a complex              Numeric.ComplexNumber
    unspecified            Values.Unspecified.Instance
    the EOF object         Values.EofObject.Instance
    #nil (Elisp nil)       Values.ElispNil.Instance
    multiple values        Values.MultipleValues
    a procedure            Values.Procedure (abstract) or an IApplicable
    a variable cell        Values.Variable
    a fluid                Values.Fluid
    a promise              Runtime.Promise or Values.LazyPromise
    a hash table           Primitives.SchemeHashTable
    an input port          Primitives.SchemeInputPort
    an output port         Primitives.SchemeOutputPort
    a character set        Values.CharSet
    an array (rank != 1)   Values.SchemeArray  (a rank-1 array IS object[])
    a record instance      object[] whose slot 0 is a Values.RecordType
    a record type          Values.RecordType
    a struct               Values.SchemeStruct   (Tree-IL nodes are these)
    a GOOPS class          Primitives.SchemeClass
    a GOOPS instance       Primitives.SchemeObject
    a module               Runtime.SchemeModule
    a compiled regexp      System.Text.RegularExpressions.Regex
    a directory stream     Primitives.DirectoryStream
    a hook                 Values.SchemeHook

LISTS AND PAIRS
---------------
    public sealed class Pair
        Pair(object car, object cdr)
        object Car { get; set; }
        object Cdr { get; set; }
        static object List(params object[] items)
        static object ListFrom(IEnumerable<object> items)
        static List<object> ToList(object list)
        static List<object> ToList(object list, out object tail)
        static int Length(object list)

    public sealed class Nil
        static Nil Instance { get; }

Car and Cdr are SETTABLE -- that is what set-car! / set-cdr! and the destructive
list family act on, and it is why a C# caller that hands a list into Scheme must
expect it to come back mutated. Pair.List() with no arguments answers
Nil.Instance. ToList's two-argument overload gives you the improper TAIL, which
is the only safe way to walk a list you did not build.

SYMBOLS, KEYWORDS, STRINGS AND CHARACTERS
-----------------------------------------
    public sealed class Symbol
        static Symbol Intern(string name)      // the only way to get an eq? symbol
        static Symbol Generate(string prefix)  // an UNINTERNED gensym
        string Name { get; }
        bool IsUninterned { get; }
        static readonly Symbol Quote, Quasiquote, Unquote, UnquoteSplicing,
            Lambda, LambdaStar, Define, If, SetBang, Begin, Let, LetStar, Letrec,
            LetrecStar, And, Or, Cond, Case, Else, Arrow, EvalWhen, When, Unless,
            CaseLambda, DefineSyntax, Do, Delay, OptionalMarker, KeyMarker,
            RestMarker, AllowOtherKeysMarker

    public sealed class Keyword
        static Keyword Get(string name)
        static Keyword Get(Symbol name)
        Symbol Name { get; }

    public sealed class MutableString
        MutableString(string value)
        MutableString(int length, char fill)
        int Length { get; }
        char this[int index] { get; set; }
        override string ToString()

    public sealed class SchemeChar : IEquatable<SchemeChar>
        static SchemeChar Get(int codePoint)
        int CodePoint { get; }

Symbols are compared with ReferenceEquals -- always go through Symbol.Intern,
never `new'. A generated symbol is UNINTERNED and can never collide with a
program symbol.

A Scheme string is a MutableString, NOT a System.String, because string-set! has
to work. Call ToString() to get text out; pass a `new MutableString(text)' to put
text in. A C# string handed to Scheme as a value is not a Scheme string and will
fail type checks -- the ONE exception is that most read-only text primitives
accept a symbol, character or keyword too, through StringPrimitives.Text.

SchemeChar holds a CODE POINT, not a char, so astral characters are one Scheme
character. Always build one with SchemeChar.Get -- it caches the ASCII range.
Equality still works for any character: eq? here compares boxed longs, bools and
SchemeChars BY VALUE (Guile's fixnums and characters are immediates, not heap
objects, so (eq? 5 5) is true there too), and eqv? adds exactness-aware numeric
equality on top.

SINGLETONS, VARIABLES AND VALUES
--------------------------------
    Values.Unspecified.Instance      what a side-effecting form answers
    Values.EofObject.Instance        end of input
    Values.ElispNil.Instance         #nil -- distinct from both #f and ()
    Values.DefaultArgument.Instance  the marker for an omitted optional argument

    public sealed class MultipleValues
        MultipleValues(object[] items)
        object[] Items { get; }

    public sealed class Variable
        Variable()                   // unbound
        Variable(object value)
        bool IsBound { get; }
        object GetValue()            // raises when unbound
        void SetValue(object value)

    public sealed class Fluid
        Fluid(object defaultValue)
        object Value { get; set; }

A Variable is a mutable BINDING CELL, not a value. Two modules can share one
cell, and a set! through either is seen by both -- that is what module-add!
means. If a lookup hands you a Variable where you expected a value, something
handed you the cell instead of calling GetValue().

MultipleValues comes back only from a producer of two or more values; a single
value is returned bare, exactly as Guile's `values' does.

PROCEDURES
----------
    public abstract class Procedure
        string Name { get; set; }
        object Properties { get; set; }     // Scheme alist; Nil.Instance default
        object Setter { get; set; }         // generalized set!, or null
        string EffectiveName { get; }       // the 'name PROPERTY first, then Name

    public sealed class Primitive : Procedure
        Primitive(string name, int minimumArgumentCount,
                  int maximumArgumentCount, Func<object[], object> implementation)
        int MinimumArgumentCount { get; }
        int MaximumArgumentCount { get; }   // -1 = variadic
        bool IsGenericCapable { get; set; }
        object AttachedGeneric { get; set; }
        object Invoke(object[] arguments)

    Runtime.Closure : Procedure             // a core-evaluator lambda
    TreeIl.TreeIlClosure : Procedure        // a psyntax-expanded lambda
    Runtime.CaseLambdaProcedure : Procedure
    Primitives.GenericFunction : Procedure

    public sealed class LambdaSignature
        LambdaSignature(IReadOnlyList<Symbol> required,
                        IReadOnlyList<OptionalParameter> optionals,
                        IReadOnlyList<OptionalParameter> keywords,
                        Symbol rest,
                        bool allowOtherKeys)
        IReadOnlyList<Symbol> Required { get; }
        IReadOnlyList<OptionalParameter> Optionals { get; }
        IReadOnlyList<OptionalParameter> Keywords { get; }
        Symbol RestParameter { get; }
        bool AllowOtherKeys { get; }
        bool IsSimple { get; }

    public sealed class OptionalParameter
        OptionalParameter(Symbol name, object defaultExpression, Keyword keyword)
        Symbol ParameterName { get; }
        object DefaultExpression { get; }
        Keyword SelectingKeyword { get; }

EffectiveName answers the `name' PROCEDURE PROPERTY before the definition-time
name, as scm_procedure_name does, because code names procedures after the fact:
LilyPond builds every markup command with a helper -- so it is anonymous -- and
then names it with (set-procedure-property! definition 'name command-name).

TreeIlClosure additionally carries Documentation (the docstring psyntax lifted
into the lambda's meta alist), Source (a SourceLocation or null) and
LambdaList(), the printed parameter list.

HASH TABLES AND PORTS
---------------------
    public sealed class SchemeHashTable
        SchemeHashTable(IEqualityComparer<object> comparer)
        int Count { get; }
        IEnumerable<Pair> Handles { get; }   // each handle IS the (key . value) pair
        Pair GetHandle(object key)           // null when absent
        Pair CreateHandle(object key, object initialValue)
        void Set(object key, object value)
        void Remove(object key)
        void Clear()

    public sealed class SchemeInputPort
        SchemeInputPort(string text, string fileName)
        SchemeInputPort(TextReader stream, string fileName)
        TextReader Stream { get; }
        string FileName { get; }
        string PortEncoding { get; set; }  // the reported name, e.g. "UTF-8"
        long Line { get; set; }            // ZERO-based, as port-line reports it
        long Column { get; set; }          // as port-column reports it
        bool IsFilePort { get; set; }
        bool IsClosed { get; set; }
        object ReadDatum()
        string ReadRemainingCharacters()
        string ReadCharacters(int count)
        char? ReadCharacter()
        char? PeekCharacter()
        void PushbackCharacter(char value)

    public sealed class SchemeOutputPort
        SchemeOutputPort(TextWriter writer)
        TextWriter Writer { get; set; }
        TextWriter InnerWriter { get; }   // the sink under the tracking wrapper
        string FileName { get; set; }     // null for a non-file port
        string PortEncoding { get; set; } // the reported name, e.g. "UTF-8"
        bool IsFilePort { get; }          // i.e. FileName != null
        bool IsClosed { get; set; }

A hash-table HANDLE is the live (key . value) pair -- Guile's hashx-get-handle
semantics -- so writing handle.Cdr writes into the table.

A port constructed from a TextReader STREAMS. That is the pipe shape, and such a
port REFUSES `read' loudly, because the datum reader works over a string and
buffering a live pipe to end-of-stream would block on the producer. Construct
from a string when Scheme code needs to `read' from the port.

SchemeOutputPort.Writer is NOT a plain auto-property. What you assign is WRAPPED
in a Primitives.ColumnTrackingWriter (unless it already tracks) so that port-line
and port-column answer for every output port, and the position carries over when
set-port-encoding! swaps the sink underneath a port that goes on existing. Read
InnerWriter, not Writer, when the concrete sink is what you want -- the
StringWriter a string port accumulates into, for instance, which is what
get-output-string and ftell need.

SchemeInputPort.Line and Column are the READER's own counters for a string-backed
port rather than a second set kept alongside; a stream-backed port keeps its own.
Both are settable, because set-port-line! and set-port-column! MOVE where the next
datum's source location is recorded. PortEncoding is carried on both port kinds as
a reported NAME: it is operative at the file boundary and nominal everywhere else,
since strings are UTF-16 throughout.

THE NUMERIC TOWER
=================
    namespace CodeBrix.LilyScheme.Numeric;

    public sealed class Ratio
        Ratio(BigInteger numerator, BigInteger denominator)
        BigInteger Numerator { get; }
        BigInteger Denominator { get; }
        double ToDouble()

    public sealed class ComplexNumber
        ComplexNumber(double real, double imaginary)
        double Real { get; }
        double Imaginary { get; }
        double Magnitude { get; }
        double Angle { get; }

    public static class SchemeNumber
        const long MostPositiveFixnum
        const long MostNegativeFixnum
        static bool IsNumber(object value)
        static bool IsExact(object value)
        static bool IsInteger(object value)
        static object Normalize(BigInteger value)    // narrows to long when it fits
        static BigInteger ToBigInteger(object value)
        static double ToDouble(object value)
        static object MakeRatio(object numerator, object denominator)
        static object ToInexact(object value)
        static object ToExact(object value)
        static object Add(object a, object b)
        static object Subtract(object a, object b)
        static object Multiply(object a, object b)
        static object Divide(object a, object b)
        static int Compare(object a, object b)
        static bool NumericEquals(object a, object b)
        static bool IsZero(object value)
        static object Negate(object value)
        static object Quotient(object a, object b)
        static object Remainder(object a, object b)
        static object Modulo(object a, object b)
        static object GreatestCommonDivisor(object a, object b)
        static string NumberToString(object value, int radix)
        static string ToDisplayString(object value)

An exact integer arrives as a `long' when it fits and a BigInteger when it does
not, and arithmetic narrows back through Normalize -- so NEVER test for `long'
alone. Use SchemeNumber.IsInteger / ToBigInteger / ToDouble, which handle every
representation including `int' (which primitives may produce).

The COMPLEX parts are doubles, so a complex here is always inexact and PRINTS as
such (1.0+2.0i -- pitfall 50); Guile's
exact complexes are not modelled. An EXACT ZERO imaginary part collapses to the
real in the reader -- 1+0i IS the exact integer 1, while 1.0+0.0i stays complex.
A product involving a complex is COMPUTED and never short-circuited, so (* 0 z)
is 0.0+0.0i and not an exact 0 (pitfall 50). number? and complex? are the same
predicate; real? and rational? are not.

CALLING SCHEME FROM C#
======================
    interpreter.Evaluator.Apply(object procedure, object[] arguments) -> object

Evaluator.Apply is the UNIVERSAL apply. It handles a core Closure, a
TreeIlClosure (by handing it to the Tree-IL evaluator), a Primitive (including
generic dispatch), a CaseLambdaProcedure, a GenericFunction and, last, any
IApplicable of your own. Anything else raises a wrong-type-arg naming the value.
There is no need to know which kind of procedure you were handed.

To get the procedure in the first place, look the name up in a module:

    Variable variable = interpreter.CurrentModule.Lookup(Symbol.Intern("string-upcase"));
    object procedure = variable.GetValue();
    object result = interpreter.Evaluator.Apply(
        procedure, new object[] { new MutableString("hello") });
    string text = result.ToString();          // "HELLO"

SchemeModule.Lookup searches the module's own bindings first and then walks its
use list breadth-first; it answers null when the name is unbound everywhere.
LookupLocal restricts the search to the module's own bindings.

TreeIlEvaluator.ApplyClosure(TreeIlClosure closure, object[] arguments) is the
direct route when you already know you hold a Tree-IL closure; Evaluator.Apply
routes there for you.

EXTENDING THE INTERPRETER FROM C#
=================================
DEFINING A PRIMITIVE
--------------------
    interpreter.DefinePrimitive("host-add", 2, 2,
        a => SchemeNumber.Add(a[0], a[1]));

The delegate receives the evaluated arguments and returns a Scheme value. Arity
is checked before the body runs; -1 as the maximum means variadic. The primitive
lands in the ROOT module, so every module sees it.

VALIDATE ARGUMENTS THE SCHEME WAY, NOT WITH A CAST. Guile validates every
primitive argument and raises a catchable wrong-type-arg naming the procedure and
the argument POSITION; Scheme code legitimately catches that key. A bare C# cast
performs the same check the .NET way, and the resulting InvalidCastException
escapes to the host where no Scheme catch can see it. Two layers keep that from
happening:

    public static class Primitives.TypeChecks
        static Symbol       AsSymbol(object value, string procedureName, int position)
        static SchemeChar   AsChar(object value, string procedureName, int position)
        static Keyword      AsKeyword(object value, string procedureName, int position)
        static MutableString AsMutableString(object value, string procedureName,
                                             int position)

    POSITIONS ARE ONE-BASED, as Guile's are.

    public static class Primitives.StringPrimitives
        static string Text(object value, string procedureName)
            Read-only text: accepts a MutableString, and also a symbol, character
            or keyword, exactly as Guile's text-accepting subrs do.

* TypeChecks raises the POSITIONED error and is what a primitive body should use
  instead of a cast. AsMutableString is for primitives that MUTATE their string
  argument; StringPrimitives.Text is for the read-only case.
* Primitive.Invoke carries a last-resort net: an InvalidCastException out of ANY
  primitive body -- including one a HOST registers through DefinePrimitive -- is
  translated to wrong-type-arg named for the primitive, unpositioned. A
  SchemeThrow from a nested primitive is not an InvalidCastException and passes
  through with its own attribution.

The net is the backstop, not the convention. Prefer the positioned accessor.

APPLICABLE, COMPARABLE AND PRINTABLE HOST OBJECTS
-------------------------------------------------
    public interface Values.IApplicable
        object Apply(object[] arguments)

    public interface Values.ISchemeEqual
        bool SchemeEquals(object other)

    public interface Values.ISchemePrintable
        string PrintRepresentation()

These are the managed equivalents of Guile's smob hooks. IApplicable lets a host
object of your own sit in operator position while still being its own type for
every predicate -- and procedure? accepts it -- WITHOUT deriving from Procedure.
IApplicable is consulted LAST in the apply path, so nothing built in is shadowed.

ISchemeEqual makes equal? compare your object BY VALUE. It is opt-in for a
reason: identity is the default in Guile too, and the hook is consulted only when
BOTH operands implement it, because equal? is symmetric and asking one side about
a value that cannot answer back is how an asymmetric comparison creeps in.

ISchemePrintable is a SEPARATE surface from ToString() on purpose: upstream smobs
carry both a bare-content form and a wrapped printed form, and code reads the two
through different routes. The printer calls it for both write and display.

The equality helpers behind all this are public if you need them directly:

    Primitives.CorePrimitives.Eq(object x, object y)
    Primitives.CorePrimitives.Eqv(object x, object y)
    Primitives.CorePrimitives.SchemeEqual(object x, object y)
    Values.ReferenceComparer.Instance          // IEqualityComparer<object>, eq?

GIVING HOST TYPES A GOOPS CLASS
-------------------------------
    Primitives.BuiltinClasses.ClassOfExtensionHook { get; set; }
        Func<object, SchemeClass>, consulted by class-of for a value none of the
        built-in classes covers. Return null to decline.

That is how a host type becomes dispatchable by define-method without the
library knowing about it.

EXTENDING A GENERIC-CAPABLE PRIMITIVE
-------------------------------------
    Primitives.PrimitiveGenerics.Enable(Primitive primitive) -> GenericFunction
    Primitives.PrimitiveGenerics.NoApplicableMethod(
        GenericFunction generic, string name, object[] arguments) -> SchemeThrow

This is what Scheme's enable-primitive-generic! does: it hangs a generic off the
PRIMITIVE OBJECT itself, so a method added from one module is visible from every
module that imports the core. See the pitfalls section -- getting this wrong is
invisible from the defining module.

DISPATCH ORDER IS GUILE'S (since 2026-08-28): arity first, then the PRIMITIVE, and
only when the primitive's own type check fails does the call fall over to the
attached generic -- SCM_WTA_DISPATCH_n. NoApplicableMethod BUILDS that goops-error
in Guile's exact shape and hands it back ready to raise, which is what a host
dispatching a generic of its own should raise rather than inventing a message. A method specialized on the primitive's own
domain (say <integer> on max) is therefore NEVER consulted for integers, and a
generic with no applicable method raises (goops-error #f "No applicable method
for ~S in call ~S" (GENERIC CALL) ()). The four arithmetic operators, the five
comparisons, max, min, gcd and lcm dispatch PAIRWISE, so the CALL in that error is
the pair that failed: (+ 1 2 "x") reports (+ 3 "x"). See pitfalls 49-51.

MODULES: SchemeModule, ModuleRegistry AND ModuleLoader
======================================================
    namespace CodeBrix.LilyScheme.Runtime;

    public sealed class ModuleRegistry
        SchemeModule RootModule { get; set; }
        Func<object, SchemeModule, bool> ModuleLoader { get; set; }
        SchemeModule Resolve(object name)      // creates + autoloads when absent
        void Register(SchemeModule module)
        int Count { get; }
        IEnumerable<SchemeModule> All { get; }

    public sealed class SchemeModule
        SchemeModule(object name)              // name is a Scheme list, e.g. (my app)
        object ModuleName { get; }
        object EnsureName(ModuleRegistry registry)
        IReadOnlyList<SchemeModule> Uses { get; }
        object Kind { get; set; }              // 'module by default
        Primitives.SchemeHashTable Submodules { get; set; }
        IReadOnlyDictionary<Symbol, Variable> Bindings { get; }
        ISet<Symbol> Exports { get; }
        SchemeModule PublicInterface { get; set; }
        SchemeModule InterfaceBacking { get; }
        void Export(Symbol name)
        SchemeModule Interface()
        SchemeModule LiveInterfaceView()
        void AddUse(SchemeModule module)
        void AddVariable(Symbol name, Variable variable)   // SHARES the cell
        bool Remove(Symbol name)
        Variable LookupLocal(Symbol name)
        Variable Lookup(Symbol name)
        Variable Define(Symbol name, object value)         // OWN cell
        void DefinePublic(Symbol name, object value)       // Define + Export
        Variable EnsureVariable(Symbol name)
        long GenerateUniqueId()

Define alone makes a PRIVATE binding under Guile's rules, so a module you provide
from C# must DefinePublic every name it is meant to export -- otherwise the
default (narrow) import delivers nothing and the names come out unbound. The
library's own shim modules use it for exactly that reason.

INSTALLING YOUR OWN MODULE LOADER
---------------------------------
ModuleRegistry.ModuleLoader is called from Resolve the first time a module name
is resolved, so (use-modules (my app)) can load your source. It answers true when
it loaded something; false is normal and simply leaves the module empty.

    Func<object, SchemeModule, bool> previous = interpreter.Modules.ModuleLoader;
    interpreter.Modules.ModuleLoader = (name, module) =>
    {
        string printed = Printer.Write(name);       // e.g. "(my app)"
        string source = MyResources.TryRead(printed);
        if (source == null)
        {
            return previous != null && previous(name, module);
        }

        SchemeModule saved = interpreter.CurrentModule;   // MANDATORY
        interpreter.CurrentModule = module;
        try
        {
            SchemeBootstrap.LoadExpanded(interpreter, source, printed);
        }
        finally
        {
            interpreter.CurrentModule = saved;
        }

        return true;
    };

THE SAVE/RESTORE IS NOT OPTIONAL, and it is the single most expensive mistake an
embedder can make here. Guile's autoloader is a save-module-excursion for exactly
this reason. The file being loaded opens with (define-module ...), which makes
ITS module current and never puts the old one back -- so an autoload triggered
from a use-modules line in the middle of another file redirects EVERY LATER
DEFINITION IN THAT FILE into the autoloaded module. The symptom is not an error:
the definitions are still found, because the outer module uses the inner one.
What breaks is SHADOWING -- methods specialised on host types get found only
after the root module's version has already answered.

CHAIN, do not replace: SchemeBootstrap.EnableModuleAutoload keeps whatever loader
is already installed and falls through to it, so installing yours before or after
LoadCore both work as long as you chain too.

THE THREE MODULE OPERATIONS THAT ARE NOT INTERCHANGEABLE
--------------------------------------------------------
* Define / module-define! takes a VALUE and gives the module a variable OF ITS
  OWN.
* AddVariable / module-add! takes a VARIABLE and installs THAT CELL as the
  binding, so two modules share one location and a set! through either is seen by
  both. Guile's own body errors on a non-variable third argument, and code relies
  on the sharing. Implementing module-add! as an alias for module-define!
  compiles, loads and passes every test that only READS the name -- and then
  hands readers the VARIABLE OBJECT as if it were the value.
* Remove / module-remove! drops a module's OWN binding for a name and nothing
  more; a name it was shadowing goes back to resolving through imports.

Modules also carry Guile's kind field (module-kind / set-module-kind!) and a
submodules table (module-submodules / set-module-submodules!), keyed by the last
name component and linked in when a child module is registered.

ANONYMOUS MODULES ARE NAMED LAZILY. module-name NAMES an anonymous module on
first ask -- a fresh generated name, under which the module is simultaneously
REGISTERED. That is load-bearing for macros: psyntax round-trips module identity
BY NAME inside hygiene wraps, so an imported macro used in a module that cannot
be named back does not resolve as a macro at all -- it reads as an ordinary
variable and its arguments get evaluated.

EXPORTS AND THE PUBLIC INTERFACE
--------------------------------
Every module carries the set of names a define-public, an `export' clause or a
#:export / #:re-export / #:export-syntax / #:replace keyword named, and
module-public-interface answers a module holding exactly those, bound to the SAME
variables. A plain `define' is not in it. The interface is built FRESH on every
ask rather than cached, because a module goes on growing after it is created.

A define-module clause keyword may be spelled #:export or as the keyword-like
SYMBOL :export -- boot-9 normalizes the latter, and so does define-module* here.

PORTS, OUTPUT REDIRECTION AND FLUSHING
======================================
Assign Interpreter.OutputWriter / ErrorWriter / InputReader to redirect a run.
They default to Console.Out / Console.Error / Console.In.

    StringWriter captured = new StringWriter();
    interpreter.OutputWriter = captured;

FLUSH WHEN THE RUN IS OVER. The file ports Scheme opens are BUFFERED, and Scheme
code is entitled not to close them -- Guile flushes every open port as the
PROCESS exits, and an embedded interpreter has no exit to hang that on. The bound
name for the host to call is flush-all-ports (force-output flushes one port).
Skip it and output files end at a buffer boundary; the tell is a set of files
whose sizes are all multiples of 1024.

close-port is Guile's any-port close, and for a FILE port it DISPOSES the writer
rather than merely flushing it. The current output and error ports are
deliberately NOT disposed by it -- those writers belong to you and must survive
being closed from Scheme.

OPENING FILES FROM SCHEME
-------------------------
open-input-file, call-with-input-file, call-with-port, get-string-all and
get-string-n are core-side rather than in a module, which is the standing posture
here for the PLACEMENT of these procedures: they are reachable without a
use-modules where Guile keeps them in ice-9/ports.scm. That is about where a name
lives, not about what a use-modules imports -- imports are Guile's by default,
see Interpreter.NarrowModuleImports and pitfall 20. call-with-input-file and open-input-file take Guile's #:binary /
#:encoding / #:guess-encoding keywords, and #:encoding is load-bearing rather
than decorative. open-output-file takes #:binary and #:encoding, and never writes
a byte-order mark.

open-file IS A DIFFERENT PROCEDURE, NOT A SPELLING OF THOSE. It takes a MODE
STRING -- "r", "w", "a", and the "b" flag that means one character per byte --
rather than keywords. The direction character selects an input or an output port;
"+" is REFUSED loudly, because a port here is a reader or a writer and never both.

file-port? asks whether a port's implementation is the FILE one, which is NOT the
same question as whether it has a name -- a string port carries the name <string>
in Guile too.

Every read behind open-file, open-input-file, call-with-input-file and load asks
the OS for FileShare.ReadWrite. That changes nothing on Linux or macOS, where the
share mode was never consulted; what it removes is the SAME Scheme program
throwing on Windows (which enforces share modes) and not on the others, when
something else holds the file open for writing.

WRITING BYTES
-------------
Scheme produces binary output the only way Scheme can: set an 8-bit codec on the
port and write one character per octet. set-port-encoding! is REAL here -- on an
open output file port it flushes the writer and reopens it APPENDING with the new
codec, no BOM, as Guile changes a live port's codec without discarding its file.

PRINTING: Printer
=================
    namespace CodeBrix.LilyScheme.Runtime;
    public static class Printer

    static string Write(object value)       // `write' conventions, strings quoted
    static string Display(object value)     // `display' conventions, unquoted
    static string WriteString(string value) // host text -> a Scheme string LITERAL
    static string Abbreviate(object value, int maximumLength)
    static void WriteThroughProgramLatch(
        object value, bool quoteStrings, Action<string> emit)
    static void ResetProgramPrintLatch()
    static bool ProgramPrintLatched { get; }

A HOST PATH REACHES SCHEME THROUGH Printer.WriteString
------------------------------------------------------
Splicing a filesystem path into source text is not string concatenation. The
reader implements Guile's FIXED-WIDTH hex escapes, so a Windows path spliced in
raw is not the path it names: C:\Users\me reaches \U, takes the next six
characters as hex digits, and fails on the 's' of Users.

The loud failure is the LUCKY case. A path component beginning with a, b, f, n,
r, t or v spells a VALID escape -- \temp is a tab -- so the source reads with no
diagnostic and names a DIFFERENT file. Which of the two you get depends on the
directory names, which is why this surfaces as an intermittent fault.

    string source = "(open-input-file " + Printer.WriteString(path) + ")";

WriteString emits the surrounding quotes and every escape, and round-trips
through the reader by contract. Hand-doubling backslashes at the call site is NOT
the same thing: it leaves a quote in a path unescaped. Nothing here is
Windows-only -- on Linux and macOS a path simply has no backslashes to escape.

THE PROGRAM-PRINT LATCH
-----------------------
Printer renders a procedure the way Guile's print-program does: "#<procedure"
then either the NAME or the object address in hex, then -- for an unnamed
procedure that knows where it came from -- " at file:line:column", then the
parameter list.

Guile's own printer sets a process-global re-entry flag while it calls out to the
Scheme printer, prints a low-level "#<program ADDR CODE>" form while the flag is
set, and NEVER RECOVERS if a non-local exit leaves it set -- which pretty-print's
truncating soft port routinely causes. That latch is reproduced here on purpose,
because it is observable in upstream output. Printer.ResetProgramPrintLatch is
for a host that runs many input files in one process: the faithful reset point is
the per-file boundary rather than process exit. Call it there, or every procedure
printed after the first abort comes out in the low-level form.

ERRORS AND EXCEPTIONS
=====================
CATCHING SCHEME ERRORS FROM C#
------------------------------
    namespace CodeBrix.LilyScheme.Runtime;

    public class SchemeThrow : Exception       // NOT sealed -- see below
        SchemeThrow(object key, object arguments)
        object Key { get; }                  // the throw key, a Symbol
        object Arguments { get; }            // the remaining throw arguments, a LIST
        object ExceptionObject { get; set; } // the modern-API object, when there is one

    public sealed class SchemeEvaluationException : Exception
        SchemeEvaluationException(string message)
        SchemeEvaluationException(string message, Exception innerException)

    public sealed class SchemeReaderException : SchemeThrow   // namespace ...Reader
        SchemeReaderException(string message, object arguments)
        string ReaderMessage { get; }        // the text, position prefix included

    public sealed class PromptAbort : Exception
        PromptAbort(object tag, object[] arguments)
        object Tag { get; }
        object[] Arguments { get; }

SchemeThrow is what every Scheme-level error reaches C# as. Its Message is
already rendered ("Scheme error: <key> <arguments>"), but the structured shape is
what you should branch on:

    try
    {
        interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
    }
    catch (SchemeThrow error)
    {
        Symbol key = error.Key as Symbol;                 // 'wrong-type-arg, ...
        List<object> arguments = Pair.ToList(error.Arguments);
        // Guile's scm-error shape is (SUBR MESSAGE ARGS DATA):
        //   arguments[0]  the procedure name as a Scheme string, or #f
        //   arguments[1]  a format string such as "Wrong type argument in position 1: ~S"
        //   arguments[2]  the format arguments as a list, or #f
        //   arguments[3]  extra data, usually #f
    }

The keys you will actually meet: wrong-type-arg, out-of-range, unbound-variable,
wrong-number-of-args, misc-error, system-error, regular-expression-syntax,
goops-error, and %exception for a raise-exception of a plain object.

SchemeEvaluationException is raised by SchemeBootstrap.LoadExpanded and LoadSource
when a top-level form fails: its message names the file, the zero-based form index
and the total, and the ORIGINAL failure is the InnerException. It does NOT derive
from SchemeThrow, so the two clauses are independent of each other in any order.

SchemeReaderException is a syntax error in the source text -- a read-error
condition, catchable from Scheme as well (pitfall 54). It comes out of
SchemeReader.ReadAll, and out of anything that reads (EvalString, LoadFile,
LoadExpanded), before the offending text is evaluated. Its ReaderMessage is the
text WITH the "NAME:LINE:COLUMN:" prefix and WITHOUT the throw wrapping, which is
what you want to show a user.

⚠ ORDER YOUR CATCH CLAUSES DERIVED-FIRST. SchemeReaderException DERIVES from
SchemeThrow, so a `catch (SchemeThrow)' clause catches syntax errors too. Inside
ONE try the compiler enforces the order for you (a SchemeThrow clause placed first
makes the SchemeReaderException clause unreachable, CS0160); ACROSS LAYERS nothing
does, so an outer `catch (SchemeThrow)' that reports "Scheme error" silently
absorbs what was really a syntax error. Write it either way round:

    try
    {
        SchemeBootstrap.LoadExpanded(interpreter, source, fileName);
    }
    catch (SchemeReaderException syntaxError)   // FIRST: the derived type
    {
        Report("Syntax error: " + syntaxError.ReaderMessage);
    }
    catch (SchemeThrow error)                   // then everything else Scheme threw
    {
        Report("Scheme error: " + error.Message);
    }
    catch (SchemeEvaluationException loadError)  // which form of which file
    {
        Report(loadError.Message);
    }

or, where one clause is all you want, branch on the key inside it -- a reader error
is exactly the key read-error:

    catch (SchemeThrow error)
    {
        bool isSyntaxError = (error.Key as Symbol)?.Name == "read-error";
    }

PromptAbort carries an abort-to-prompt out to the matching call-with-prompt.
Do NOT swallow it in a C# primitive that calls back into Scheme: prompts here are
ESCAPE-ONLY, and an abort that never reaches its prompt hangs the control flow.

THE MODERN EXCEPTION API, AND HOW IT MEETS catch/throw
------------------------------------------------------
Guile 3's exception objects are here: raise-exception, with-exception-handler
(#:unwind? and #:unwind-for-type included), exception?, make-exception and the
compound/simple object model, make-exception-type, exception-predicate,
exception-accessor (both reaching through compounds), exception-kind and
exception-args are core. (ice-9 exceptions) is vendored VERBATIM on top and
supplies the standard types (&message, &warning, &external-error,
&assertion-failure, ...), define-exception-type, raise-continuable, R7RS guard,
and the converter table that turns native throw keys into typed exception objects.

The interop is BOTH WAYS and is the load-bearing part: `catch' sees a raised
exception through its kind and args (a plain object raises with kind %exception),
and an exception handler sees a plain throw -- including every SchemeThrow a C#
primitive raises -- as the converted exception object, so
(guard (e ((assertion-failure? e) ...)) (car 5)) works.

A non-unwinding handler runs INSIDE a .NET exception filter, pre-unwind; its
non-local exits are carried out through the catch block and rethrown, because the
CLR silently swallows any exception escaping a filter.

RECORD TYPES CARRY THE EXCEPTION HIERARCHY
------------------------------------------
    namespace CodeBrix.LilyScheme.Values;

    public sealed class RecordType
        RecordType(string name, IReadOnlyList<object> fields)
        RecordType(string name, IReadOnlyList<object> ownFields, RecordType parent,
                   bool extensible, bool[] ownMutability)
        string Name { get; }
        IReadOnlyList<object> Fields { get; }     // COMPLETE layout, parent first
        RecordType Parent { get; }
        IReadOnlyList<RecordType> Ancestors { get; }
        bool Extensible { get; }
        int IndexOf(object field)
        bool IsFieldMutable(int index)
        bool HasParent(RecordType candidate)
        bool IsInstance(object value)             // subtype instances included

A RECORD INSTANCE IS AN object[] WHOSE SLOT 0 IS ITS RecordType, with the fields
following in layout order. That is also the struct view: struct-ref counts fields
from 0 with the type slot skipped. #:parent lays out the parent's fields FIRST,
a record-predicate accepts subtype instances, only an #:extensible? #t type may
be a parent, a field spec may be (immutable name) or (mutable name), and
record-type-name answers the SYMBOL. Records print as
#<type-name field: value ...>.

THE EXPANSION CACHE
===================
    namespace CodeBrix.LilyScheme.Caching;

    public sealed class ExpansionCache
        ExpansionCache()                      // empty, ready to record
        bool IsDirty { get; }
        bool IsReplay { get; }
        int FileCount { get; }
        bool TryGetFile(string fileName, string sourceHash,
                        out IReadOnlyList<object> forms)
        void RecordFile(string fileName, string sourceHash,
                        IReadOnlyList<object> forms)
        static string HashSource(string source)     // lower-case hex SHA-256

    public static class ExpansionCacheFile
        static void Write(ExpansionCache cache, Stream stream, string key)
        static ExpansionCache Read(Stream stream, string expectedKey)
        static void WriteFile(ExpansionCache cache, string path, string key)
        static ExpansionCache TryReadFile(string path, string expectedKey)  // null = miss

Measured over a full engine boot, ~99% of loading a Scheme layer is psyntax macro
expansion (the expander itself runs interpreted); evaluating the expanded Tree-IL
is milliseconds. The cache removes the expansion: assign an ExpansionCache to
Interpreter.ExpansionCache and SchemeBootstrap.LoadExpanded records each file's
expanded Tree-IL on first load and substitutes it on later loads, keyed per file
by name + source SHA-256. Everything is still EVALUATED live, in order -- nested
loads, module switches and load-time side effects behave exactly as an uncached
boot.

FOUR RULES, EACH OF WHICH HAS ALREADY DRAWN BLOOD:

* NEVER SHARE AN INSTANCE BETWEEN INTERPRETERS. Recorded quoted constants become
  live, MUTABLE runtime data when evaluated. Deserialize one instance per
  interpreter -- the file's BYTES may be memoized, the graphs may not.
* RECORDING RUNS psyntax IN c&e MODE, AND MUST NOT RE-EVALUATE. That is handled
  for you by LoadExpanded; the consequence you must not defeat is that a recorded
  boot rebuilds macros as well as values. If you write your own recorder over
  TreeIlEvaluator.Expand, pass compileAndEval: true and do NOT evaluate the form
  again -- the expander already did.
* IDENTITY IS PART OF THE FORMAT. The serializer keeps an object table, and every
  repeated heap object round-trips to ONE object, so gensym lookup by reference
  equality still works and uninterned symbols can never collide with live ones.
* A CACHE MUST NEVER BE ABLE TO FAIL OR FALSIFY A BOOT. The file carries the
  caller's world-signature key and a SHA-256 of the payload; any mismatch,
  truncation or corruption is a MISS (TryReadFile answers null) and the boot
  records live again. Unknown value types THROW at record time, so the boot keeps
  its live result and simply saves nothing.

THE KEY IS THE CALLER'S JOB. It must change whenever anything that shaped the
expansion changes -- your own assembly identity, this library's assembly identity,
and the content of every Scheme source that participates. Because the CodeBrix
family stamps a new assembly version on every build, any rebuild changes the
assembly MVIDs: the first boot after a rebuild re-records once and every boot
until the next rebuild replays.

GOOPS, STRUCTS AND ARRAYS FROM C#
=================================
    namespace CodeBrix.LilyScheme.Primitives;

    public sealed class SchemeClass
        SchemeClass(Symbol name, IReadOnlyList<SchemeClass> superclasses)
        Symbol ClassName { get; }
        IReadOnlyList<SchemeClass> Superclasses { get; }
        int Depth { get; }
        List<SlotDefinition> Slots { get; }
        List<SlotDefinition> AllSlots()
        bool IsSubclassOf(SchemeClass other)

    public sealed class SchemeObject
        SchemeObject(SchemeClass objectClass)
        SchemeClass ObjectClass { get; }
        Dictionary<Symbol, object> Slots { get; }

    public sealed class SlotDefinition
        SlotDefinition(Symbol name)
        Symbol SlotName { get; }
        object InitialValue { get; set; }
        Keyword InitKeyword { get; set; }
        object InitThunk { get; set; }

    public sealed class GenericFunction : Procedure
        List<GenericMethod> Methods { get; }
        object Fallback { get; set; }
        GenericMethod Select(object[] arguments)

    public sealed class GenericMethod
        GenericMethod(IReadOnlyList<SchemeClass> specializers, object implementation)
        IReadOnlyList<SchemeClass> Specializers { get; }
        object Implementation { get; }
        bool Accepts(object[] arguments, out int specificity)

    public static class BuiltinClasses
        static readonly SchemeClass Top, Object, Class, Number, Complex, Real,
            Integer, Fraction, List, Pair, Null, String, Symbol, Keyword, Char,
            Boolean, Vector, Procedure, HashTable, Port, Struct, Unknown
        static IReadOnlyDictionary<string, SchemeClass> All { get; }
        static SchemeClass ClassOf(object value)
        static Func<object, SchemeClass> ClassOfExtensionHook { get; set; }

STRUCTS AND TREE-IL
-------------------
    namespace CodeBrix.LilyScheme.Values;

    public sealed class StructVtable
        StructVtable(string name, params string[] fieldNames)
        string Name { get; }
        string[] FieldNames { get; }
        int FieldCount { get; }
        int IndexOf(string fieldName)

    public sealed class SchemeStruct
        SchemeStruct(StructVtable vtable, object[] fields)
        StructVtable Vtable { get; }
        object[] Fields { get; }
        object GetField(string fieldName)

    public static class ExpandedVtables
        const int Void, Const, PrimitiveRef, LexicalRef, LexicalSet, ModuleRef,
            ModuleSet, ToplevelRef, ToplevelSet, ToplevelDefine, Conditional,
            Call, Primcall, Seq, Lambda, LambdaCase, Let, Letrec, Count
        static StructVtable[] All { get; }
        static StructVtable Get(int index)
        static object[] BuildSchemeVector()
        static int IndexOf(StructVtable vtable)

    public sealed class SyntaxObject
        SyntaxObject(object expression, object wrap, object module, object sourceVector)
    public sealed class SyntaxTransformer
        SyntaxTransformer(object name, object type, object binding)

There are exactly EIGHTEEN Tree-IL node types, mirrored from Guile's expand.h.
psyntax constructs them POSITIONALLY, so field order is part of the contract.
A consumer normally never touches these -- they matter if you inspect expanded
code, or write your own cache over TreeIlEvaluator.Expand.

ARRAYS AND CHARACTER SETS
-------------------------
    public sealed class SchemeArray
        SchemeArray(int[] lowerBounds, int[] lengths, object[] storage)
        SchemeArray(int[] lowerBounds, int[] lengths, SchemeArray target, object mapper)
        int[] LowerBounds { get; }
        int[] Lengths { get; }
        object[] Storage { get; }
        SchemeArray Target { get; }        // non-null for a SHARED array view
        object Mapper { get; }
        int Rank { get; }
        bool IsShared { get; }
        int ElementCount { get; }
        int Offset(long[] indices)

    public sealed class CharSet
        CharSet(string name, Func<char, bool> contains)
        string Name { get; }
        bool Contains(char value)
        static readonly CharSet Empty, Full, Letter, Digit, LetterOrDigit,
            Whitespace, Punctuation, Graphic, Printing, LowerCase, UpperCase, Blank
        static CharSet Of(IEnumerable<char> characters)
        static CharSet Complement(CharSet set)
        static CharSet Union(IReadOnlyList<CharSet> sets)
        static CharSet Intersection(IReadOnlyList<CharSet> sets)
        static CharSet Difference(CharSet first, IReadOnlyList<CharSet> rest)

A VECTOR IS AN ARRAY. array?, array-ref, array-set!, array-rank and
array-dimensions all take a plain object[] as the rank-1, zero-based case -- no
conversion, and array-set! writes THROUGH to the vector.

UNICODE CHARACTER NAMES
=======================
    namespace CodeBrix.LilyScheme.Unicode;
    public static class UnicodeCharacterNames
        static string UnicodeVersion { get; }   // the UCD release the table came from
        static int Count { get; }
        static string Of(int codePoint)         // null when the character has no name
        static int Find(string name)            // -1 when the name is unknown

This is what (ice-9 unicode)'s char->formal-name and formal-name->char answer
from: Guile implements them over GNU libunistring, there is no managed
equivalent, so the table ships as an embedded resource derived from the Unicode
Character Database. Only rows carrying a LITERAL name are in it. A CJK ideograph
or a Hangul syllable answers #f rather than having its ALGORITHMIC name derived
-- that is Guile's measured behaviour, and Python's unicodedata does the
opposite. The table is version-dependent; UnicodeVersion tells you which release
it was built from.

THE REST OF THE PUBLIC SURFACE
==============================
Everything below is public, and a normal embedding never calls it -- the
Interpreter constructor and SchemeBootstrap.LoadCore do. It is listed so you can
recognise a type you meet in a stack trace or a debugger, and so you know which
file to read when you want to know exactly what a Scheme name does.

THE PRIMITIVE INSTALLERS (namespace CodeBrix.LilyScheme.Primitives)
-------------------------------------------------------------------
Each is a static class with a `static void Install(Interpreter interpreter)' that
registers one family of Scheme names, and the Interpreter constructor calls all
of them, in this order:

    CorePrimitives          eq?/eqv?/equal?, the list family, symbols, control,
                            and boot-9's ordinary procedures (identity, const,
                            and=>, ->bool and neighbours). Also exposes
                            Eq(object, object), Eqv(object, object) and
                            SchemeEqual(object, object).
    NumericPrimitives       the arithmetic and numeric-predicate surface
    StringPrimitives        strings, SRFI-13 and SRFI-14. Also exposes
                            Text(object value, string procedureName)
    VectorPrimitives        vectors and hash tables (SchemeHashTable lives here)
    ArrayPrimitives         Guile arrays, shared-array views, transpose
    ControlPrimitives       eval, eval-string, values, dynamic-wind, prompts,
                            catch/throw. Also exposes
                            EvalAny(Interpreter, object expression, SchemeModule)
    ModulePrimitives        the module procedures and the object-property table
    PortPrimitives          ports, file opening, the load path
                            (SchemeInputPort / SchemeOutputPort live here)
    GoopsPrimitives         GOOPS: classes, instances, generics, methods
    GuileCorePrimitives     the wider Guile core, including setters, regexps and
                            the directory family (DirectoryStream lives here)
    PosixPrimitives         system, system*, stat, broken-down time, wait status
    ExceptionPrimitives     the modern exception API
    PrimitiveGenerics       LAST, because it MARKS the primitive objects installed
                            above rather than defining them: generic-capability?
                            is a property of the subr

Two more install SHIM MODULES rather than core names, and SchemeBootstrap.LoadCore
calls them: SoftPortPrimitives.InstallShim(Interpreter) and
UnicodePrimitives.InstallShim(Interpreter). BuiltinClasses.Install(Interpreter)
registers the built-in GOOPS class objects.

REMAINING RUNTIME TYPES
-----------------------
    Primitives.SoftPortWriter : TextWriter
        SoftPortWriter(Interpreter interpreter, object writeString, object close)
        long Line { get; }        long Column { get; }
        void InvokeClose()
        The TextWriter behind a soft output port: it forwards text to a Scheme
        write-string procedure and tracks the port's line and column. See the
        buffering pitfall -- the buffering is observable.

    Runtime.LexicalEnvironment
        LexicalEnvironment(LexicalEnvironment parent, int capacity)
        LexicalEnvironment Parent { get; }
        Variable Define(Symbol name, object value)
        Variable Lookup(Symbol name)
        A lambda's binding frame. Both evaluators take one as their `environment'
        argument, and null means top level.

    Runtime.CurriedDefinitions
        static object Expand(object form)
        The (define ((f a) b) ...) rewrite, run over every form before psyntax
        sees it. It returns the ORIGINAL object when nothing changed, so source
        locations survive.

    Runtime.LoadDiagnostics
        The load-timing counters -- see PERFORMANCE TIPS.

    Primitives.ColumnTrackingWriter : TextWriter
        ColumnTrackingWriter(TextWriter inner)
        TextWriter Inner { get; }
        long Line { get; set; }   long Column { get; set; }
        static TextWriter Wrap(TextWriter writer)     // no-op if already tracking
        static TextWriter Unwrap(TextWriter writer)   // the sink underneath
        The writer that makes port-line and port-column answer for an OUTPUT port.
        Every write through it forwards to Inner and advances the position by the
        same rules PortPosition applies. Interpreter.TrackedOutputWriter() and
        SchemeOutputPort.Writer both produce one; SchemeOutputPort.InnerWriter is
        Unwrap. You will meet it in a stack trace and in a debugger watch on a
        writer you thought you had assigned directly.

    Reader.PortPosition
        static void Advance(ReadOnlySpan<char> text, ref long line, ref long column)
        static void Advance(char value, ref long line, ref long column)
        static void Retreat(char value, ref long line, ref long column)
        The one place the position rules live, shared by every port and by the
        reader, so all of them agree: a newline advances the line and zeroes the
        column, a carriage return zeroes the column without advancing the line, a
        tab advances to the next multiple of eight, a backspace retreats but never
        below zero, and a column counts CODE POINTS. See pitfall 53.

WHAT THE SCHEME LAYER GIVES YOU
===============================
After SchemeBootstrap.LoadCore, code you load through LoadExpanded can use:

* THE CORE LANGUAGE via psyntax: syntax-case, syntax-rules, define-syntax,
  quasisyntax (#` templates), hygienic macros, and the prelude's derived syntax
  -- and, or, cond, case, when, unless, do, let-values, define-values, receive,
  and-let*, while, parameterize, cond-expand, defmacro, define-module,
  use-modules, define-public, and (ice-9 optargs)'s let-keywords / let-keywords*.
* THE MODULE SYSTEM, with autoloading of the vendored modules, #:select
  (including the renaming (original . local) form), #:export / #:re-export /
  #:replace, public interfaces and submodules.
* GOOPS: define-class, define-method, define-generic, slot options including
  #:init-value / #:init-thunk / #:accessor / #:getter / #:setter, and extension
  of generic-capable primitives.
* VENDORED GUILE MODULES, autoloaded by name the first time a use-modules
  clause names them: (srfi srfi-1), (srfi srfi-2), (srfi srfi-8), (srfi srfi-11),
  (srfi srfi-39), (srfi srfi-43), (rnrs bytevectors), (ice-9 match),
  (ice-9 format), (ice-9 regex), (ice-9 rdelim), (ice-9 getopt-long),
  (ice-9 pretty-print), (ice-9 exceptions), (ice-9 common-list), (ice-9 list),
  (ice-9 string-fun), (ice-9 receive), (ice-9 session), (ice-9 control).
  Autoloading maps the LAST name component onto a vendored file name, so a module
  with no vendored file behind it simply resolves EMPTY -- and every name it
  would have supplied comes out unbound, which reads as dozens of unrelated
  failures rather than one missing module.
* SHIM MODULES provided from C#: (system vm program) (whose program? answers #f
  for everything, because there is no VM), (ice-9 iconv) (string->bytevector /
  bytevector->string over .NET's encodings), (ice-9 soft-ports) (keyword-form
  make-soft-port building OUTPUT ports over a Scheme write-string procedure),
  (ice-9 popen) (open-pipe, open-pipe*, open-input-pipe, open-output-pipe,
  close-pipe over System.Diagnostics.Process), and (ice-9 unicode).
* PORTS: string ports, file ports, soft output ports, (current-input-port) over
  Interpreter.InputReader, #:encoding-aware readers, binary output through
  set-port-encoding!, and the escape-only call-with-prompt / abort-to-prompt /
  make-prompt-tag protocol.
* A POSIX SURFACE: system and system* with the status:exit-val / status:term-sig
  / status:stop-sig decoders; stat and lstat building Guile's 18-slot vector with
  the accessors from the vendored ice-9/posix.scm; localtime, gmtime and strftime
  over Guile's 11-slot tm vector; opendir / readdir / closedir, mkdir, rmdir and
  delete-file.
* POSIX REGULAR EXPRESSIONS: make-regexp with flag INTEGERS as separate rest
  arguments, regexp-exec answering Guile's match VECTOR (slot 0 the target
  string, slot i+1 the (start . end) pair of group i, (-1 . -1) for a group that
  did not participate -- so match:substring answers #f, never an empty string,
  for an unmatched group), and the vendored ice-9/regex.scm's string-match,
  fold-matches, regexp-substitute/global and match: accessors on top.
* THE MODERN EXCEPTION API, interoperating with catch/throw in both directions.
* THE NUMERIC TOWER including complex literals, make-polar, make-rectangular,
  magnitude, angle, real-part and imag-part (the last four accept a REAL too).
* SORTING: sort and stable-sort are a MERGE sort, so a Scheme predicate that is
  not a strict weak ordering is tolerated rather than throwing, and the sort is
  stable.

LICENSING AND REDISTRIBUTION
============================
This package is LGPL-3.0-or-later, because it incorporates source from the GNU
Guile project, which is itself LGPL-3.0-or-later. Twenty-nine .scm files from the
GNU Guile source tree are vendored VERBATIM and shipped as embedded resources.

READ THIS BEFORE TAKING THE DEPENDENCY. LGPL-3.0-or-later is a copyleft license:
it permits linking from a differently-licensed application, but it attaches
conditions -- notably that the recipient must be able to relink your application
against a modified version of this library, and that the license texts and
notices travel with what you ship. If your project cannot accept those
conditions, do not reference this package.

The .nupkg carries LICENSE (the full LGPL-3 text), LICENSE.GPL (the full GPL-3
text, which LGPL-3 incorporates by reference, so a LICENSE carrying only the LGPL
text would be incomplete), and THIRD-PARTY-NOTICES.txt -- the per-file
attribution ledger, which records seven additional copyright holders beyond the
Free Software Foundation, one public-domain file, and one file under an MIT-style
grant of its own rather than the LGPL. Ship all of them onward.

The C# in this library is new-in-family: written against R7RS, the SRFI documents
and Guile's published interfaces, not translated from Guile's C.

COMPLETE EXAMPLES
=================
All of these assume `using System;', `using System.Collections.Generic;',
`using System.IO;', the usings from KEY NAMESPACES above, and this helper, which
is the shape every embedding takes -- one interpreter, bootstrapped once, driven
inside RunWithLargeStack:

    private static object EvalOne(Interpreter interpreter, string source)
    {
        object result = Values.Unspecified.Instance;
        foreach (object form in SchemeReader.ReadAll(source, "<host>"))
        {
            result = interpreter.TreeIlEvaluator.ExpandAndEval(
                form, interpreter.CurrentModule);
        }

        return result;
    }

1. EVALUATE A STRING AND READ THE RESULT BACK
---------------------------------------------
    Interpreter interpreter = new Interpreter();
    Interpreter.RunWithLargeStack(() =>
    {
        SchemeBootstrap.LoadCore(interpreter);

        // A number. Exact integers arrive as long, widening to BigInteger.
        object number = EvalOne(interpreter, "(* 6 7)");
        long answer = (long)number;                          // 42
        double asDouble = SchemeNumber.ToDouble(number);     // 42.0

        // A string. Scheme strings are MutableString, not System.String.
        object text = EvalOne(interpreter, "(string-append \"Lily\" \"Scheme\")");
        string hostText = ((MutableString)text).ToString();  // "LilyScheme"

        // A list. Walk it with Pair.ToList; the two-argument overload also gives
        // you the improper tail, which is the only safe way to walk a list you
        // did not build yourself.
        object list = EvalOne(interpreter, "(map (lambda (n) (* n n)) '(1 2 3))");
        List<object> items = Pair.ToList(list, out object tail);
        // items = [1L, 4L, 9L]; tail is Nil.Instance for a proper list.

        // A boolean, and the true-ness rule: everything except #f is true.
        bool flag = Evaluator.IsTrue(EvalOne(interpreter, "(> 3 2)"));

        // Anything at all, rendered the way Scheme would print it.
        Console.WriteLine(Printer.Write(list));              // (1 4 9)
        Console.WriteLine(Printer.Display(text));            // LilyScheme
    });

2. DEFINE A HOST PRIMITIVE AND CALL IT FROM SCHEME
--------------------------------------------------
    Interpreter interpreter = new Interpreter();
    Interpreter.RunWithLargeStack(() =>
    {
        SchemeBootstrap.LoadCore(interpreter);

        // Two required arguments, no more: (host-join symbol string) -> string.
        // TypeChecks and StringPrimitives.Text raise Guile's catchable
        // wrong-type-arg instead of letting an InvalidCastException escape.
        interpreter.DefinePrimitive("host-join", 2, 2, a =>
        {
            Symbol prefix = TypeChecks.AsSymbol(a[0], "host-join", 1);
            string suffix = StringPrimitives.Text(a[1], "host-join");
            return new MutableString(prefix.Name + ":" + suffix);
        });

        // Variadic: -1 as the maximum.
        interpreter.DefinePrimitive("host-sum", 0, -1, a =>
        {
            object total = 0L;
            foreach (object argument in a)
            {
                total = SchemeNumber.Add(total, argument);
            }

            return total;
        });

        // A non-procedure binding.
        interpreter.DefineValue("host-version-name", new MutableString("demo"));

        Console.WriteLine(Printer.Write(
            EvalOne(interpreter, "(host-join 'file \"score.ly\")")));   // "file:score.ly"
        Console.WriteLine(Printer.Write(
            EvalOne(interpreter, "(host-sum 1 2 3 4)")));               // 10

        // And the type failure is catchable from Scheme, as Guile's is:
        Console.WriteLine(Printer.Write(EvalOne(interpreter,
            "(catch 'wrong-type-arg"
            + " (lambda () (host-join 5 \"x\"))"
            + " (lambda (key . args) 'caught))")));                     // caught
    });

3. CALL A SCHEME PROCEDURE FROM C#
----------------------------------
    Interpreter interpreter = new Interpreter();
    Interpreter.RunWithLargeStack(() =>
    {
        SchemeBootstrap.LoadCore(interpreter);
        EvalOne(interpreter,
            "(define (scale factor items)"
            + "  (map (lambda (n) (* n factor)) items))");

        Variable variable = interpreter.CurrentModule.Lookup(Symbol.Intern("scale"));
        if (variable == null || !variable.IsBound)
        {
            throw new InvalidOperationException("scale is not defined");
        }

        object scale = variable.GetValue();
        object arguments = Pair.List(1L, 2L, 3L);
        object scaled = interpreter.Evaluator.Apply(
            scale, new object[] { 10L, arguments });

        Console.WriteLine(Printer.Write(scaled));       // (10 20 30)
    });

Evaluator.Apply is the universal apply -- it does not matter whether the value
you looked up is a primitive, a core closure, a psyntax-expanded closure, a
case-lambda, a generic function or an IApplicable of your own.

4. INSTALL A MODULE LOADER SO use-modules FINDS YOUR OWN SCHEME
---------------------------------------------------------------
    private static void InstallLoader(
        Interpreter interpreter, IReadOnlyDictionary<string, string> sources)
    {
        Func<object, SchemeModule, bool> previous = interpreter.Modules.ModuleLoader;
        interpreter.Modules.ModuleLoader = (name, module) =>
        {
            string printed = Printer.Write(name);       // "(my app)"
            if (!sources.TryGetValue(printed, out string source))
            {
                return previous != null && previous(name, module);
            }

            SchemeModule saved = interpreter.CurrentModule;
            interpreter.CurrentModule = module;
            try
            {
                SchemeBootstrap.LoadExpanded(interpreter, source, printed + ".scm");
            }
            finally
            {
                interpreter.CurrentModule = saved;
            }

            return true;
        };
    }

    // ...
    Interpreter interpreter = new Interpreter();
    Interpreter.RunWithLargeStack(() =>
    {
        SchemeBootstrap.LoadCore(interpreter);          // enables vendored autoload
        InstallLoader(interpreter, new Dictionary<string, string>
        {
            ["(my app)"] =
                "(define-module (my app) #:export (greet))\n"
                + "(define (greet who) (string-append \"hello, \" who))\n",
        });

        Console.WriteLine(Printer.Write(EvalOne(interpreter,
            "(begin (use-modules (my app)) (greet \"world\"))")));   // "hello, world"
    });

Install AFTER LoadCore and CHAIN to the previous loader (as above), or install
before it -- EnableModuleAutoload chains too, so either order works. The
save/restore of CurrentModule is mandatory; see MODULES above for what happens
without it.

5. WIRE THE EXPANSION CACHE
---------------------------
    private static Interpreter BootCached(string cachePath, string worldKey,
                                          IReadOnlyList<string> sources)
    {
        Interpreter interpreter = new Interpreter();
        Interpreter.RunWithLargeStack(() =>
        {
            // A MISS -- absent file, wrong key, truncation, corruption -- answers
            // null, and the boot simply records live instead.
            ExpansionCache cache = ExpansionCacheFile.TryReadFile(cachePath, worldKey)
                                   ?? new ExpansionCache();
            interpreter.ExpansionCache = cache;

            SchemeBootstrap.LoadCore(interpreter);
            for (int i = 0; i < sources.Count; i++)
            {
                SchemeBootstrap.LoadExpanded(
                    interpreter, sources[i], "layer-" + i + ".scm");
            }

            if (cache.IsDirty)
            {
                ExpansionCacheFile.WriteFile(cache, cachePath, worldKey);
            }
        });

        return interpreter;
    }

worldKey must change whenever anything that shaped the expansion changes. A
workable key combines your assembly's MVID, this library's assembly MVID, and a
hash of every Scheme source that participates:

    string worldKey = string.Join(
        "|",
        typeof(MyHost).Assembly.ManifestModule.ModuleVersionId,
        typeof(Interpreter).Assembly.ManifestModule.ModuleVersionId,
        ExpansionCache.HashSource(string.Concat(sources)));

NEVER hand one ExpansionCache instance to two interpreters. Read the file once
if you like, but deserialize a fresh instance per interpreter.

6. REDIRECT OUTPUT, AND FLUSH WHEN THE RUN IS OVER
--------------------------------------------------
    Interpreter interpreter = new Interpreter();
    StringWriter output = new StringWriter();
    StringWriter errors = new StringWriter();

    Interpreter.RunWithLargeStack(() =>
    {
        interpreter.OutputWriter = output;
        interpreter.ErrorWriter = errors;
        interpreter.InputReader = new StringReader("(1 2 3)\n");

        SchemeBootstrap.LoadCore(interpreter);
        EvalOne(interpreter, "(display (read)) (newline)");

        // Scheme is entitled to leave file ports open; nothing flushes them for
        // you, because an embedded interpreter has no process exit to hang that
        // on. Do this at the end of every run that may have written files.
        EvalOne(interpreter, "(flush-all-ports)");
        Printer.ResetProgramPrintLatch();     // per input file, if you run many
    });

    Console.WriteLine(output.ToString());     // (1 2 3)

7. TAKE OVER A READER DISPATCH CHARACTER
----------------------------------------
    // '#[' now reads as a list of the following data up to ']'.
    SchemeReader.RegisterHashExtension('[', reader =>
    {
        List<object> items = new List<object>();
        while (true)
        {
            char next = reader.PeekCharacter();
            if (next == ']')
            {
                reader.ReadCharacterRaw();
                return Pair.ListFrom(items);
            }

            items.Add(reader.ReadDatum());
        }
    });

Registration is PROCESS-WIDE and takes precedence over the built-in syntax for
that character. SchemeBootstrap.LoadCore suspends every extension while it reads
Guile's own source and restores them afterwards, so registering before the
bootstrap is safe.

MINIMUM VIABLE PROJECT
======================
MyHost.csproj

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>MyHost</RootNamespace>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.LilyScheme.LgplLicenseForever" />
      </ItemGroup>
    </Project>

Program.cs

    using System;
    using CodeBrix.LilyScheme;
    using CodeBrix.LilyScheme.Reader;
    using CodeBrix.LilyScheme.Runtime;
    using CodeBrix.LilyScheme.Values;

    namespace MyHost;

    public static class Program
    {
        public static int Main(string[] arguments)
        {
            string source = arguments.Length > 0
                ? arguments[0]
                : "(begin (use-modules (srfi srfi-1)) (fold + 0 (iota 10)))";

            int exitCode = 0;
            Interpreter interpreter = new Interpreter();
            Interpreter.RunWithLargeStack(() =>
            {
                try
                {
                    SchemeBootstrap.LoadCore(interpreter);
                    object result = Unspecified.Instance;
                    foreach (object form in SchemeReader.ReadAll(source, "<argv>"))
                    {
                        result = interpreter.TreeIlEvaluator.ExpandAndEval(
                            form, interpreter.CurrentModule);
                    }

                    Console.WriteLine(Printer.Write(result));   // 45
                }
                catch (SchemeThrow error)
                {
                    Console.Error.WriteLine(
                        "Scheme error " + Printer.Write(error.Key)
                        + ": " + Printer.Write(error.Arguments));
                    exitCode = 1;
                }
                finally
                {
                    interpreter.EvalString("(flush-all-ports)", "<shutdown>");
                }
            });

            return exitCode;
        }
    }

Add the package version your repository pins; the id is
CodeBrix.LilyScheme.LgplLicenseForever.

PERFORMANCE TIPS
================
* THE EXPANSION CACHE IS THE ONE BIG LEVER. Macro expansion is, measured, ~99% of
  the cost of loading a Scheme layer, because psyntax itself runs interpreted.
  Recording once and replaying takes a cold boot of a large layer from roughly
  half a minute to milliseconds. Wire it as in example 5 for any host that loads
  the same Scheme repeatedly.
* LOAD ONCE, EVALUATE MANY. Constructing an Interpreter is cheap; LoadCore is
  not. Keep the interpreter alive across requests rather than rebuilding it.
* USE ONE BIG-STACK THREAD FOR A WHOLE RUN, not one per evaluation.
  RunWithLargeStack starts and joins a thread; nesting it around every tiny call
  pays that cost repeatedly for no benefit.
* PREFER Evaluator.Apply OVER BUILDING SOURCE TEXT. Calling a looked-up procedure
  with an object[] skips the reader and the expander entirely. Building a string
  and evaluating it pays for both, every time.
* KEEP HOST PRIMITIVES OUT OF THE ARGUMENT-CONVERSION BUSINESS. StringPrimitives
  .Text and the TypeChecks accessors are cheap; converting a whole list to
  System.String per call is not.
* MEASURE WITH LoadDiagnostics. Runtime.LoadDiagnostics accumulates ReadTime,
  ExpandTime and PlainEvalTime plus ExpandedFormCount and
  ExpandedFormCountOnThisThread, and Reset() zeroes them. Accumulation is two
  timestamps per top-level form and is always on. The process-wide counters
  answer "how much expansion has this process done"; the per-thread count is the
  honest measure of "did THIS load expand anything", because another thread
  expanding concurrently adds to the process-wide one.
* A rank-1 array IS an object[]; array-ref on a vector does not convert or copy.
* sort and stable-sort are a merge sort. That is a deliberate choice -- .NET's
  introsort VALIDATES its comparer and throws on a Scheme predicate that is not a
  strict weak ordering -- and it is stable, but it is not the fastest possible
  sort for a well-behaved predicate.

COMMON PITFALLS TO AVOID
========================
Every item below is a MEASURED sharp edge -- something that has already cost
somebody a debugging session here, or a recorded divergence from Guile that a
consumer can see. Almost all of them fail SILENTLY or at a distance, which is
why they are worth reading before you write code rather than after.

EMBEDDING
---------
1.  EVALUATING WITH THE WRONG EVALUATOR. Interpreter.LoadFile and
    LoadFileWithProgress run the CORE evaluator and do not expand macros: they are
    the boot paths that load psyntax itself. Since 2026-08-28 Interpreter.Eval and
    EvalString (and the Scheme `eval' / `eval-string') EXPAND once psyntax is
    loaded, as Guile's do, and fall back to the core evaluator only before that --
    the `(markup ...)' from (lily) through EvalString that used to fail as "Wrong
    type to apply: #<syntax-transformer markup>" now works (EvalExpansionTests).
    For a file, use SchemeBootstrap.LoadExpanded. A macro that still fails "where
    it is USED, not where it is defined" means a core-evaluator path was taken.

2.  NOT RUNNING ON A BIG STACK. psyntax overflows the CLR's default 1 MB stack
    while expanding. Wrap the work in Interpreter.RunWithLargeStack. The stack
    limit is per THREAD, so the thread is the fix; a failure on it reaches you as
    itself, with its original stack trace, so do not add a wrapper of your own --
    one used to live in that method and it hid every real message behind a
    generic one.

3.  A BARE CAST IN A HOST PRIMITIVE. Guile raises a catchable wrong-type-arg for
    a wrong-typed argument; a C# cast raises InvalidCastException, which no
    Scheme catch can see. Use TypeChecks.AsSymbol / AsChar / AsKeyword /
    AsMutableString (positioned) or StringPrimitives.Text. Primitive.Invoke
    translates a stray InvalidCastException as a backstop, unpositioned -- treat
    that as the net, not the convention.

4.  A MODULE LOADER THAT DOES NOT SAVE CurrentModule. The loaded file's
    (define-module ...) makes ITS module current and never restores it, so every
    later definition in the file that triggered the autoload lands in the wrong
    module. Nothing errors -- lookups still succeed through the use list. What
    breaks is SHADOWING, which surfaces much later as the wrong method winning.

5.  SPLICING A FILESYSTEM PATH INTO SOURCE TEXT. Go through Printer.WriteString.
    A raw Windows path is not the path it names, and the LOUD failure is the
    lucky case: a component beginning with a, b, f, n, r, t or v spells a valid
    escape and silently names a different file.

6.  SHARING AN ExpansionCache BETWEEN INTERPRETERS. Recorded quoted constants
    become live, MUTABLE data when evaluated. One deserialized instance per
    interpreter.

7.  FORGETTING TO FLUSH. File ports are buffered and nothing flushes them at
    process exit here. Call flush-all-ports at the end of a run. The tell is a
    set of output files whose sizes are all multiples of 1024.

8.  FORGETTING Printer.ResetProgramPrintLatch BETWEEN INPUT FILES. Guile's
    program-print re-entry latch is reproduced faithfully, including the fact
    that it never recovers on its own: once a non-local exit leaves it set, every
    procedure in the process prints in the low-level #<program ADDR CODE> form.

9.  REGISTERING A READER HASH EXTENSION AND THEN BOOTSTRAPPING BY HAND.
    Registration is PROCESS-WIDE. psyntax-pp.scm itself contains extended symbols
    such as #{ $sc-ellipsis }#, so LoadCore suspends every extension while it
    reads Guile's own source. A hand-rolled bootstrap that skips
    SuspendHashExtensions corrupts the expander with no error.

10. SWALLOWING PromptAbort IN A HOST PRIMITIVE THAT CALLS BACK INTO SCHEME.
    Prompts here are escape-only; an abort that never reaches its
    call-with-prompt breaks the control flow.

11. ASSUMING AN EXACT INTEGER IS A long. It is a long when it fits and a
    BigInteger when it does not, and primitives may hand you an int. Test with
    SchemeNumber.IsInteger / IsNumber and convert with ToBigInteger / ToDouble.

12. TREATING A SCHEME STRING AS System.String. It is a MutableString, because
    string-set! has to work. ToString() to read, new MutableString(text) to write.
    Read-only text primitives also accept symbols, characters and keywords.

13. CONSTRUCTING A Symbol INSTEAD OF INTERNING ONE. Symbols compare by reference;
    always use Symbol.Intern. Symbol.Generate makes an UNINTERNED gensym that can
    never collide.

14. MISTAKING A Variable FOR A VALUE. Module lookups answer the BINDING CELL.
    Call GetValue(). Handing a Variable to Scheme where a value was expected is
    what a module-add!-as-module-define! confusion produces.

MODULES
-------
15. module-define! / module-add! / module-remove! ARE NOT INTERCHANGEABLE.
    Define takes a VALUE and gives the module its own cell; AddVariable takes a
    VARIABLE and SHARES the cell, so a set! through either side is seen by both;
    Remove drops the module's own binding only, and a name it was shadowing goes
    back to resolving through imports.

16. EXTENDING A GENERIC-CAPABLE PRIMITIVE IS GLOBAL; DEFINING A FRESH GENERIC IS
    NOT. define-method on a name that already holds a generic-capable primitive
    hangs the method off the PRIMITIVE OBJECT, so every module that imports the
    core sees it. On a fresh name it defines a generic in the current module and
    nothing outside sees it. Getting this wrong is invisible from the defining
    module: everything there passes, and every OTHER module resolves the raw
    primitive and throws wrong-type-arg. Do not reach for a module-ordering fix;
    reordering imports does not make an extension global.

17. #:accessor CARRIES A SETTER. A GOOPS #:accessor makes an <accessor> -- a
    generic whose setter is a generic -- so (set! (acc obj) v) works. A bare
    lambda reads identically everywhere the accessor is only CALLED, and then
    throws wrong-type-arg on `setter' the first time something assigns through it.

18. use-modules #:select HAS TWO HALVES, AND BOTH BITE. An element is either a
    bare symbol or a pair (original . local); the RENAMED form binds a name that
    exists nowhere else. The RESTRICTION is honoured too -- a #:select clause
    builds an interface holding only the selected bindings, so an unselected name
    does not arrive.

19. WHEN TWO IMPORTS BIND ONE NAME, THE FIRST IMPORT WINS. This is a MEASURED
    DIVERGENCE that is deliberately kept: Guile's duplicate-binding handlers
    resolve toward the LAST module used and honour #:replace. The practical cost
    is confined to names a module #:replace's over the core -- (srfi srfi-43)'s
    vector-copy, vector->list and list->vector resolve to the CORE bindings for
    importers. The core vector-copy takes [start [end]], so the common arities
    agree; only srfi-43's fourth (fill) argument is out of reach. A module's OWN
    binding beats every import.

20. THE IMPORT IS GUILE'S BY DEFAULT, AND THE WIDE IMPORT IS A CHOICE. Since
    2026-08-28 Interpreter.NarrowModuleImports defaults to TRUE: a use-modules
    without #:select imports the module's public interface only. Set it FALSE --
    BEFORE the code it governs is loaded -- to put the WHOLE module on the use
    list, private names included, which is what CodeBrix.LilyPort does explicitly
    until its corpus is swept under the narrow default. //was previously: false
    by default (the wide import), true as the opt-in Guile-exact position. The
    wide import HID a real defect for the project's whole life: define-module
    clause keywords spelled as keyword-like SYMBOLS (`:export', srfi-1's spelling)
    were silently skipped, so srfi-1's export list went unrecorded -- found only
    when the narrow import was first tried.

21. AN ANONYMOUS MODULE IS NAMED LAZILY, AND THAT IS LOAD-BEARING. psyntax
    round-trips module identity BY NAME inside hygiene wraps. A macro imported
    into a module that cannot be named back does not resolve as a macro at all --
    it reads as an ordinary variable and its arguments get evaluated.

22. A NAME FOUND ONLY IN THE VENDORED boot-9.scm IS UNBOUND. That file is
    vendored but NEVER LOADED -- it builds Guile's module system from scratch on
    low-level vtable layouts and asserts (current-module) is #f as it starts.
    Treat "it is in boot-9.scm" as saying nothing about whether a name exists.
    Check with (defined? 'name), not by grepping vendored source. The same
    applies to ice-9/ports.scm and ice-9/textual-ports.scm, whose procedures are
    implemented core-side here instead.

23. A MODULE WITH NO VENDORED FILE BEHIND IT RESOLVES EMPTY. Autoloading maps the
    last name component onto a file name; a miss is not an error, and every name
    the module would have supplied comes out unbound -- which reads as dozens of
    unrelated failures rather than one missing module.

WRITING SCHEME AGAINST THIS IMPLEMENTATION
------------------------------------------
24. (eval form module) MAKES ITS MODULE ARGUMENT CURRENT for the whole call, as
    Guile's does. psyntax resolves free identifiers AT EXPANSION TIME against
    (current-module), so expanding in one module while evaluating in another
    yields references bound to the wrong namespace. The tell is two mechanisms
    disagreeing about one module -- module-defined? answering #t for a name that
    eval then cannot find.

25. A DOCSTRING ON A MACRO GOES ON THE TRANSFORMER, not on the user's body
    lambda. Invisible to every ordinary use of the macro, and visible to exactly
    one reader: a documentation generator asking procedure-documentation of
    (macro-transformer m).

26. A LONE STRING BODY IS A RETURN VALUE, NOT A DOCSTRING. A string as the first
    of SEVERAL body forms is the docstring. A CURRIED definition carries its
    docstring on the OUTERMOST lambda: (define ((f a) b) "doc" body) documents f,
    not (f 1). procedure-documentation answers the docstring or an explicitly set
    procedure-property of the same name, and #f otherwise -- it does NOT answer
    the procedure's name.

27. GOOPS EVALUATES A SLOT OPTION'S VALUE. #:init-value '() means the empty list,
    not the two-element list (quote ()). #:accessor, #:getter and #:setter stay
    quoted, because the macro is about to define those names.

28. char-ci<? AND ITS FAMILY FOLD UPWARD, as libguile/chars.c does, so every
    letter sorts BELOW the punctuation between the two ASCII cases -- [ \ ] ^ _ `
    -- instead of above it. Folding down agrees on every pair of letters and
    disagrees on every letter-versus-backslash pair.

29. SRFI-13's OPTIONAL [start [end]] IS HONOURED, AND OUT OF RANGE IS LOUD.
    string-index, string-rindex, string-count, string-pad, string-pad-right,
    string-reverse, string-titlecase, string-delete, string-filter, string-trim,
    string-trim-right, string-trim-both, string-any, string-every and
    string-tokenize all take it, validated as 0 <= start <= end <= length with a
    violation raised as a catchable out-of-range naming the procedure. Five
    shapes, read out of libguile rather than out of the SRFI document:
      - a hit from string-index / string-rindex is an index into the WHOLE
        string, never into the window;
      - string-reverse and string-titlecase copy the whole string and transform
        the region INSIDE the copy (SRFI-13's own reference implementation
        answers just the region; Guile does not);
      - string-pad, string-pad-right, string-delete, string-filter, the
        string-trim family and string-tokenize build their answer from the region
        ALONE -- characters outside it are dropped. string-pad truncates keeping
        the RIGHT of the region, string-pad-right the left, and string-pad's CHR
        must be a character;
      - string-any and string-every with a PREDICATE answer the value of the LAST
        call made, not a washed boolean, while a char or char-set criterion
        answers a plain boolean;
      - a wrong-typed criterion raises the positioned wrong-type-arg BEFORE the
        search loop, so an EMPTY window rejects it too.

30. append! RE-LINKS, AND THE IDENTITY IS THE POINT. It rewrites the last pair's
    cdr of each argument and answers the first non-empty argument, so a variable
    that held one of the inputs afterwards holds the CONCATENATION. Treating it
    as a faster `append' is invisible in every use that only reads the return
    value and wrong in every use that does not. The LAST argument is attached as
    it stands: never walked, and not required to be a list.

31. EVERY ARGUMENT BEFORE THE LAST OF append AND append! MUST BE A PROPER LIST,
    and the failures are libguile's own: append raises wrong-type-arg naming the
    argument's position, the words "empty list", and the offending TAIL; append!
    distinguishes a non-pair argument ("pair") from an improper tail ("empty
    list").

32. THE INDEX-WALKING FAMILY HAS THREE DIFFERENT FAILURES. list-ref, list-set!
    and list-cdr-set! (which splices a kth CDR and answers the VALUE) raise
    out-of-range naming argument 2 when they run off a proper list,
    wrong-type-arg naming argument 1 on an improper tail, and a NEGATIVE index
    dies inside the size_t conversion -- subr #f -- before the procedure's name
    enters the story. A catch on 'out-of-range stands on the distinction.

33. A VECTOR IS AN ARRAY, BUT A STRING IS NOT. array?, array-ref, array-set!,
    array-rank and array-dimensions accept a plain vector as the rank-1
    zero-based case, and array-set! writes THROUGH to the vector. Guile's
    scm_is_array also counts strings, bitvectors and bytevectors; those are not
    accepted here and give the same "Not an array" a missing name would.

34. AN EXACT ZERO IMAGINARY PART COLLAPSES IN THE READER. 1+0i IS the exact
    integer 1, while 1.0+0.0i stays complex. A product involving a complex is
    COMPUTED rather than short-circuited -- see pitfall 50, (* 0 z) is 0.0+0.0i.
    And a token ENDING in '@' is not a
    polar literal -- psyntax's own source contains such symbols -- so both sides
    of the '@' must exist before either is parsed.

35. THE QUOTE FAMILY ARE SYMBOL CONSTITUENTS MID-TOKEN. Hello' is ONE symbol;
    quote syntax lives at datum start only. And string escapes are FIXED WIDTH:
    \uXXXX takes exactly four hex digits and \UXXXXXX exactly six.

36. A SILENT, UNCATCHABLE STACK OVERFLOW IN SCHEME-HEAVY CODE HAS TWO KNOWN
    CAUSES, and both look identical: a `case' key that gets evaluated once per
    clause instead of once, and a carriage return inside a multi-line string
    literal in a vendored file. Read one as EITHER until proven otherwise.

PORTS, FILES AND THE OUTSIDE WORLD
----------------------------------
37. open-file IS NOT A SPELLING OF open-input-file. It takes a MODE STRING --
    "r", "w", "a", plus the "b" flag meaning one character per byte -- rather
    than keywords, and "+" is REFUSED loudly: a port here is a reader or a writer
    and never both.

38. file-port? IS NOT "HAS A NAME". It asks whether the port's implementation is
    the FILE one, so answering by name takes the wrong branch. ⚠ The parenthetical
    that stood here -- "a string port carries the name <string> in Guile too" --
    was REFUTED by measurement on 2026-08-30 and the behaviour it described is now
    gone: a string port has NO name, port-filename answers #f, its datums record
    #f as their source-properties filename, and its read errors say
    "#<unknown port>". A FILE port still names itself, which is what makes
    file-port? and "has a name" look alike; they are still not the same question.

39. close-port DISPOSES A FILE PORT'S WRITER, not merely flushes it -- but the
    current output and error ports are deliberately NOT disposed by it, because
    those writers belong to the host and must survive being closed from Scheme.

40. A STREAM-BACKED INPUT PORT REFUSES `read', LOUDLY. The datum reader works
    over a string, and buffering a live pipe to end-of-stream would block on the
    producer. Construct the port from a string when Scheme needs to `read'.

41. SOFT OUTPUT PORTS ARE BLOCK-BUFFERED AND THE BUFFERING IS OBSERVABLE. A soft
    output port buffers 1024 bytes; a write that does not fit tops the buffer up
    by whole 252-byte QUANTA and flushes, so an empty buffer transfers
    1008 = 4 x 252 at a time and leaves 16 bytes unused. Both constants were
    measured, and they decide where pretty-print's mid-write abort lands.

42. set-port-encoding! ON AN OPEN OUTPUT FILE PORT REOPENS IT APPENDING with the
    new codec, no BOM. It is real, not a no-op -- a stub that accepted its
    arguments and did nothing turned every octet above 0x7F into two UTF-8 bytes,
    and the corruption was visible only to whatever later READ the file.

43. A SIGNAL-KILLED CHILD DECODES THROUGH status:exit-val, NOT status:term-sig.
    .NET reports it as exit code 128+signal (the shell convention) rather than
    through a separate WIFSIGNALED channel, so status:exit-val answers 137 for
    SIGKILL and status:term-sig answers #f.

44. THE stat SLOTS .NET CANNOT ANSWER TRUTHFULLY HOLD #f DELIBERATELY -- dev,
    ino, nlink, uid, gid, rdev, blksize, blocks, plus mode and perms on Windows.
    A visible non-answer, not a plausible zero. The tm conventions are struct
    tm's exactly: mon 0-based, year from 1900, and tm:gmtoff seconds WEST of UTC.
    localtime's optional TZ argument is refused loudly rather than guessed at.

45. POSIX REGEXP FLAGS ARE INTEGERS PASSED AS SEPARATE REST ARGUMENTS, and three
    things are refused rather than half-served: regexp/basic (BRE is a different
    grammar), regexp/noteol, and the [. .] / [= =] collating forms. Inside
    brackets, [[:digit:]] and [[:xdigit:]] are ASCII (where .NET's \d is
    Unicode), a ] in first position is a literal, and a backslash is a LITERAL.
    An unmatched group's match:substring answers #f, never an empty string.
    RECORDED DIVERGENCE: alternation is .NET's leftmost-FIRST, not POSIX's
    leftmost-longest, so (a|ab) prefers the first alternative.

46. (ice-9 unicode) ANSWERS #f FOR A CJK IDEOGRAPH OR A HANGUL SYLLABLE rather
    than deriving its algorithmic name. That is Guile's measured behaviour;
    Python's unicodedata does the opposite. Do not "fix" it.

47. A NON-LOCAL EXIT FROM A NON-UNWINDING EXCEPTION HANDLER continues from the
    with-exception-handler frame rather than from the raise point, so frames
    BETWEEN the two do not see it. RECORDED DIVERGENCE, bounded; `guard' is
    unaffected because its prompt sits outside its handler.

48. THE WRONG NUMBER OF ARGUMENTS IS AN ERROR ON EVERY PATH, since 2026-08-28.
    Applying a procedure with too few or too many arguments raises Guile's
    wrong-number-of-args in the VM's shape -- (#f "Wrong number of arguments to
    ~A" (PROCEDURE) #f), so a report reads "Wrong number of arguments to
    #<procedure unfold-repeats (types music)>". Before that date the Tree-IL
    path (every psyntax-expanded procedure) bound a MISSING required parameter
    to #<unspecified> and DROPPED surplus arguments, and the body ran anyway;
    only the core evaluator's closures and primitives had ever checked. Found
    through LilyPond: scores calling unfold-repeats with its older arity
    engraved where the pinned oracle refuses the file. A case-lambda with no fitting
    clause names ITSELF in the error, not its last arm; #:optional still
    defaults to #f, a rest parameter still takes any count, and a #:key clause
    has no positional ceiling (its tail is keyword/value pairs). Fenced by
    WrongNumberOfArgumentsTests.cs.

49. A PRIMITIVE GENERIC RUNS THE PRIMITIVE FIRST, since 2026-08-28. Arity is
    checked, the primitive runs, and only its OWN type failure (a wrong-type-arg
    whose subr is the primitive's name) falls over to the generic that
    enable-primitive-generic! attached; no applicable method there is Guile's
    (goops-error #f "No applicable method for ~S in call ~S" (#<<generic> + (2)>
    (+ 3 "x")) ()) -- generic object, the failing PAIR for the pairwise operators,
    EMPTY LIST data. //was previously: method-first with the primitive as the
    fallback, which charged a method-selection pass to every arithmetic call and
    surfaced a type failure as wrong-type-arg. MEASURED on the pinned oracle:
    (define-method (max (a <integer>) (b <integer>)) ...) leaves (max 1 2) = 2.
    A primitive's arity error is the VM's shape too: (wrong-number-of-args #f
    "Wrong number of arguments to ~A" (#<procedure abs (_)>) #f). Fenced by
    PrimitiveGenericTests.cs.

50. THE NUMERIC FAMILY RAISES GUILE'S POSITIONED wrong-type-arg, since 2026-08-28:
    (NAME "Wrong type argument in position ~A: ~S" (POS VALUE) (VALUE)) -- a
    TEMPLATE message, position and value as its arguments, the value again as the
    data. Positions are PAIRWISE for + - * / < > <= >= max min gcd lcm logand
    (the accumulator is position 1 of every later pair), and `=' names position 1
    whichever side is bad. Guile's quirks are reproduced, not corrected: (* 1 "x")
    and (* "x" 1) are "x" (exact 1 is the multiplicative identity, tested before
    types); (< "x") is #t (one argument, unchecked); (> 1 2 "x") is #f (the first
    false pair stops the scan); lognot reports itself as "logxor"; (ash 1 "x")
    fails inside `<' as (< "x" 0); (expt "x" 2) fails inside `*' as (* "x" "x");
    a radix or shift that is not an exact integer is the UNNAMED (wrong-type-arg
    #f "Wrong type (expecting ~A): ~S" ("exact integer" v) (v)). quotient,
    remainder, modulo, gcd, lcm, even? and odd? ACCEPT inexact integers ((gcd 4.0
    2) is 2.0); the bitwise family wants EXACT ones. COMPLEX NUMBERS, same date:
    both parts PRINT as inexact reals (1+2i reads back as 1.0+2.0i, +i is
    0.0+1.0i); an EXACT zero imaginary part or polar angle is no complex at all
    ((make-rectangular 1 0) is 1, (make-rectangular 1 0.0) is 1.0+0.0i); (* 0 z)
    is the computed 0.0+0.0i, not an exact 0; sqrt of a negative real or of a
    complex, and exp/log/sin/cos/tan/asin/acos/atan/expt of a complex, compute in
    the complex plane (System.Numerics.Complex); zero? and = take a complex;
    inexact->exact of a zero-imaginary complex is the exact real; and the ordered
    comparisons, max, min, abs, the rounding family, positive?/negative?,
    numerator/denominator and inexact->exact of a non-zero-imaginary complex
    REFUSE a complex with the positioned error. //was previously: subr
    "arithmetic" / "comparison", unpositioned, #f data; a dozen primitives let a
    raw .NET ArgumentException escape to the host (a complex to any of them, too);
    floor, round, inexact->exact and numerator answered a non-number unchanged;
    complexes printed as 1+2i and (sqrt -4) was +nan.0. Fenced, every row
    measured on the oracle, by GuileNumericErrorShapeTests.cs.

51. define-method ON A NAME THAT HOLDS A PLAIN PROCEDURE IS REFUSED, since
    2026-08-28: (goops-error #f "~S is not a valid generic function" (proc) ()),
    which is what add-method! on a non-generic-capable <procedure> raises in
    GOOPS. //was previously: the procedure quietly became the new generic's
    Fallback -- written for LilyPond's operators.scm extending `+' and `*',
    which are generic-capable PRIMITIVES and are extended in place instead. A
    GenericFunction.Fallback now arises only from Enable on a primitive.

52. eval AND eval-string EXPAND. See pitfall 1: since 2026-08-28 Interpreter.Eval,
    Interpreter.EvalString and the Scheme `eval' / `eval-string' go through
    psyntax once it is loaded (ControlPrimitives.EvalAny), so a macro defined by
    one eval-string is usable by the next and a bare (markup ...) evaluates.
    LoadFile / LoadFileWithProgress stay on the core evaluator on purpose.

53. EVERY OUTPUT PORT TRACKS ITS LINE AND COLUMN, since 2026-08-30, and the rules
    are not "one character, one column". port-column used to answer for exactly two
    kinds of port -- a soft port, which keeps its own counters, and a string port,
    whose accumulated text could be re-read -- and returned a flat 0 for every
    other, the process's own output included. That single 0 broke
    (ice-9 pretty-print) outright: its `indent' emits a newline when the target
    column is BEHIND the current one and spaces otherwise, and `pp-list' passes
    (port-column port) itself as the target, so at zero it took neither branch --
    no newline, and (spaces 0) writes nothing -- and every separator between list
    items DISAPPEARED. display-scheme-music printed
    (make-music'SequentialMusic'elements(list ...)) on one unreadable line.
    ColumnTrackingWriter now sits in front of any writer that does not already
    track (SoftPortWriter keeps its own, deliberately updated on entry to the port
    rather than on flush -- pitfall 41). The counters live on the WRITER and not on
    SchemeOutputPort because current-output-port hands back a FRESH port object
    every call while the writer behind it is the shared thing; counters on the port
    would restart at zero each time it was asked for. Two consequences worth
    knowing: anything wanting the concrete sink underneath -- get-output-string and
    ftell want the StringWriter -- must go through SchemeOutputPort.InnerWriter or
    it finds the wrapper and answers empty; and set-port-column! / set-port-line!
    are REAL SETTERS now rather than the no-ops they were, because on the oracle
    (set-port-column! p 42) makes the next character land at 43.
    THE UPDATE RULES, every one MEASURED on the pinned oracle rather than read off
    Guile's source: a newline advances the line and zeroes the column; a CARRIAGE
    RETURN zeroes the column WITHOUT advancing the line; a TAB advances to the next
    multiple of eight (columns 0, 1 and 7 all become 8; 8 and 9 become 16); a
    BACKSPACE retreats one but never below zero; an ALARM advances nothing; a form
    feed and a vertical tab are ordinary characters; and a column counts CODE
    POINTS, so two astral characters make column 2 and not 4. Fenced by
    PortPositionTests, whose expectations are all oracle readings and which fails
    8 of its 10 cases with the change reverted.
    INPUT PORTS TRACK TOO, since the same day, and this is the half that reaches
    beyond printing. A datum's source-properties ARE the port's line and column at
    its first character, so the counters decide where every diagnostic points. Three
    things follow. (1) port-line / port-column answer for an input port, from the
    READER's own counters rather than a second set kept alongside -- a port whose
    position could be read but not moved would answer plausibly and do nothing.
    (2) set-port-line! / set-port-column! on an input port MOVE WHERE THE NEXT DATUM
    IS RECORDED, which is the whole point: LilyPond's parser-ly-from-scheme.scm
    synchronises a second port over the same text with exactly that pair so that
    #{ ... #} embedded Scheme carries the location of its real source, and with both
    calls no-ops the sync did nothing at all. (3) THE READER NOW COUNTS A TAB TO THE
    TAB STOP, so a source column on a tab-indented line changed: "\t(x)" records
    column 8, exactly as eight spaces do, where it used to record 1. peek-char does
    not advance, and unread-char retreats -- a newline taking the LINE back and
    leaving the column alone, a tab simply decrementing, both stopping at zero.
    ⚠ CONSEQUENCE FOR CONSUMERS: (3) and (2) change SOURCE LOCATIONS, so
    CodeBrix.LilyPort must take this as a pin bump with its full battery, not with
    the calibrated pin-bump bar -- #{ #} locations and any tab-indented input can
    move. Nothing in LilyPort's graded reference diagnostics carried the old values,
    but that is an argument for running the battery, not for skipping it.

54. A SYNTAX ERROR IS A read-error CONDITION, since 2026-08-30, and the reader is
    as STRICT as Guile's. It used to throw a plain .NET exception that
    (catch #t ...) went straight past, so no Scheme code could recover from a bad
    datum; SchemeReaderException now DERIVES from SchemeThrow, so it is caught by
    (catch 'read-error ...) while staying the same type a C# host catches. The
    condition is Guile's own shape --
      (read-error #f "NAME:LINE:COLUMN: text ~A" (args) #f)
    -- with NO subr, the position folded into the message TEXT, and the format
    arguments kept BESIDE it rather than substituted in. NAME is the port's file
    name or "#<unknown port>"; LINE and COLUMN count from ONE, which the port
    itself does not (port-line and port-column count from zero, and libguile adds
    one for the message: reading ")" leaves the port at column 1 and the message
    says 1:2). Every message string is upstream's verbatim, MEASURED one input at
    a time, and its inconsistencies are reproduced rather than tidied: #z reports
    "Unknown # object" and #d1x2 reports "unknown # object", and "unknown
    character name ~a" takes a lower-case directive where its neighbours take ~S.
    ⚠ FOUR THINGS THE READER USED TO ACCEPT AND NOW REFUSES, each measured against
    the oracle, which refuses them too: an unknown string escape ("\q" read as
    "q", losing the backslash silently); an unterminated #| ... |# comment (read
    as end of input); a mismatched close paren ("(a b]" closed the list anyway);
    and an unknown character name (#\nosuchchar answered #\n, the first letter --
    a silently WRONG character, the worst of the four). A consumer whose input the
    ORACLE accepts is unaffected, since the port is now strict in exactly the
    places upstream is.
    ⚠ AND THREE PLACES LET A RAW .NET EXCEPTION OUT of the reader, all of them
    int.Parse over whatever had been collected without validating it: "\x" (which
    collected the closing quote), #\xzz, and the same path for \u / \U. They raise
    read-errors now. Fenced by ReadErrorTests, whose eleven Scheme-level cases all
    fail with the change reverted and whose twelfth does not compile against the
    old surface.

55. A VALUE'S EXTERNAL REPRESENTATION READS BACK, since 2026-08-30, in three places
    where it did not. (a) A BYTEVECTOR wrote as "System.Byte[]" -- a .NET type name
    in Scheme output; it writes #vu8(1 2) now. (b) A SYMBOL wrote its bare name, so
    the symbol . wrote as . and a symbol containing a space wrote as though it were
    two; names that would not read back now use Guile's #{...}# syntax. (c) ARRAYS
    refused rank ZERO, which upstream reads (#0(a) has array-rank 0 and is indexed
    by NO subscripts, so array-ref takes one argument), printed a rank-1 array with
    a rank digit upstream omits (#1(a b) writes as #(a b), but #1@1(a b) keeps it),
    and reported a ragged literal as a read-error where upstream raises a
    misc-error -- it finds that while BUILDING the array, not while reading it.
    THE SYMBOL RULES, measured from a character-by-character table rather than
    derived. Extended syntax is needed when the name is EMPTY, is exactly ".",
    starts with a DIGIT (1+ and 1abc both qualify) or otherwise reads as a NUMBER
    (+1, -1, 1.5), starts with ' , or ` -- reader syntax only at the START, so a'b
    needs nothing -- or contains " # ( ) ; [ ] { } whitespace or a control
    character. INSIDE the braces the split is upstream's and is not tidy: the six
    bracketing characters and the control characters become \xN; with MINIMAL
    lower-case hex (\x9;, not \x09;), while " # ; a space and even a BACKSLASH are
    written literally. The backslash is upstream's own round-trip hazard, kept.
    ⚠ ONE ARRAY CASE IS DELIBERATELY NOT MATCHED: upstream reads an optional TYPE
    PREFIX (#2f64(...) is a typed array), which this implementation does not have.
    Where the input runs out the two agree exactly -- #2, #2a, "#2 a" and "#2 abc"
    all report end of input while reading an array -- but "#2 (...)" and #2x(...)
    get a read-error here where upstream reports a wrong-type-arg out of
    make-generalized-vector or length, naming a procedure the caller never used.
    Both refuse; only the shape differs. Fenced by ExternalRepresentationTests.

56. A CHARACTER LITERAL KNOWS EVERY NAME GUILE KNOWS, since 2026-08-30, and the
    ones it did not know were answered WRONG rather than refused. The table had
    twelve names in it; Guile has fifty-one, in five groups searched in order --
    R5RS (space, newline), R6RS (nul alarm backspace tab linefeed vtab page
    return esc delete), R7RS (escape), the abbreviated C0 control names (soh stx
    etx eot enq ack bel bs ht lf vt ff cr so si dle dc1..dc4 nak syn etb can em
    sub fs gs rs us sp del) and the compatibility names (null nl np). All five
    are matched CASE-INSENSITIVELY, so #\Cr and #\NUL read. The precedence is not
    decoration: several names answer one code point and it decides which name a
    character is WRITTEN with.
    ⚠ WHY IT MATTERED. Before, an unknown name fell back to "the name's first
    character", so #\cr read as #\c and #\lf as #\l -- a silently WRONG character.
    LilyPond's own lily.scm line 1055 does (string-delete #\cr ...) and
    (string-split ... #\nl), and framework-ps.scm line 596 maps #\cr and #\nul, so
    both files had been parsing to the wrong thing for the project's whole life.
    Pitfall 54's refusal made the same two files stop reading ALTOGETHER, which
    took LilyPort's engine down at boot -- it is what turned a quiet defect loud.
    THE NUMERIC ESCAPES, measured with them: octal is written bare (#\101 is A,
    while #\8 is the character 8 and #\19 is an unknown NAME, because a leading
    digit that does not make a valid octal number FALLS THROUGH to the table), and
    hex takes a LOWER-CASE x only -- #\X41, #\u41 and #\U41 are all refused by
    Guile and were all accepted here. #\rubout was accepted too and is not a Guile
    name. A code point outside 0..10FFFF or inside the surrogate block is
    integer->char's out-of-range condition, NOT a read-error, because upstream's
    reader reaches the character through integer->char itself; integer->char used
    to let a .NET ArgumentOutOfRangeException out instead, and that one did not
    surface until the PRINTER touched the value.
    ⚠ THE DOTTED-CIRCLE RULE HAD TO BE MEASURED, NOT READ: upstream ships two
    readers and they DISAGREE. libguile/read.c tests the FIRST character for
    U+25CC and answers the second; module/ice-9/read.scm tests the SECOND and
    answers the first -- and ice-9/read.scm is the one Guile 3 runs. Measured on
    the oracle, #\<combining acute><dotted circle> is 769 and the other order is
    refused. Fenced by SchemeReaderTests (61 acceptance rows, 6 refusals and the
    two-way control) and WrongTypeArgumentTests (the out-of-range family).
    THE PRINTER USES THE SAME TABLE BACKWARDS, and its half was five names long, so
    a control character wrote as ITSELF -- a raw byte in the middle of Scheme output
    where the oracle writes #\soh, #\vtab, #\delete. A GRAPHIC character (Unicode
    categories L, M, N, P and S -- upstream's own test, which is what keeps SPACE,
    category Zs, on the named path) writes as itself; anything else takes a name if
    it has one and otherwise the octal escape. The search ORDER decides which name a
    character is written with, so 0x0d writes as #\return and not #\cr, 0x0a as
    #\newline and not #\lf, 0x0c as #\page and not #\ff -- while all of cr, lf and ff
    still READ. Fenced by ExternalRepresentationTests, including a round trip over
    every code point through 0xFF asserted as a relationship rather than a literal.

57. AN OPTIONAL PORT ARGUMENT MEANS THE CURRENT INPUT PORT, since 2026-08-30, and
    for read, read-syntax, read-char and peek-char it used to mean END OF FILE. Called
    with no port they answered #<eof> unconditionally -- a plausible answer, so
    nothing looked broken: (read) simply read nothing, and the whole
    current-input-port mechanism was inert behind it. All four now fall back to
    (current-input-port), and an explicit port argument still wins.
    with-input-from-string came with them; it was the one member of the string-port
    family missing, though (ice-9 ports) exports it into the default environment
    upstream and both with-output-to-string and call-with-input-string were here.
    ⚠ IT REDIRECTS THE PORT, NOT THE READER, and that is forced rather than chosen:
    a reader-backed port STREAMS, and a streaming port refuses `read' by design
    (pitfall 40) -- redirecting Interpreter.InputReader would give a
    current-input-port that read-char could use and `read' could not. Upstream lands
    in the same place from the other side, defining with-input-from-string as
    call-with-input-string plus with-input-from-port.
    THE WHOLE with-*-port FAMILY WAS ABSENT and all three are here now:
    with-input-from-port, with-output-to-port and with-error-to-port. The input one
    swaps the port override described above; the two output ones swap the
    interpreter's WRITER, because the output side resolves its default port through
    the writer (display with no port asks TrackedOutputWriter()) -- which is also how
    with-output-to-string already worked, so the two nest correctly in either order.
    ⚠ with-output-to-port was not merely missing: the vendored ice-9/pretty-print.scm
    CALLS it at line 494, so truncated-print raised unbound-variable where the oracle
    prints (a b c); boot-9.scm calls it twice more, in peek-error and %load-announce.
    %default-port-conversion-strategy came with it -- a FLUID holding 'substitute
    (measured), which pretty-print.scm rebinds with with-fluids at its line 335.
    Nothing here CONSULTS that fluid: strings are UTF-16 throughout and no port raises
    encoding-error, so the rebinding simply succeeds.
    port-encoding CAME WITH THEM, and BOTH PORT TYPES NOW CARRY AN ENCODING NAME
    defaulting to "UTF-8" -- upstream's answer for all four port kinds (measured; an
    earlier reading of "" for a string output port was a MEASUREMENT ERROR, since
    call-with-output-string returns the accumulated STRING and not the procedure's
    value). set-port-encoding! records the name for every port kind AND keeps
    re-encoding the bytes of a file port, which scm/backend-library.scm depends on.
    Upstream's canonicalisation is UPPER-CASING and nothing else: "latin1" and
    "Latin1" both answer "LATIN1", "ISO-8859-1" is NOT collapsed onto it, and an
    explicit #:encoding at open time shows through (#:binary is "ISO-8859-1").
    ⚠ ENCODING IS OPERATIVE AT THE FILE BOUNDARY AND NOMINAL EVERYWHERE ELSE. That is
    the shape of this implementation rather than a compromise: a file port's reader or
    writer really does decide bytes, while a string port has no byte layer at all
    because strings are UTF-16 throughout. The name is carried for every port kind
    because pretty-print.scm:338 ROUND-TRIPS it onto another port rather than
    interpreting it.
    ⚠ ONE CONSEQUENCE, DELIBERATELY NOT BUILT: nothing here raises encoding-error, so
    %default-port-conversion-strategy 'error cannot fire and truncated-print always
    picks the real U+2026 ellipsis where Guile on a Latin-1 port falls back to "...".
    Upstream's fallback IS reachable (measured). Building it means checking
    representability on the write path for ports carrying a non-UTF-8 name; it would
    buy one character in one procedure that nothing in LilyPort calls. Decided
    2026-08-30: LEFT ALONE.
    Fenced by PortProcedureTests, with the nesting case as the control that a redirect
    is undone and a refusal row per family member.

58. THE ARRAY FAMILY GAINED ITS LAST THREE ACCESSORS, 2026-08-30, and with them
    (ice-9 pretty-print) reached full parity -- truncated-print RUNS now, having been
    blocked in turn on with-output-to-port, %default-port-conversion-strategy,
    port-encoding and then these. array-length answers dimension ZERO (measured: 2 for
    #2((a b c) (d e f)), not 3 and not 6) and refuses a rank-0 array, which has no
    dimension to report. array-type answers #t: every array here is a general one,
    which is upstream's own answer for a vector and for a multi-dimensional array.
    bitvector? answers #f for every value, because there is no such type to BE one --
    true rather than merely plausible, which is what keeps it out of pitfall 12's
    stubbed-predicate shape; it is the first thing to change if bitvectors are added.
    ⚠ PITFALL 33'S DIVERGENCE EXTENDS TO THESE TWO, deliberately: upstream answers `a'
    for a string and `vu8' for a bytevector because those ARE arrays there and are not
    arrays here, so they take the family's "Not an array" like any other non-array.
    array? must not accept what array-type refuses, nor the other way round.
    ⚠ A DIFF OF (ice-9 pretty-print)'s BINDINGS AGAINST THE ORACLE NOW SHOWS EXACTLY
    ONE NAME, `else', and that is NOT a defect: cond and case handle it as syntax here
    rather than as a module binding, verified by running all three forms. That diff is
    the cheap instrument for this question -- module-defined? over every head symbol in
    the file, on both engines -- and it is what turned a five-round guessing chain into
    one measurement. Fenced by GuileCompatibilityTests, whose truncated-print case is
    the end-to-end fence for all six names.

WHAT THIS PACKAGE DOES NOT DO
=============================
It is not a general-purpose Guile replacement. It implements the subset of Guile
that LilyPond's Scheme layer needs. A use-modules imports what Guile's does (see
pitfall 20), but a handful of procedures Guile keeps in a module -- the file and
port openers named under OPENING FILES FROM SCHEME -- are placed CORE-side here
and so are reachable without importing anything. Specifically, none of the
following exists:

* NO VM, NO COMPILER, NO BYTECODE. Everything is interpreted. The
  (system vm program) shim answers #f from program? for every value. There are no
  stack frames, so print-exception accepts and IGNORES its frame argument, and
  there is no backtrace, no debugger and no make-stack.
* NO call-with-current-continuation. Prompts are ESCAPE-ONLY: call-with-prompt,
  abort-to-prompt and make-prompt-tag work for aborting OUT of a thunk, and
  re-entering a captured continuation fails loudly. dynamic-wind is present.
* NO FFI, NO dynamic-link, NO C extensions, NO shared-library loading.
* NO THREADS, FUTURES OR FIBERS at the Scheme level. RunWithLargeStack starts one
  thread for stack size, and that is the whole of the concurrency story.
* NO EXACT COMPLEX NUMBERS. A complex here always has double parts. (Exact
  integers, bignums and rationals are all present.)
* NO BASIC REGULAR EXPRESSIONS. regexp/basic is refused, as are regexp/noteol and
  the [. .] / [= =] collating forms, and alternation is leftmost-first rather
  than POSIX's leftmost-longest.
* NO INPUT SOFT PORTS. make-soft-port builds OUTPUT ports over a Scheme
  write-string procedure; the input form is refused loudly until something
  demands it.
* NO BIDIRECTIONAL PORTS. OPEN_BOTH and open-input-output-pipe are refused, and
  so is open-file's "+" mode. A port is a reader or a writer, never both.
* NO boot-9.scm. It is vendored for reference and never loaded; a name that
  exists only there is unbound at runtime. Likewise ice-9/ports.scm and
  ice-9/textual-ports.scm, whose procedures live core-side instead.
* NO getpwent / getgrent FAMILY. The vendored posix.scm's wrappers over
  getpw/setpw/getgr/setgr are unbound; calling one is a visible
  unbound-variable error.
* NO TZ ARGUMENT TO localtime, because mapping POSIX TZ strings onto .NET time
  zones would be a guess.
* NO ALGORITHMIC UNICODE NAMES for CJK ideographs or Hangul syllables.
* NO ARRAY VIEW OF STRINGS, BITVECTORS OR BYTEVECTORS -- array? counts vectors
  and SchemeArray only.
* NO srfi-43 vector-copy FILL ARGUMENT: importers resolve the range-capable core
  binding, whose signature is [start [end]].
* NO SYSTEM-FONT, GRAPHICS, NETWORKING OR LILYPOND LAYER. This package is the
  Scheme language only. The LilyPond layer is a separate project built on top of
  it.

WORKING EXAMPLES ON GITHUB
==========================
The test suite is the executable specification for everything above; read it,
do not run it (it is long-running by design). Browse it at:

  https://github.com/ellisnet/CodeBrix.LilyScheme/tree/main/tests/CodeBrix.LilyScheme.Tests

Files worth reading first, by what you are trying to do
(https://github.com/ellisnet/CodeBrix.LilyScheme/blob/main/tests/CodeBrix.LilyScheme.Tests/<file>):

    PsyntaxBootstrapTests.cs    the minimal working embedding: psyntax loads,
                                macroexpand returns Tree-IL, syntax-rules runs,
                                expansion is hygienic, srfi-1 and (ice-9 match)
                                both load and work
    InterpreterTests.cs         the core evaluator without psyntax -- arithmetic,
                                closures, tail calls, letrec, lambda*, hash
                                tables, catch/throw, and the pre-unwind
                                with-throw-handler contract
    WrongTypeArgumentTests.cs   HOST-REGISTERED PRIMITIVES through DefinePrimitive,
                                including a deliberately bare cast reaching the
                                translation net, and a primitive raising its own
                                SchemeThrow
    WrongNumberOfArgumentsTests.cs
                                arity on the Tree-IL path: too few and too many
                                arguments raise wrong-number-of-args in the VM's
                                shape, the report text character for character,
                                case-lambda naming itself, and #:optional / rest /
                                #:key clauses unaffected
    GuileNumericErrorShapeTests.cs
                                the numeric family's wrong-type-arg in Guile's
                                template shape at Guile's positions (every row
                                measured on the oracle), the reproduced quirks
                                ((* 1 "x"), (< "x"), short-circuit, expt via *),
                                inexact integers accepted where Guile accepts them,
                                and no host exception escaping any numeric site
    EvalExpansionTests.cs       eval / eval-string / Interpreter.Eval / EvalString
                                macro-expand once psyntax is loaded: a macro
                                defined by one eval-string is used by the next,
                                and the (lily)-shaped explicit-module case
    ExpansionCacheTests.cs      wiring the cache: record, serialize, replay,
                                per-interpreter deserialization, identity-preserving
                                round-trips, and corruption reading as a MISS
    SchemeReaderTests.cs        reader coverage and the Printer.WriteString round
                                trip a host path depends on, Windows and POSIX
                                shapes both; and the whole character-literal
                                table -- every name Guile knows, the octal and
                                hex escapes, and the forms it refuses (pitfall 56)
    SourceLocationTests.cs      source locations from the reader through psyntax
                                into a procedure's printed representation, and the
                                program-print latch
    GuileCompatibilityTests.cs  the broad surface: modules, quasisyntax, SRFI-13
                                and SRFI-14, SRFI-9 records, generalized setters,
                                GOOPS over built-in classes, string ports, reader
                                hash extensions, format, arrays, the bitwise
                                family, the shim modules, prompts, pretty-print
    ModernExceptionTests.cs     the modern exception API from both sides of the
                                old/new interop
    PortProcedureTests.cs       #:encoding, the two end-of-file conventions,
                                with-input-from-string and the optional-port
                                defaulting behind it (pitfall 57), the
                                mode-string opener, close-port and file-port?
    ExternalRepresentationTests.cs
                                what a value writes as: bytevectors, the #{...}#
                                symbol syntax and its escaping table, array
                                rank 0 / rank 1 / ragged literals, and the
                                character-name table the printer writes with
                                (pitfall 56)
    ReadErrorTests.cs           the reader's error surface: read-error as a Scheme
                                condition, upstream's wording and position format,
                                port naming, and the four inputs it used to accept
    PortPositionTests.cs        port-line / port-column on every kind of port,
                                input and output, and the real setters; the tab /
                                carriage return / backspace / alarm / code-point
                                rules; peek and unread; a datum's source location
                                and the #{ #} synchronisation that moves it; and
                                pretty-print's line breaking, which stands on all
                                of it
    BinaryPortTests.cs          set-port-encoding!, the directory family,
                                read-char / peek-char
    PopenTests.cs               (ice-9 popen) against real child processes,
                                including the loud OPEN_BOTH refusal
    PosixTests.cs               wait-status encoding, stat, broken-down time
    RegexPosixTests.cs          every divergence-prone regexp point, from both sides
    NarrowImportTests.cs        Interpreter.NarrowModuleImports in both positions
    SelectImportTests.cs        use-modules #:select, both halves
    PrimitiveGenericTests.cs    extending a generic-capable primitive, and the
                                cross-module visibility that is the whole point
    RecordInheritanceTests.cs   the single-inheritance record model
    ComplexNumberTests.cs       complex literals, arithmetic and the exact-zero
                                collapse
    Srfi13RangeTests.cs         every SRFI-13 range, fenced with cases whose ranged
                                answer DIFFERS from the unranged one
    DestructiveAppendTests.cs   append! re-linking, and both appends' measured
                                failure shapes
    LilyPondSchemeManualTests.cs  a user's-eye contract: REPL transcripts from a
                                published "LilyPond's Scheme" tutorial, replayed

QUICK REFERENCE CARD
====================
    PACKAGE   CodeBrix.LilyScheme.LgplLicenseForever   (namespace
              CodeBrix.LilyScheme, .NET 10 or later, LGPL-3.0-or-later)

    BOOT      Interpreter i = new Interpreter();
              Interpreter.RunWithLargeStack(() => {
                  SchemeBootstrap.LoadCore(i);            // ALWAYS first
                  ...
              });

    EVALUATE  i.TreeIlEvaluator.ExpandAndEval(form, i.CurrentModule)  // macros
              SchemeBootstrap.LoadExpanded(i, source, fileName)       // macros
              i.Eval / i.EvalString            // macros, once psyntax is loaded
              i.LoadFile / i.LoadFileWithProgress            // NO macros, ever

    READ      SchemeReader.ReadAll(text, fileName) -> List<object>
              SchemeReader.RegisterHashExtension('c', reader => ...)

    PRINT     Printer.Write(v) / Printer.Display(v)
              Printer.WriteString(hostPath)      // ALWAYS for a path
              Printer.ResetProgramPrintLatch()   // per input file

    HOST API  i.DefinePrimitive(name, min, max, a => ...)  // max -1 = variadic
              i.DefineValue(name, value)
              i.OutputWriter / i.ErrorWriter / i.InputReader
              i.LoadPath (List<string>) / i.ObjectProperties
              i.NarrowModuleImports / i.ExpansionCache
              i.Modules.ModuleLoader = (name, module) => ...  // SAVE CurrentModule

    CALL      i.Evaluator.Apply(procedure, new object[] { ... })
              i.CurrentModule.Lookup(Symbol.Intern("name")).GetValue()

    VALUES    bool | Pair | Nil.Instance | Symbol | Keyword | MutableString |
              SchemeChar | object[] | long / BigInteger / Ratio / double /
              ComplexNumber | Unspecified.Instance | EofObject.Instance |
              MultipleValues | Variable | Fluid | SchemeHashTable |
              SchemeInputPort | SchemeOutputPort | Procedure | IApplicable |
              object[] with a RecordType at slot 0 | SchemeStruct | SchemeArray

    ERRORS    catch (SchemeThrow e)  -> e.Key (Symbol), e.Arguments
                                        = (SUBR MESSAGE ARGS DATA)
              catch (SchemeEvaluationException e)  -> file + form index, InnerException
              catch (SchemeReaderException e)      -> syntax error while reading

    VALIDATE  TypeChecks.AsSymbol/AsChar/AsKeyword/AsMutableString(v, name, pos)
              StringPrimitives.Text(v, name)       // read-only text
              SchemeNumber.IsNumber/IsInteger/ToBigInteger/ToDouble

    CACHE     ExpansionCacheFile.TryReadFile(path, key) ?? new ExpansionCache()
              i.ExpansionCache = cache;  ...  ExpansionCacheFile.WriteFile(...)
              ONE INSTANCE PER INTERPRETER.

    SHUTDOWN  evaluate (flush-all-ports) before you stop caring about the run
