================================================================
AGENT-README: CodeBrix.LilyScheme
A Comprehensive Guide for AI Coding Agents
================================================================

OVERVIEW
--------
CodeBrix.LilyScheme is a managed, cross-platform Scheme language
implementation for .NET, derived from the GNU Guile project. It
implements the subset of Guile that LilyPond's Scheme layer needs.

THE CENTRAL IDEA
----------------
Guile ships its macro expander twice: as psyntax.scm, written in
syntax-case, and as psyntax-pp.scm, the same expander already
macro-expanded into core Scheme so it can be loaded by an
implementation that does not yet have a macro expander. That is how
Guile bootstraps itself, and it is how LilyScheme gets full
syntax-case without anyone hand-writing an expander.

So the architecture is:

    source text
       -> reader                (Reader/SchemeReader.cs)
       -> core evaluator        (Runtime/Evaluator.cs)   [bootstrap only]
       -> psyntax               (vendored psyntax-pp.scm)
       -> Tree-IL structs
       -> Tree-IL evaluator     (TreeIl/TreeIlEvaluator.cs)

Guile's macroexpand does NOT return s-expressions. It returns structs
built from the %expanded-vtables vector -- Tree-IL. There are exactly
eighteen node types, mirrored from libguile/expand.h in
Values/SchemeStruct.cs. Getting their field order wrong breaks
everything silently, because psyntax constructs them positionally.

QUICK START
-----------
    Interpreter interpreter = new Interpreter();
    SchemeBootstrap.LoadCore(interpreter);      // psyntax + prelude
    object form = SchemeReader.ReadAll("(+ 1 2)", "<input>")[0];
    object value = interpreter.TreeIlEvaluator.ExpandAndEval(
        form, interpreter.CurrentModule);

Run deeply recursive Scheme on a big stack:

    Interpreter.RunWithLargeStack(() => { /* ... */ });

psyntax recurses hard while expanding and will overflow the CLR's
default 1 MB stack. The limit is per thread, so a dedicated thread is
the fix; LargeStackBytes is 256 MB.

An exception raised on that thread reaches the caller AS ITSELF, with
its original stack trace, so `catch` clauses read the same as they
would without the thread. Do not re-introduce a wrapper here: one used
to live in this method and it hid every real message behind a generic
one, which made a whole class of failure look like a single unknown
cause.

TWO SHARP EDGES WORTH KNOWING
-----------------------------
* syntax-empty-wrap is (() . ()), a cons of two empty lists -- a
  psyntax wrap is (marks . substs). libguile/syntax.c builds it as
  scm_cons (SCM_EOL, SCM_EOL). Get this wrong and every identifier
  resolves as a top-level reference instead of a lexical one, with no
  error: lambda parameters simply come out unbound.

* primitive-eval receives ALREADY-EXPANDED Tree-IL. psyntax's
  top-level-eval and local-eval hooks are both
  (lambda (x mod) (primitive-eval x)). If primitive-eval only handles
  source forms, every macro definition silently stores an unevaluated
  struct and the macro is never found.

A WRONG-TYPED PRIMITIVE ARGUMENT RAISES wrong-type-arg, NEVER A HOST EXCEPTION
------------------------------------------------------------------------------
Guile validates every primitive argument and raises a catchable
wrong-type-arg naming the procedure and the argument position; Scheme code
legitimately catches that key. A bare C# cast in a primitive body performs
the same check the .NET way, and the resulting InvalidCastException escapes
to the host where no Scheme catch can see it. Two layers keep that from
happening here:

* Primitives.TypeChecks (AsSymbol / AsChar / AsKeyword / AsMutableString)
  raises the POSITIONED error -- "(subr "Wrong type argument in position
  N: ~S" (value) #f)" -- and is what a primitive body should use instead of
  a cast. StringPrimitives.Text is the older sibling for read-only text
  arguments (it accepts symbols, chars and keywords too; TypeChecks'
  AsMutableString is for primitives that MUTATE).

* Primitive.Invoke carries a last-resort net: an InvalidCastException from
  any primitive body -- including one a HOST registers through
  DefinePrimitive -- is translated to wrong-type-arg named for the
  primitive, unpositioned. A SchemeThrow from a nested primitive is not an
  InvalidCastException and passes through with its own attribution.

The contract is fenced by WrongTypeArgumentTests. When writing a new
primitive, prefer the positioned accessor; the net is the backstop, not the
convention.

INSTALLATION
------------
NuGet package: CodeBrix.LilyScheme.LgplLicenseForever

    dotnet add package CodeBrix.LilyScheme.LgplLicenseForever

The library's namespace is `CodeBrix.LilyScheme` (without the
`.LgplLicenseForever` suffix -- that suffix is part of the NuGet
package ID only, chosen to travel the LGPL-3.0-or-later license
identification with the package name).

Target framework: .NET 10.0 or higher.

KEY NAMESPACES
--------------
    using CodeBrix.LilyScheme;              // Interpreter
    using CodeBrix.LilyScheme.Reader;       // SchemeReader
    using CodeBrix.LilyScheme.Runtime;      // Evaluator, SchemeBootstrap, Printer
    using CodeBrix.LilyScheme.TreeIl;       // TreeIlEvaluator
    using CodeBrix.LilyScheme.Values;       // Symbol, Pair, SchemeStruct, ...
    using CodeBrix.LilyScheme.Numeric;      // SchemeNumber, Ratio
    using CodeBrix.LilyScheme.Caching;      // ExpansionCache, ExpansionCacheFile

CORE API REFERENCE
-------------------
Interpreter                 the embedding entry point: modules,
                            evaluators, primitives, load path
SchemeBootstrap.LoadCore    loads psyntax and the prelude
SchemeBootstrap.LoadExpanded  loads Scheme through psyntax (or the
                            interpreter's ExpansionCache — see THE
                            EXPANSION CACHE below)
ExpansionCache              per-file recorded Tree-IL; record/replay
ExpansionCacheFile          its keyed, checksummed binary serialization
SchemeReader                text -> Scheme data
SourceProperties            where each datum was read from; psyntax's
                            only source of location information
Evaluator                   core s-expression evaluator, with proper
                            tail calls (used to bootstrap)
TreeIlEvaluator             evaluates the eighteen Tree-IL node types
SchemeModule / ModuleRegistry   Guile's module system
Printer                     write / display external representations, plus
                            Printer.WriteString -- host text as a Scheme
                            string LITERAL, which is what a FILESYSTEM PATH
                            must go through (see A HOST PATH REACHES SCHEME
                            THROUGH Printer.WriteString below)
SchemeNumber                the fixnum/bignum/ratio/real tower
IApplicable                 an embedder's own value in operator position

CODING CONVENTIONS (CodeBrix family)
-------------------------------------
* Target framework is always net10.0 -- no multi-targeting.
* `<Nullable>` is never enabled family-wide. Do not write `?` on
  reference types (`string?`, `MyClass?`, etc.) and never use the
  null-forgiveness `!` operator. Value-type nullables (`int?`,
  `bool?`, `MyEnum?`) are fine -- those are `Nullable<T>`.
* No `global using` directives anywhere.
* File-scoped namespaces only (`namespace X;`), never block-scoped.
* `using` directives are fully qualified, at the top of the file,
  System.* first then alphabetical, with no blank lines inside the
  block, and never appear below the `namespace` line.
* `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is on
  for the library project. Every public type and public/protected
  member needs an XML doc comment; CS1591 is fixed at the source,
  never suppressed with `<NoWarn>`.
* Files ported from an external open-source project carry a
  `//was previously: <upstream.ns>;`
  provenance comment on the `namespace` line, and any non-trivial
  upstream copyright header is preserved verbatim above the usings.
  New-in-family files never get a fabricated header.
* Tests use xUnit v3 + SilverAssertions fluent assertions
  (`x.Should().Be(y)`), coverlet.collector for coverage, and thread
  `TestContext.Current.CancellationToken` through any call that
  accepts a `CancellationToken`.
* No project-level warning suppression (`<NoWarn>`,
  `<WarningLevel>0</WarningLevel>`, `<TreatWarningsAsErrors>false</>`)
  on any csproj in this repo.

ARCHITECTURE
------------
    Values/       Scheme data: Symbol, Pair, SchemeStruct (+ the
                  eighteen Tree-IL vtables), SyntaxObject, Procedure
    Numeric/      the numeric tower: fixnum, bignum, Ratio, real
    Reader/       SchemeReader and SourceProperties -- Guile dialect, including #:keywords,
                  #{extended symbols}#, #nil, block and datum comments,
                  array literals (#1@1(...), #2((...) (...))), and Guile's
                  fixed-width \uXXXX / \UXXXXXX string escapes (exactly four
                  and six hex digits, libguile/read.c's SCM_READ_HEX_ESCAPE)
    Runtime/      Evaluator, SchemeModule, LexicalEnvironment, Printer,
                  SchemeBootstrap
    TreeIl/       TreeIlEvaluator, TreeIlClosure
    Primitives/   Core, GuileCore, Numeric, String, Vector, Array, Control,
                  Module, Port, SoftPort, Goops, PrimitiveGenerics,
                  BuiltinClasses, Unicode, Posix, Exception
    Caching/      ExpansionCache and its keyed, checksummed binary file
                  format (see THE EXPANSION CACHE below)
    Scheme/       vendored Guile .scm (embedded resources) plus
                  lilyscheme/prelude.scm
    Unicode/      the formal character-name table (an embedded resource derived
                  from Unicode's UnicodeData.txt) and its reader; what
                  (ice-9 unicode) answers from

    Interpreter.cs is the only type at the root -- it is the entry point.

MODULE AUTOLOADING
------------------
(use-modules (srfi srfi-1)) loads the vendored srfi-1.scm the first time the
module is named, exactly as Guile autoloads. Without it a freshly resolved
module is simply EMPTY and every name it would supply comes out unbound --
which reads as dozens of unrelated failures rather than one missing mechanism.

The vendored srfi-1.scm expects some of its own names FROM THE CORE and marks
them so -- a line reading ";; filter!  <= in the core" sits where the definition
would be, and the name is then re-exported. A core that does not define such a
name leaves it unbound for everything importing (srfi srfi-1). Grep the vendored
file for "in the core" before concluding that an srfi-1 name is missing on
purpose.

SchemeBootstrap.SelfProvidedModules lists the modules that must NOT be
autoloaded: (oop goops) is superseded by the C# GOOPS, (ice-9 optargs) and
(ice-9 and-let-star) by the prelude, (ice-9 boot-9) cannot load at all, and
five SHIM modules are provided from C# at bootstrap -- (system vm program),
whose program? answers #f for everything because LilyScheme has no VM,
(ice-9 iconv), whose string->bytevector / bytevector->string run over .NET's
encodings, (ice-9 soft-ports), whose keyword-form make-soft-port builds
output ports over a Scheme write-string procedure (input soft ports are
refused loudly until something demands them), (ice-9 popen), whose pipes run
over System.Diagnostics.Process (see SUBPROCESSES below), and (ice-9 unicode), whose
char->formal-name / formal-name->char read a shipped table because Guile
implements them over GNU libunistring and .NET has no character names at all.
/!\ (ice-9 unicode) answers #f for a CJK ideograph or a Hangul syllable rather
than DERIVING its algorithmic name -- that is Guile's behaviour, measured, and
Python's unicodedata does the opposite; do not "fix" it. The vendored ice-9/session.scm
needs the first; LilyPond's qr-code.scm imports from the second; the vendored
pretty-print.scm builds its truncating writer on the third, together with the
escape-only call-with-prompt / abort-to-prompt / make-prompt-tag protocol in
ControlPrimitives -- aborting OUT of the thunk works in full, re-entering the
continuation fails loudly.

APPLICABLE HOST OBJECTS
-----------------------
Guile lets a smob declare an apply hook (scm_set_smob_apply), so a host object
can sit in operator position and still be its own type for every predicate.
Values/Procedure.cs's IApplicable is the managed equivalent: implement it on a
type of your own and both the evaluator's apply path and procedure? accept it,
without that type having to derive from Procedure.

CodeBrix.LilyPort needs this for LilyPond's Music_function, which upstream
declares with LY_DECLARE_SMOB_PROC -- its syntax constructors ARE music
functions, and the parser applies them directly.

MODULES: THE THREE OPERATIONS THAT ARE NOT INTERCHANGEABLE
---------------------------------------------------------
module-define! takes a VALUE and makes the module a variable of its own.
module-add! takes a VARIABLE and installs that cell as the binding, so two
modules SHARE one location and a set! through either is seen by both. Guile's
own body errors on a non-variable third argument, and code relies on the
sharing: LilyPond's define-session-public hands every parser scope the very
variable that lives in (lily), commenting that this is so "both set! and define
will affect the original variable". Implementing module-add! as an alias for
module-define! compiles, loads and passes every test that only READS the name --
and then hands readers the VARIABLE OBJECT as if it were the value.

module-remove! drops a module's own binding for a name and nothing more; a name
it was shadowing goes back to resolving through imports.

Modules also carry Guile's kind field (module-kind / set-module-kind!,
defaulting to 'module) and a submodules table (module-submodules /
set-module-submodules!), keyed by the last name component and linked in when a
child module is registered. LilyPond's session-save and session-terminate use
all four to snapshot and restore a session's namespace.

module-name NAMES an anonymous module on first ask -- a fresh generated name,
under which the module is simultaneously REGISTERED (SchemeModule.EnsureName;
boot-9 does the same with a gensym). This is load-bearing for macros: psyntax
round-trips module identity BY NAME inside hygiene wraps, so an imported macro
used in a module that cannot be named back does not resolve as a macro at all
-- it reads as an ordinary variable and its arguments get evaluated. LilyPond's
anonymous parser scopes hit exactly this: every define-music-function in the
init layer failed on an unbound variable named after its first parameter, and
the consumer worked around it by naming its scopes until the lazy naming
landed here. defined? ((defined? sym [module]), resolving against the current
module and answering #f for an unbound variable) exists for the same layer:
ly/init.ly's epilogue uses it to find the toplevel book handler escape hatch.
AnonymousModuleMacroTests fences the whole mechanism.

EXTENDING A PRIMITIVE IS GLOBAL; EXTENDING A NAME IS NOT
--------------------------------------------------------
define-method does two different things depending on what the name already holds.

On a fresh name it defines a generic in the current module -- Guile's
toplevel-define!, and what oop/goops.scm's define-method macro does when
(defined? 'name) is false.

On a GENERIC-CAPABLE PRIMITIVE it defines nothing. goops.scm's
(define-method (add-method! (proc <procedure>) (m <method>)) ...) calls
enable-primitive-generic! on the subr, which hangs the generic off the PRIMITIVE
ITSELF, and the method goes there. Because every module that imports the core
sees that one object, the extension is global: LilyPond writes
(define-method (- (a <Pitch>) (b <Pitch>)) ...) once in lily/operators.scm and
\transpose works from parser scopes that never loaded the file.

Getting this wrong is invisible from the defining module. Defining a fresh
generic there instead passes every test that subtracts pitches in that same
module, and every OTHER module still resolves the raw numeric '-' and throws
wrong-type-arg -- which is how 28 files' worth of accidental and \transpose
failures looked like unrelated arithmetic bugs. Primitives/PrimitiveGenerics.cs
holds the mechanism and the roster of capable names, which was READ OUT of
libguile rather than recalled: an undeclared name simply never dispatches, with
no diagnostic. Do NOT reach for a module-ordering fix -- reordering
LilyModules.Make or the Install calls diverges from ly_make_module shadowing for
every binding, and does not make the extension global anyway.

The apply path selects a method first and invokes the primitive when none
applies, so ordinary arithmetic is untouched: LilyPond's operator methods all
carry real specializers, and a number never matches <Moment> or <Pitch>.
PrimitiveGenericTests fences the mechanism, including the cross-module case that
is the whole point.

GOOPS #:accessor CARRIES A SETTER
---------------------------------
GOOPS's #:accessor makes an <accessor> -- a generic whose setter is a generic --
so (set! (acc obj) v) works. The prelude's define-class emits a
make-procedure-with-setter for the same reason. A bare lambda reads identically
everywhere the accessor is only CALLED, and then scm/part-combiner.scm's
(set! (split-index state) idx) throws wrong-type-arg on `setter', taking the
whole \partCombine family with it.

Note that make-procedure-with-setter and procedure-with-setter? are installed by
GuileCorePrimitives.InstallSetters. Placeholders answering "no setter" used to
sit in ControlPrimitives as well, and worked only because ControlPrimitives
installs FIRST -- swapping the two Install calls would have made every accessor
silently discard its setter. They are gone.

use-modules #:select -- BOTH HALVES ARE HONOURED
------------------------------------------------
A #:select element is either a bare symbol or a pair (original . local). The
RENAMED form binds a name that exists nowhere else, so dropping it leaves that
name unbound with no diagnostic -- scm/lily.scm opens with
((ice-9 format) #:select ((format . ice9-format))) and then calls ice9-format
from stencil.scm and lily-library.scm.

The RESTRICTION is honoured too: a #:select clause builds an interface module
holding only the selected bindings, and THAT is what goes on the importer's
use list, so an unselected name does not arrive -- Guile's documented
resolve-interface behaviour. SelectImportTests fences both halves from both
sides: the selected or renamed name must arrive AND the unselected one must
not.

DIVERGENCE, now with an OPT-IN closure: by DEFAULT a use-modules WITHOUT
#:select puts the WHOLE module on the use list rather than its public
interface, so visible scope is WIDER than Guile's, never narrower -- the
behaviour the LilyPond layer's module world was verified under. Setting
Interpreter.NarrowModuleImports = true BEFORE loading code closes it: such a
clause then imports the module's public interface, as Guile documents -- only
exported names arrive, through a LIVE view that keeps growing with the
module's exports (an export made after the import still reaches the
importer, and the view answers the module's own variable cells, so set!
works through it). #:select clauses and the implicit core import behave
identically in both settings. NarrowImportTests fences both positions of the
switch. Flipping the DEFAULT still wants a session that can sweep the
LilyPond layer behind it.

A define-module clause keyword may be spelled #:export or as the
keyword-like SYMBOL :export -- boot-9 normalizes the latter with
keyword-like-symbol->keyword, and define-module* here does the same. The
vendored srfi-1.scm uses exactly that spelling; before the normalization its
whole export list went unrecorded, which the wide import silently hid.

WHEN TWO IMPORTS BIND ONE NAME, THE FIRST IMPORT WINS -- A MEASURED
DIVERGENCE, KEPT. Guile's duplicate-binding handlers (default chain
(replace warn-override-core warn last)) resolve toward the LAST module used
and honor #:replace; a newest-first search reproducing that was BUILT AND
REVERTED the same day, because it broke macro resolution across the LilyPond
layer's module world (make-engraver read as an unbound variable, seven
engraving tests red) -- that world's scope chains were verified under
first-wins. The practical cost is confined to names a module #:replace's
over core: (srfi srfi-43)'s vector-copy / vector->list / list->vector
resolve to the CORE bindings for importers, and the core vector-copy now
takes [start [end]] (libguile/vectors.c's own signature), so the common
arities agree; only srfi-43's fourth (fill) argument is out of reach. Do not
reintroduce newest-first without budgeting a full corpus sweep and suite run
for it. A module's OWN binding beats every import in both readings.

EXPORTS ARE TRACKED, AND THE PUBLIC INTERFACE IS NARROW
-------------------------------------------------------
That divergence is about the IMPORT side and is unchanged. The EXPORT side is
now Guile's: every module carries the set of names a define-public, an `export'
clause or a #:export / #:re-export / #:export-syntax / #:replace keyword named,
and module-public-interface answers a module holding exactly those, bound to the
SAME variables (module-add! semantics, so a set! through either side is seen by
both). A plain `define' is not in it.

The interface is built fresh on every ask rather than cached, because a module
goes on growing: LilyPond loads more than fifty files INTO (lily) after the
module is created, and an interface captured on first ask would answer for
whatever had been exported at that moment.

Returning the whole module here instead is not a small thing. LilyPond generates
its Internals Reference by walking (module-public-interface (resolve-module
'(lily))) and documenting every procedure in it that has a docstring, so the
wide answer documented eighty-two private helpers upstream does not.

SOURCE LOCATIONS, AND WHAT DEPENDS ON THEM
------------------------------------------
The reader records where it read each pair and vector -- file, ZERO-based line,
zero-based column -- into Reader/SourceProperties, a weak table keyed by object
identity, and source-properties reads it back as Guile's
((filename . F) (line . L) (column . C)) alist.

That table is not decoration: psyntax's datum-sourcev (ice-9/psyntax.scm:307-312)
asks source-properties and NOTHING ELSE, then threads the answer through
expansion as the src field of every Tree-IL node. With the table empty the
expander has nothing to propagate, so every node carries #f, every procedure
prints as anonymous, and no error message can name a file -- all silently.

Two consequences worth knowing:

* THE LINE IS ZERO-BASED AND THE COLUMN IS NOT. Guile's own
  source-line-for-user (system/vm/debug.scm:673-674) is (1+ (source-line s)),
  and nothing adds anything to the column. Anything showing a location to a
  human adds the one back.

* A REWRITE THAT REBUILDS PAIRS DESTROYS LOCATIONS. Properties are keyed by
  object identity, so `new Pair(...)' over a form that already had one drops it.
  CurriedDefinitions runs over every form of every file before psyntax sees it,
  and rebuilding unconditionally erased the entire layer's locations; it now
  returns the ORIGINAL object when nothing changed, and copies properties across
  when a rebuild is genuinely needed. Forms a rewrite INVENTS inherit the
  location of the form they came out of, which is what Guile does for
  macro-introduced code and what makes a curried definition's inner procedure
  report the whole definition's position.

PROCEDURES PRINT AS GUILE PRINTS THEM -- INCLUDING THE LATCH
-------------------------------------------------------------
Printer renders a procedure the way system/vm/program.scm's print-program does:
"#<procedure" then either the NAME or the object address in hex, then -- for an
unnamed procedure that knows where it came from -- " at file:line:column", then
the parameter list (arguments-alist->lambda-list: required names, then
#:optional, then #:key, then a rest parameter as an improper tail; a
C-implemented procedure's parameters are all the placeholder `_').

procedure-name answers the `name' PROCEDURE PROPERTY before the definition-time
name, as scm_procedure_name does. Code names procedures after the fact and
relies on it: LilyPond's define-markup-command-internal builds each markup
command with a helper -- so it is anonymous -- and then names it with
(set-procedure-property! definition 'name command-name).

⚠ AND THE RE-ENTRY LATCH IS REPRODUCED ON PURPOSE. libguile/programs.c's
scm_i_program_print sets a file-static print_error flag, calls out to the Scheme
printer, and clears the flag afterwards; while it is set, it prints the
low-level "#<program ADDR CODE>" instead. It is a guard against a printer that
errors -- and it never recovers, because pretty-print writes through a
truncating soft port that abort-to-prompts mid-write, and an abort that lands
inside the printer leaves the flag SET for the rest of the process. From then on
every procedure in that process prints in the low-level form. LilyPond knows:
scm->string carries a regex whose only job is to normalise it, and the generated
manual shows that form 206 times against 29 ordinary ones. Printer holds the
latch across the EMIT rather than the render, because the render builds a string
and cannot abort; Printer.ResetProgramPrintLatch is for a host that runs many
input files in one process, where the faithful reset point is the per-file
boundary rather than process exit.

SOFT PORTS ARE BLOCK-BUFFERED, AND THE BUFFERING IS OBSERVABLE
---------------------------------------------------------------
A soft output port buffers 1024 bytes. A write that fits is appended, and a full
buffer flushes; a write that does not fit tops the buffer up by whole 252-byte
QUANTA and flushes, so an empty buffer transfers 1008 = 4 x 252 at a time and
leaves 16 bytes unused. Both constants were measured against the pinned oracle
and then confirmed by prediction on fills the model had not been shown.

This is not an internal detail. pretty-print's truncating writer aborts from
INSIDE write-string, so when the buffer flushes decides where that abort lands
-- and an abort landing inside the procedure printer latches print_error for the
rest of the process. Writing straight through made procedures start printing in
the low-level form earlier than Guile does, and differed on twenty-four entries
of a generated manual. SoftPortBufferingTests fences the model.

A DOCSTRING ON A MACRO GOES ON THE TRANSFORMER
-----------------------------------------------
define-syntax-rule passes a docstring through to syntax-rules, which puts it on
the transformer procedure (expand-syntax-rules, ice-9/psyntax.scm:3186-3197);
defmacro and defmacro* hoist it onto the transformer lambda themselves, as
boot-9's define-macro does (ice-9/boot-9.scm:735-757), leaving the user's body on
an inner lambda without it.

Getting this wrong is invisible to every ordinary use of the macro and visible to
exactly one reader: documentation generators ask procedure-documentation of
(macro-transformer m) and skip any macro that answers #f. Every macro LilyPond
documents this way was silently missing from its manual.

eval MAKES ITS MODULE ARGUMENT CURRENT
--------------------------------------
(eval form module) sets the current module for the whole call, which is what
Guile's own eval does -- save-module-excursion around set-current-module. This
is not tidiness: psyntax resolves free identifiers AT EXPANSION TIME against
(current-module), so expanding in one module while evaluating in another yields
references bound to the wrong namespace, and every one of them fails as unbound
no matter what the target module contains. The tell is two mechanisms
disagreeing about one module -- module-defined? answering #t for a name that
eval then cannot find.

The excursion belongs in the eval PRIMITIVE and nowhere else. Putting it inside
TreeIlEvaluator.ExpandAndEval looks equivalent and is not: that is also the
per-form loader path, and a (define-module ...) at the head of a file takes
effect BY changing the current module, so restoring it afterwards silently
undoes the declaration and every later form in the file lands in the caller's
module.

A HOST THAT AUTOLOADS MODULES MUST SAVE THE CURRENT MODULE
----------------------------------------------------------
ModuleRegistry.ModuleLoader is called from Resolve to load a module's source.
SchemeBootstrap's own vendored-module loader wraps that load in a save/restore
of Interpreter.CurrentModule, and a host installing its own loader must do the
same -- Guile's autoloader is a save-module-excursion for this reason. The file
being loaded opens with (define-module ...), which makes ITS module current and
never puts the old one back, so an autoload triggered from a use-modules line
redirects every later definition in the file that triggered it. CodeBrix.LilyPort
hit exactly this: 668 bindings that belonged in (lily) were defined in
(lily curried-definitions) instead, and it went unnoticed for a long time
because (lily) uses that module, so ordinary lookups still found everything.
What it broke was SHADOWING -- GOOPS methods specialising +, -, * and < on host
types were found only after the root module's arithmetic had already answered.

THE EXPANSION CACHE (CodeBrix.LilyScheme.Caching)
-------------------------------------------------
Measured over a full LilyPort engine boot, ~99% of loading a Scheme layer is
psyntax macro-expansion (the expander itself runs interpreted); evaluating the
expanded Tree-IL is milliseconds. The cache removes the expansion: assign an
ExpansionCache to Interpreter.ExpansionCache and SchemeBootstrap.LoadExpanded
records each file's expanded Tree-IL on first load and substitutes it on later
loads, keyed per file by name + source SHA-256. Everything is still EVALUATED
live, in order — nested loads, module switches and load-time side effects
behave exactly as an uncached boot.

Four rules, each of which has already drawn blood:

* RECORDING RUNS psyntax IN c&e MODE, AND MUST NOT RE-EVALUATE. In the
  default e mode a top-level define-syntax installs its macro purely as an
  expansion-time side effect and returns void-ish Tree-IL — a replayed boot
  rebuilds every value binding and NO macros, and dies on the first LIVE
  expansion afterwards (LilyPort's ly-syntax-constructors autoload). c&e is
  upstream's own file-compilation mode: the expander EVALUATES each form
  itself and returns Tree-IL that rebuilds the same state. Because the
  expander already evaluated, LoadExpanded records WITHOUT evaluating again —
  re-evaluating re-runs every form, and a re-run (define-module ...)
  re-creates the module out from under the expander's own state.
* NEVER SHARE AN INSTANCE BETWEEN INTERPRETERS. Recorded quoted constants
  become live, MUTABLE runtime data when evaluated. Deserialize one instance
  per interpreter (the file's bytes can be memoized; the graphs cannot).
* IDENTITY IS PART OF THE FORMAT. Gensym lookup is reference equality, so the
  serializer keeps an object table and every repeated heap object round-trips
  to ONE object. Uninterned symbols therefore can never collide with live
  ones — no counter management exists or is needed.
* A CACHE MUST NEVER BE ABLE TO FAIL OR FALSIFY A BOOT. The file carries the
  caller's world-signature key and a SHA-256 of the payload; any mismatch,
  truncation or corruption is a MISS (TryReadFile answers null), and the boot
  records live again. Unknown value types THROW at record time — the boot
  keeps its live result and simply saves nothing.

The KEY is the caller's job: it must change whenever anything that shaped the
expansion changes. CodeBrix.LilyPort keys on the LilyScheme + Engine assembly
MVIDs plus every embedded .scm resource's content (see BootExpansionCache
there); note the family's minute-stamped versioning means ANY rebuild changes
the MVIDs, so the first boot after a rebuild re-records once (~half a minute)
and every boot until the next rebuild replays (~50 ms for the whole layer).

ExpansionCacheTests fences all four rules.

DOCSTRINGS, AND THE FOUR PLACES THEY GO WRONG
----------------------------------------------
A string as the first of SEVERAL body forms is a docstring; a lone string body
is the return value. psyntax already separates the two for you -- its parse-body
(ice-9/psyntax.scm:2088) lifts the docstring into the lambda's META alist under
`documentation', so TreeIlEvaluator reads it from there rather than inspecting
the body. procedure-documentation answers that, or an explicitly set
procedure-property of the same name, or #f. It does NOT answer the procedure's
name; it used to, which put every markup command's own name in LilyPond's
generated manual where its description belonged.

A CURRIED definition carries its docstring on the OUTERMOST lambda:
(define ((f a) b) "doc" body) documents f, not (f 1). Guile's
ice-9/curried-definitions.scm hoists it deliberately -- "Keep moving docstring
to outermost lambda" -- and CurriedDefinitions.cs does the same in its rewrite.
Leaving it on the inner lambda is invisible in every ordinary use.

GOOPS EVALUATES A SLOT OPTION'S VALUE. #:init-value '() means the empty list,
not the two-element list (quote ()). define-class therefore emits the slot
specs as a runtime `list' call with the init-value and init-thunk expressions
left unquoted, rather than handing %make-class the whole spec as quoted data.
Quoting it makes every (pair? slot) guard downstream take the wrong branch over
a slot that is supposed to be empty. #:accessor, #:getter and #:setter stay
quoted, because the macro is itself about to define those names.

char-ci<? AND ITS FAMILY FOLD UPWARD. libguile/chars.c compares
scm_c_upcase (x) against scm_c_upcase (y) in all five comparisons, so every
letter sorts BELOW the punctuation that sits between the two ASCII cases --
[ \ ] ^ _ ` -- instead of above it. Folding down agrees with Guile on every
pair of letters and disagrees on every letter-versus-backslash pair, which
surfaces only when something sorts identifiers beginning with one.

PORTS ARE FLUSHED BY WHOEVER OWNS THE RUN
------------------------------------------
open-output-file takes Guile's #:binary and #:encoding keywords, mirroring the
input side, and never writes a byte-order mark. The ports it hands out are
BUFFERED and Scheme code is entitled not to close them: Guile flushes every open
port as the process exits (libguile/init.c:332). An embedded interpreter has no
exit to hang that on, so flush-all-ports is bound for the host to call when a
run is over. Skip it and output files end at a buffer boundary -- the tell is a
set of files whose sizes are all multiples of 1024.

load-from-path is likewise boot-9's, and it is Scheme (in the prelude) rather
than a C# primitive on purpose: it resolves primitive-load-path WHEN IT RUNS, so
a host that replaces that name -- as CodeBrix.LilyPort does, to serve LilyPond's
layer from embedded resources -- is honoured. primitive-load-path itself applies
%load-extensions the way libguile/load.c's search_path does: (".scm" ""), except
that a name already ending in .scm is searched for as it stands.

A HOST PATH REACHES SCHEME THROUGH Printer.WriteString
-------------------------------------------------------
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
through the reader by contract (SchemeReaderTests fences it, Windows and POSIX
shapes both). Hand-doubling backslashes at the call site is NOT the same thing:
it leaves a quote in a path unescaped.

The reader is not the thing to change here. Guile on Windows reads paths exactly
this way, its \uXXXX / \UXXXXXX widths are fenced deliberately, and making the
reader lenient would diverge from Guile on every platform to paper over a
caller's bug.

READS TOLERATE A CONCURRENT WRITER
-----------------------------------
Windows ENFORCES share modes and POSIX does not, so File.ReadAllBytes's default
FileShare.Read refuses a file that anything else holds open for writing. Scheme
here is entitled to leave a port open -- ports are buffered and closing them is
the host's job (see PORTS ARE FLUSHED BY WHOEVER OWNS THE RUN) -- and Guile on a
POSIX host reads such a file without complaint, seeing whatever has been flushed.

Every read behind open-file, open-input-file, call-with-input-file and load
therefore goes through Runtime/HostFile, which asks for FileShare.ReadWrite. That
changes nothing on Linux or macOS, where the share mode was never consulted; what
it removes is the SAME Scheme program throwing on one platform and not the others.

WRITING BYTES, AND THE FILESYSTEM FAMILY
-----------------------------------------
Scheme code produces binary output the only way Scheme can: set an 8-bit
codec on the port and write one character per octet. set-port-encoding! is
therefore REAL here -- on an open output file port it flushes the writer and
reopens it appending with the new codec (no BOM), as Guile changes a live
port's codec without discarding its file. It was once a no-op that accepted
its arguments and did nothing, which turned every octet above 0x7F into two
UTF-8 bytes -- nothing failed, and the corruption was only ever visible to
whatever later READ the file. A stub that answers plausibly is worse than
one that throws; BinaryPortTests fences the whole path.

read-char and peek-char take the standard optional port. The directory
family exists too: opendir / readdir / closedir over a directory-stream
object, plus mkdir, rmdir and delete-file, throwing Guile's system-error
shapes on a missing path or a non-empty directory. LilyPond's
document-supplied-font machinery walks and cleans temporary directories
with exactly these.

DELIMITED READING, AND THE CURRENT INPUT PORT
---------------------------------------------
(ice-9 rdelim) is vendored VERBATIM: read-line with all four handle-delim
modes, read-delimited, read-string and their filling variants are Guile's own
Scheme, running over three C-side names from libguile/rdelim.c -- %read-line
(answering (line . delimiter), with (#<eof> . #<eof>) at end of file),
%read-delimited! (answering (terminator . nchars), pushing an ungobbled
delimiter back) -- plus unread-char, whose pushbacks stack most recent first
as scm_ungetc's do. write-line is libguile/rdelim.c's too, and it DISPLAYS:
it rendered through the write printer for a time, which put quotes around
every string it wrote, and RdelimTests caught it the day the module was
vendored.

(current-input-port) exists and reads the interpreter's InputReader, which
defaults to the process's standard input; an embedding host substitutes its
own TextReader, mirroring OutputWriter and ErrorWriter. An input port can now
also STREAM from a TextReader (the pipe shape) instead of holding its whole
text up front -- but a stream-backed port refuses `read', loudly, because the
datum reader works over a string and buffering a live pipe to end-of-stream
would block on the producer.

SUBPROCESSES
------------
system, system* and the status:exit-val / status:term-sig / status:stop-sig
decoders are core, and (ice-9 popen) is the FIFTH shim module: open-pipe,
open-pipe*, open-input-pipe, open-output-pipe and close-pipe over
System.Diagnostics.Process, with OPEN_READ / OPEN_WRITE / OPEN_BOTH bound in
the core as Guile binds them. A read pipe captures the child's standard
output and inherits its input; a write pipe the reverse; close-pipe closes,
waits, and answers the encoded wait status. OPEN_BOTH and
open-input-output-pipe are REFUSED loudly -- a port here is a reader or a
writer and never both (the open-file rule) -- and PopenTests fences the
refusal so a half-implementation cannot slip in.

DIVERGENCE, recorded on PosixPrimitives.EncodeWaitStatus: .NET reports a
signal-killed child as exit code 128+signal (the shell convention) rather
than through a separate WIFSIGNALED channel, so such a child decodes through
status:exit-val (137 for SIGKILL, as a shell's $? shows) and status:term-sig
answers #f.

THE POSIX SURFACE: stat AND BROKEN-DOWN TIME
--------------------------------------------
stat and lstat build libguile/filesys.c's 18-slot vector; localtime and
gmtime build libguile/stime.c's 11-slot tm vector. THE ACCESSORS ARE GUILE'S
OWN: the vendored ice-9/posix.scm (stat:, passwd:, group:, utsname:) is
loaded by the prelude with the same include-from-path that loads
quasisyntax, and the tm: family is copied verbatim into the prelude from
boot-9.scm:2037 (boot-9 never loads here). posix.scm's tail defines
getpwent-family wrappers over getpw/setpw/getgr/setgr, which are UNBOUND --
calling one is a visible unbound-variable error, the ABOUT boot-9 posture.

The stat slots .NET cannot answer truthfully -- dev, ino, nlink, uid, gid,
rdev, blksize, blocks -- hold #f DELIBERATELY, a visible non-answer rather
than a plausible zero; on Windows, mode and perms are #f too. The tm
conventions are struct tm's exactly (mon 0-based, year from 1900, and
tm:gmtoff seconds WEST of UTC, Guile's documented sign), fenced against
epoch 0 being Thursday 1970-01-01. strftime implements the common C
directives over the vector itself and copies an unrecognised conversion
through verbatim, as glibc does; localtime's optional TZ argument is refused
loudly, because mapping POSIX TZ strings onto .NET time zones would be a
guess.

POSIX REGULAR EXPRESSIONS
-------------------------
The surface is libguile/regex-posix.c's, exactly enough for the vendored
ice-9/regex.scm to load VERBATIM on top -- string-match, fold-matches,
regexp-substitute/global and the match: accessors are Guile's own Scheme.

* make-regexp takes flag INTEGERS as separate rest arguments and detects
  regexp/basic by equality with 0, as scm_make_regexp does. Extended is the
  default; regexp/icase and regexp/newline map onto the .NET options.
* regexp-exec answers Guile's match VECTOR: slot 0 the target string, slot
  i+1 the (start . end) pair of group i, (-1 . -1) for a group that did not
  participate -- so match:substring answers #f for an unmatched group, NEVER
  an empty string. LilyPond's output-svg.scm reads exactly that with
  (string? (match:substring m 1)).
* The pattern dialect is POSIX ERE translated onto .NET's engine. Three
  constructs are translated, all inside bracket expressions: [[:class:]]
  (with digit and xdigit fixed to ASCII, where .NET's \d is Unicode -- the
  discriminating fence), a ] in first position (a POSIX literal), and a
  backslash (a POSIX LITERAL inside brackets, an escape in .NET). Everything
  else passes through -- ERE is a subset of .NET's syntax there.
* ^ matches AT a start offset by default (regex-posix.c searches the
  substring); regexp/notbol is what turns that off, and .NET's
  Match(input, startat) is exactly notbol's shape.
* REFUSED loudly rather than half-served: regexp/basic (BRE is a different
  grammar), regexp/noteol (no .NET equivalent), and the [. .] / [= =]
  collating forms.
* DIVERGENCE, recorded: alternation is .NET's leftmost-FIRST, not POSIX's
  leftmost-longest. A pattern like (a|ab) prefers the first alternative
  where POSIX regexec takes the longest match. Nothing in the corpus or the
  fences turns on it; a caller that does gets its own ruling that day.

THE MODERN EXCEPTION API, AND HOW IT MEETS catch/throw
------------------------------------------------------
Guile 3's exception objects are here: raise-exception, with-exception-handler
(#:unwind? and #:unwind-for-type included), exception?, make-exception and the
compound/simple object model, make-exception-type, exception-predicate and
exception-accessor (both reaching through compounds), exception-kind and
exception-args are core, built to boot-9.scm:1448-1861 in
Primitives/ExceptionPrimitives.cs. (ice-9 exceptions) is vendored VERBATIM on
top and supplies the standard types (&message, &warning, &external-error,
&assertion-failure, ...), define-exception-type, raise-continuable, R7RS
guard, and the converter table that turns native throw keys into typed
exception objects -- it upgrades the core's boot make-exception-from-throw
with set!, exactly as it does in Guile, so the conversion sites read that
variable LIVE.

The interop is BOTH WAYS and is the load-bearing part: catch sees a raised
exception through its kind and args (a plain object raises with kind
%exception), and an exception handler sees a plain throw -- including every
SchemeThrow a C# primitive raises -- as the converted exception object, so
(guard (e ((assertion-failure? e) ...)) (car 5)) works. The dispatch design
differs from boot-9's fluid walk because the classic side here is .NET
exceptions rather than prompts: a non-continuable raise-exception simply
throws the SchemeThrow its object decodes to, and .NET propagation visits
every intervening frame innermost first -- which IS boot-9's handler-stack
order. Only raise-continuable walks an explicit per-interpreter handler stack
(catch, with-throw-handler and both with-exception-handler modes register
frames on it), because a non-unwinding handler's return value must flow back
to the raise point and no .NET throw can do that. A non-unwinding handler
runs INSIDE an exception filter, pre-unwind; its non-local exits are carried
out through the catch block and rethrown, because the CLR silently swallows
any exception escaping a filter -- guard's clause dispatch escapes its
handler by abort-to-prompt and depends on this.

DIVERGENCE, recorded: a non-local exit from a non-unwinding handler continues
from the with-exception-handler frame rather than from the raise point, so
frames BETWEEN the two do not see it -- the same bounded shape as the
with-throw-handler divergence in ControlPrimitives. guard is unaffected (its
prompt sits outside its handler).

The exception types stand on Guile's single-inheritance RECORD model, now
carried in full by make-record-type: #:parent lays out the parent's fields
FIRST, record-type-fields answers the complete layout, a record-predicate
accepts subtype instances, only an #:extensible? #t type may be a parent
("parent type is final"), a field spec may be (immutable name) /
(mutable name) and an immutable field refuses record-modifier,
record-type-name answers the SYMBOL, and a record IS a struct to struct-vtable
and struct-ref (fields counted from 0, the type slot skipped) -- which is what
exceptions.scm's own printer indexes by. Records print as boot-9's
default-record-printer prints them: #<type-name field: value ...>.

print-exception and set-exception-printer! are core (C#); the standard per-key
printers -- scm-error-printer and friends, boot-9.scm:1917-1979 minus
getaddrinfo-error -- are registered by the prelude, and (ice-9 exceptions)
registers its '%exception printer when it loads. There are no stack frames
here, so print-exception accepts and ignores its frame argument.

READER EXTENSIONS
-----------------
SchemeReader.RegisterHashExtension(char, handler) is the equivalent of Guile's
read-hash-extend: it takes over one '#' dispatch character, and it takes
precedence over the built-in syntax for that character. CodeBrix.LilyPort
registers '{' for LilyPond's #{ embedded music #}.

SHARP EDGE: psyntax-pp.scm itself contains Guile extended symbols such as
#{ $sc-ellipsis }#. SchemeBootstrap.LoadCore therefore SUSPENDS every
registered extension while it reads Guile's own source, and restores them
afterwards. Skip that and a second interpreter in the same process reads
psyntax as embedded music and the expander is corrupted with no error.

quasisyntax is NOT part of psyntax. Guile pulls it into the core environment
from boot-9.scm line 424; the prelude does the same include, because #`
templates are what LilyPond's scm/music-functions.scm is built on.

A VECTOR IS AN ARRAY
--------------------
libguile/arrays.c's scm_is_array counts scm_tc7_vector, so array?, array-ref,
array-set!, array-rank and array-dimensions all take a plain vector as the
rank-1, zero-based case -- no conversion, and array-set! writes THROUGH to the
vector. LilyPond's qr-code.scm depends on it: its format-information tables are
written as #(...) literals and read with array-ref, and refusing them stops the
whole file with "Not an array".

DIVERGENCE: scm_is_array also counts strings, bitvectors and bytevectors.
Those are not accepted here, because nothing has asked for them yet; a caller
that does will get the same "Not an array" a missing name would give, which is
a visible failure rather than a wrong answer.

COMPLEX NUMBERS ARE READ AND CALCULATED WITH
--------------------------------------------
Guile's rectangular and polar literals read as numbers: 1+0i, 0+1i, -1-0.25i,
+i, -i, 2@1.57. +, -, * and / accept a complex operand, and make-polar,
make-rectangular, magnitude, angle, real-part and imag-part are bound. The four
accessors deliberately accept a REAL too, because a real IS a complex with a
zero imaginary part.

AN EXACT ZERO IMAGINARY PART COLLAPSES TO THE REAL, in the reader. 1+0i IS the
exact integer 1 in Guile, while 1.0+0.0i stays complex. That is not a
simplification: scm/stencil.scm's arrow-stencil-maker binds e_x to 1+0i and
multiplies coordinates by it, so keeping the zero would make every arrow
coordinate inexact. For the same reason a product with an EXACT zero answers
exact 0, whatever the other operand is -- the middle vertex of every arrow head
is the literal 0, rotated by a complex and read back with real-part.

number? and complex? are THE SAME predicate, as they are in R7RS and Guile.
real? and rational? are NOT, and are spelled out separately rather than sharing
the tower's IsNumber, so a later widening cannot take them along silently.

The parts are doubles, so a complex here is always INEXACT; Guile's exact
complexes are not modelled.

SHARP EDGE: a token ending in '@' is not a polar literal, and psyntax's own
source contains such symbols. Both sides of the '@' must exist before either is
parsed.

WHY sort IS A MERGE SORT
------------------------
List<T>.Sort is an introsort that VALIDATES its comparer and throws
"IComparer.Compare() returns inconsistent results" when the Scheme predicate is
not a strict weak ordering. LilyPond passes predicates that are not. A merge
sort asks only "does b come before a", so it copes -- and it is stable, which is
what stable-sort promises. Do not swap it back.

append! RE-LINKS, AND THE IDENTITY IS THE POINT
----------------------------------------------
append! is not "append, but faster". It rewrites the last pair's cdr of each
argument to point at the next one and answers the first non-empty argument, so
a variable that held one of the inputs afterwards holds the CONCATENATION --
the pairs it is made of ARE the result's pairs. Aliasing it to the copying
append is invisible in every use that only reads the return value and wrong in
every use that does not: LilyPond's add-to-tag-group re-registers
(append! tag-group tags) and lets the caller's own \tagGroupRef variable track
the group, so under a copying append the variable kept the group as it was
BEFORE the change and the next lookup answered "tag group (foo bar) not found",
quoting the stale contents. The same shape reaches base-tkit.ly's
variable-names and every (append! (ly:music-property m 'elements) ...) in
music-functions-init.ly. The LAST argument is attached as it stands: never
walked, and not required to be a list.

WHY case BINDS ITS KEY ONCE
---------------------------
The prelude's case macro rebinds a compound key expression in a let before
dispatching (R7RS 7.3's own pattern). The clause recursion splices the key
into every memv test, so without that rebind a side-effecting key evaluates
once PER CLAUSE -- and ice-9/format.scm dispatches on
(case (char-upcase (next-char)) ...), which then silently re-read the format
string, ran off its end, and recursed through format-error until the process
died with an uncatchable stack overflow. Do not "simplify" the extra rule
away, and treat any silent SIGABRT in Scheme-heavy code as a possible
once-per-clause evaluation somewhere until proven otherwise.

ABOUT boot-9.scm
----------------
It is vendored but NOT loaded. boot-9 builds Guile's module system,
record types, port types and exception hierarchy from scratch on
low-level vtable layouts, and it opens by asserting (current-module)
is #f because it runs before any module system exists. LilyScheme
supplies the module system from C#, so boot-9 cannot load verbatim.
Scheme/lilyscheme/prelude.scm provides the derived syntax instead --
and, or, cond, case, when, unless, do, let-values, define-values,
receive, and-let*, while, parameterize, cond-expand, defmacro, the
define-module / use-modules / define-public family, and (ice-9 optargs)'s
let-keywords / let-keywords*.

The last pair are in the prelude rather than loaded from the vendored
optargs.scm because that file's expansion calls parse-lambda-case, a Guile VM
primitive with no analogue here. lambda* already carries the whole keyword
protocol in C#, so both macros expand to a lambda* applied to the rest list.

Note that psyntax's core forms are only quote, if, lambda, let,
letrec, begin, set! and define. Even `and` and `or` are boot-9 macros,
which is why the prelude has to define them.

boot-9 also defines a good many ordinary PROCEDURES -- identity, const,
and=>, ->bool and their neighbours -- and because the file never loads,
none of them exists unless something puts it there. Those live in
Primitives/CorePrimitives.cs. Treat "it is in Scheme/ice-9/boot-9.scm"
as saying nothing at all about whether a name is bound: that file is
reference material, and a name found only there is a name that is
UNBOUND at runtime. Check with (defined? 'name) rather than by grepping
the vendored source -- a diagnosis of "present but not visible from this
module" is almost always really "never loaded".

The same applies to Guile's port procedures, which live in ice-9/ports.scm
and ice-9/textual-ports.scm rather than in boot-9 and are likewise not
loaded. open-input-file, call-with-input-file, call-with-port,
get-string-all and get-string-n are implemented in Primitives/PortPrimitives.cs,
core-side rather than in a module, which is the standing posture here:
LilyScheme's scope is deliberately WIDER than Guile's per-module scope and
never narrower. call-with-input-file and open-input-file take Guile's
#:binary / #:encoding / #:guess-encoding keywords, and #:encoding is
load-bearing rather than decorative -- LilyPond reads every file into a
string through one procedure that passes it, asking for latin1 in one
spelling and UTF-8 in another.

open-file IS A DIFFERENT PROCEDURE, NOT A SPELLING OF THOSE. It is
libguile/fports.c's scm_open_file and takes a MODE STRING -- "r", "w", "a",
and the "b" flag that means one character per byte -- rather than keywords.
scm/backend-library.scm and scm/framework-ps.scm reach for it six times
between them, to write a header field and to copy EPS and PostScript bytes.
The direction characters select an input or an output port; "+" is refused
loudly, because a port here is a reader or a writer and never both.

file-port? asks whether a port's implementation is the FILE one, which is NOT
the same question as whether it has a name -- a string port carries the name
<string> in Guile too. scm/graphviz.scm's graph-write gates a port-filename
call on it and is handed (current-error-port) by its own regression file, so
answering by name takes the wrong branch and prints the graph's destination
as #f.

close-port is Guile's ANY-port close, and for a FILE port it disposes the
writer rather than merely flushing it -- scm/backend-library.scm opens a
header field's file, displays to it and closes it, and never calls
flush-all-ports. The current output and error ports are deliberately NOT
disposed by it: those writers belong to the host and must survive being
closed from Scheme.

THIRD-PARTY / LICENSING NOTE
-----------------------------
This library is licensed LGPL-3.0-or-later because it incorporates
source from the GNU Guile project, which is itself LGPL-3.0-or-later.
Twenty-nine .scm files from the GNU Guile source tree are vendored
VERBATIM under Scheme/ and shipped as embedded resources -- see
THIRD-PARTY-NOTICES.txt at the repo root for the per-file attribution
ledger, which records seven additional copyright holders beyond the FSF,
one public-domain file, and one file (ice-9/quasisyntax.scm, Andre van
Tonder) under its own MIT-style grant rather than the LGPL. The C# is
new-in-family: it was written against R7RS, the SRFI documents and
libguile/expand.h, not translated from Guile's C.

Never edit a file under Scheme/ that came from Guile. They are
verbatim by design, which is what makes re-syncing a straight copy
plus cmp. Scheme/lilyscheme/prelude.scm is ours and may be edited.

AND THEY MUST BE LF. Every file under Scheme/ is an <EmbeddedResource>,
so the bytes on disk at BUILD time are baked into the assembly and
shipped in the package -- a CRLF working tree is not a local
inconvenience, it is a broken artifact for every consumer on every
platform. A CR is whitespace between forms and is NOT whitespace inside
a string literal, and the multi-line format-directive literals of
ice-9/format.scm are full of them: format's parser then runs off the end
of its string and recurses through format-error until the process dies
with an uncatchable stack overflow. Nothing warns. (It is the same
observable failure as the once-per-clause `case' bug in WHY case BINDS
ITS KEY ONCE, from an unrelated cause -- so read a silent stack overflow
in Scheme-heavy code as EITHER.)

Two layers hold it, and both are load-bearing:

* .gitattributes at the repo root pins *.scm to eol=lf. The committed
  blobs are already LF, so this only stops a Windows checkout
  (core.autocrlf=true) expanding them on the way out -- which also keeps
  the vendored files byte-identical to Guile's for the cmp re-sync.
* SchemeBootstrap reduces CRLF to LF as it reads each resource. The
  .gitattributes governs a CHECKOUT and nothing else; this is what makes
  the ARTIFACT correct when the bytes arrive some other way -- a source
  zip, a contributor configured differently, an editor that saves CRLF.
  Only the PAIR is rewritten; a lone CR is left alone, because silently
  rewriting a deliberate carriage return inside a string literal would be
  the same class of quiet corruption. SmokeTests sweeps every resource.

The LGPL compliance obligations documented in
~/GitHome/CodeBrix.Library.Dev-private/info/LGPL_GUIDANCE.txt apply.

TESTING
-------
    dotnet test CodeBrix.LilyScheme.slnx

The suite's classes, by what each one fences:

    SchemeReaderTests       reader coverage: numbers, the numeric
                            tower, strings, characters, keywords,
                            vectors, #{...}#, #nil, comments, and the
                            Printer.WriteString round trip that a host
                            path depends on -- Windows and POSIX shapes,
                            including the paths whose next character
                            spells a VALID escape and would otherwise
                            read clean as a DIFFERENT path, with the raw
                            \U splice fenced as the control
    InterpreterTests        the core evaluator without psyntax --
                            arithmetic, closures, tail calls, letrec,
                            lambda*, hash tables, catch/throw, and the
                            throw-handler contract: with-throw-handler
                            runs its handler BEFORE the stack unwinds
                            (real pre-unwind semantics via .NET
                            exception filters) and the throw then keeps
                            propagating; catch honours its optional
                            pre-unwind handler the same way
    PsyntaxBootstrapTests   the milestone gate: psyntax loads,
                            macroexpand returns Tree-IL, syntax-rules
                            runs, macro expansion is HYGIENIC, and the
                            vendored srfi-1 and ice-9 match both load
                            and work
    GuileCompatibilityTests the surface beyond psyntax: module
                            autoloading, quasisyntax (including a tail
                            unsyntax), the non-finite reals, SRFI-13
                            strings, SRFI-14 character sets, SRFI-9
                            records, generalized setters, stable
                            sorting, GOOPS over built-in classes, string
                            ports, reader hash extensions, once-evaluated
                            case keys, format directives, format errors
                            propagating as catchable throws, list-copy
                            preserving improper tails, Guile arrays
                            (literals, make-shared-array views, transpose),
                            the bitwise family (including logcount),
                            procedure-arguments via the synthesized
                            'arglist property, the (system vm program) /
                            (ice-9 iconv) / (ice-9 soft-ports) shims,
                            escape-only prompts (aborts pass through
                            catch), put-string, SRFI-13 string= and
                            string-concatenate-reverse, module-map,
                            (ice-9 list)'s rassoc family, and
                            pretty-print writing and wrapping through
                            the soft-port machinery
    AnonymousModuleMacroTests  the lazy naming-and-registration of
                            anonymous modules that lets psyntax resolve
                            imported macros inside them, plus defined?
    DocumentationSupportTests  the surface LilyPond's documentation
                            generator stands on: GOOPS evaluating a slot's
                            #:init-value, char-ci<? folding UPWARD as
                            libguile/chars.c does, procedure-documentation
                            answering a docstring rather than a name, a curried
                            definition carrying its docstring on the OUTERMOST
                            lambda, and load-from-path going through whatever
                            primitive-load-path is bound at call time
    SourceLocationTests     source locations from the reader through psyntax into
                            a procedure's printed representation -- the line shown
                            one-based and the column as it stands, a named
                            procedure showing neither address nor location, a
                            define NOT naming a value it merely computed, and the
                            program-print re-entry latch sticking after a
                            non-local exit while a normal return leaves it clear
    SoftPortBufferingTests  the soft port's 1024-byte buffer and 252-byte transfer
                            quantum, every expectation a flush sequence measured
                            against the pinned LilyPond oracle
    PrimitiveGenericTests   extending a generic-capable primitive, and that
                            the extension is visible from ANOTHER module --
                            the case the whole mechanism exists for; that
                            ordinary arithmetic still falls through to the
                            subr; generic-capability? matching Guile's own
                            declarations; the setter a #:accessor carries;
                            the renaming half of a use-modules #:select; and
                            the Guile-core names those three exposed as
                            missing -- the floor/ceiling/truncate/euclidean
                            division family, finite?, string-capitalize and
                            substring/shared
    PortProcedureTests      the file-reading layer gulp-file-with-encoding
                            stands on -- #:encoding decoding the same byte
                            two different ways, the two end-of-file
                            conventions, a port closed even when the
                            procedure throws -- plus the mode-string
                            opener: open-file round-tripping, "a" keeping
                            what "w" truncates, "rb" reading one character
                            per byte, "r+" refused rather than half-served,
                            close-port flushing an output FILE port, and
                            file-port? telling a file from a string port
                            and from (current-error-port)
    BinaryPortTests         set-port-encoding! actually switching a live
                            port's codec (latin1 octets leave one byte
                            each), the opendir / readdir / closedir /
                            rmdir / delete-file family, and
                            read-char / peek-char
    ExpansionCacheTests     the cache's four rules (see THE EXPANSION
                            CACHE above): c&e recording without
                            re-evaluation, per-interpreter
                            deserialization, identity-preserving
                            round-trips, and corruption always reading
                            as a MISS, never as a failed boot
    SelectImportTests       use-modules #:select from both sides -- the
                            selected or renamed name arrives, the
                            unselected one does not, and a clause without
                            #:select still imports the whole module
    AlistRemoveTests        assq-remove! / assv-remove! / assoc-remove!
                            unlinking exactly ONE entry -- the FIRST
                            match -- as libguile/alist.c documents
    DestructiveAppendTests  append! re-linking its arguments rather than
                            copying them (the identity IS the contract),
                            plus eval-string
    HostEqualityTests       equal? dispatching to a host object's own
                            equality handler, the way scm_equal_p ends at
                            a smob's equal_p
    NaryEqualityTests       eq? / eqv? / equal? as N-ARY predicates, and
                            the optional equality predicate of member and
                            assoc
    ListMutationTests       list-set!, to libguile/list.c's documented
                            behaviour
    ComplexNumberTests      the complex literals, arithmetic and accessors
                            described above, including the exact-zero
                            collapse
    UnicodeNameTests        char->formal-name / formal-name->char against
                            the Unicode Character Database's own contents,
                            including answering #f for algorithmically
                            named CJK and Hangul rather than deriving
    RdelimTests             (ice-9 rdelim) end to end: all four handle-delim
                            modes of read-line, read-delimited gobbling or
                            splitting its delimiter, read-string reading the
                            REMAINDER, unread-char stacking most recent
                            first, and write-line DISPLAYING (the fence that
                            caught it writing quotes)
    PopenTests              (ice-9 popen) against real child processes:
                            an input pipe streaming lines in order, an
                            output pipe reaching the child's stdin (proved
                            by the bytes on disk), close-pipe's encoded wait
                            status, and the loud OPEN_BOTH refusal.
                            POSIX-shell facts skip on Windows
    PosixTests              system's wait-status encoding (7 travels as
                            1792), system* bypassing the shell, stat's size
                            and type with the #f-for-missing arm, and the
                            broken-down-time conventions -- epoch 0 as
                            Thursday 1970-01-01 with every struct tm
                            off-by-one asserted, strftime's directives, and
                            the %s round trip through tm:gmtoff
    RegexPosixTests         the POSIX REGULAR EXPRESSIONS contract above,
                            each divergence-prone point fenced from both
                            sides: ASCII [[:digit:]] against a Unicode
                            digit, the literal backslash in brackets, ^ at
                            a start offset vs regexp/notbol, unmatched
                            groups answering #f, and the loud
                            regexp/basic and regexp/noteol refusals
    GetoptLongTests         (ice-9 getopt-long) vendored verbatim -- an
                            end-to-end fence for common-list's #:select
                            rename, (ice-9 match), (ice-9 regex) and SRFI-9
                            records all at once
    ModernExceptionTests    the modern exception API from BOTH sides of the
                            old/new interop: catch seeing a raised exception's
                            kind and args, a handler seeing a C# primitive's
                            throw as a converted &assertion-failure, the
                            pre-unwind ordering of a non-unwinding handler and
                            the &non-continuable a returning one provokes,
                            raise-continuable's value flowing back through a
                            guard's re-raise chain, #:unwind-for-type in both
                            symbol and type form, the compound object model,
                            print-exception, and the loud wrong-type-arg
                            refusals
    WrongTypeArgumentTests  the wrong-typed-argument contract from both
                            layers: a Scheme catch on 'wrong-type-arg sees
                            a primitive's type failure, the positioned
                            subr/position message, the Primitive.Invoke net
                            translating a bare cast (including one in a
                            HOST-registered primitive), the net's
                            selectivity (a primitive's own SchemeThrow
                            passes untouched), and the well-typed controls
    RecordInheritanceTests  Guile's single-inheritance record model: parent
                            fields laid out first, subtype-accepting
                            predicates, the "parent type is final" refusal,
                            (immutable name) specs refusing a modifier, the
                            struct view of a record, and the
                            default-record-printer rendering
    NarrowImportTests       the Interpreter.NarrowModuleImports OPT-IN switch
                            from both positions: exported names arrive and
                            private ones do not, the interface view is LIVE
                            (later exports arrive) and shares variable cells
                            (set! works through it), #:select is unaffected,
                            and the vendored srfi-1 and (ice-9 exceptions)
                            keep working narrow -- including macro bindings
                            through the view
    Srfi43Tests             (srfi srfi-43): the INDEX-FIRST calling
                            convention its iteration procedures have (which
                            R7RS's vector-map does not), and the ranged
                            vector-copy its importers reach through the
                            range-capable core binding
    SmokeTests              the library assembly loads at all, and every
                            vendored .scm resource arrives WITHOUT
                            carriage returns -- the sweep that stops a
                            CRLF checkout being packed into a release
                            (see THIRD-PARTY / LICENSING NOTE)

Tests that evaluate Scheme must run inside
Interpreter.RunWithLargeStack -- psyntax will overflow the default
stack otherwise. A failure on the big-stack thread reaches the caller
AS ITSELF, with its original stack trace (see QUICK START), so
assertions read error text straight off the caught exception.
