================================================================================
MAINTAINER-README: CodeBrix.LilyScheme
Notes for people and agents MAINTAINING this repository — not for package consumers
================================================================================

If you are CONSUMING the NuGet package, read AGENT-README.txt instead. This file
is about changing this repository.

PURPOSE AND SCOPE
=================
The repository produces exactly one NuGet package:

    CodeBrix.LilyScheme.LgplLicenseForever
        from src/CodeBrix.LilyScheme/CodeBrix.LilyScheme.csproj
        License: LGPL-3.0-or-later
        Consumer documentation: AGENT-README.txt (repo root, packed into the
        .nupkg)

The library is a managed, cross-platform Scheme implementation for .NET, derived
from GNU Guile, implementing the subset of Guile that LilyPond's Scheme layer
needs. Its principal consumer is the sibling CodeBrix.LilyPort project, which
loads more than ninety vendored .scm files on top of it.

REPOSITORY LAYOUT
=================
    CodeBrix.LilyScheme.slnx                the solution
    src/CodeBrix.LilyScheme/                the library (the only packable project)
    tests/CodeBrix.LilyScheme.Tests/        the xUnit v3 suite
    tools/unicode-names/                    the Unicode-table generator (see EXTRAS)
    AGENT-README.txt                        consumer documentation, packed
    MAINTAINER-README.txt                   this file
    EXTRAS-README.txt                       non-package content
    README-INDEX.txt                        map of the README files
    README.md                               human-facing overview, packed
    THIRD-PARTY-NOTICES.txt                 the attribution ledger, packed
    LICENSE / LICENSE.GPL                   both texts, packed
    icon-codebrix-128.png                   the package icon, packed
    .gitattributes                          pins *.scm to eol=lf -- load-bearing

SOURCE FOLDERS INSIDE src/CodeBrix.LilyScheme
---------------------------------------------
    Interpreter.cs      the only type at the root -- it is the entry point
    Values/             Scheme data: Symbol, Pair, SchemeStruct (plus the
                        eighteen Tree-IL vtables), SyntaxObject, Procedure,
                        RecordType, CharSet, SchemeArray
    Numeric/            the numeric tower: fixnum, bignum, Ratio, real, complex
    Reader/             SchemeReader and SourceProperties
    Runtime/            Evaluator, SchemeModule, ModuleRegistry,
                        LexicalEnvironment, Printer, SchemeBootstrap, HostFile,
                        ExceptionRuntime, CurriedDefinitions, LoadDiagnostics
    TreeIl/             TreeIlEvaluator, TreeIlClosure
    Primitives/         Core, GuileCore, Numeric, String, Vector, Array, Control,
                        Module, Port, SoftPort, Goops, PrimitiveGenerics,
                        BuiltinClasses, Unicode, Posix, Exception, TypeChecks
    Caching/            ExpansionCache and its keyed, checksummed binary format
    Scheme/             vendored Guile .scm (embedded resources) plus
                        lilyscheme/prelude.scm
    Unicode/            the formal character-name table (an embedded, deflated
                        resource derived from Unicode's UnicodeData.txt) and its
                        reader

/!\ Caching/ExpansionCache.cs is stored in a non-UTF-8 encoding, so plain `grep'
treats it as a binary file and reports "binary file matches" instead of the
matching lines. Use `grep -a' when searching the source tree, or that file
silently drops out of every sweep.

BUILDING
========
    dotnet build CodeBrix.LilyScheme.slnx

The library targets net10.0 only -- no multi-targeting, ever.
GenerateDocumentationFile is on, so every public type and public/protected member
needs an XML doc comment; CS1591 is fixed at the source, never suppressed with
<NoWarn>. There is no project-level warning suppression anywhere in the repo and
none may be added.

GeneratePackageOnBuild is TRUE for the library project, so an ordinary build
produces a .nupkg with a fresh version (see PACKAGING AND PUBLISHING).

TESTING
=======
    dotnet test CodeBrix.LilyScheme.slnx

The suite is long-running by design. Consumers are told not to run it; a
maintainer runs it before any change ships.

Tests that evaluate Scheme MUST run inside Interpreter.RunWithLargeStack --
psyntax will overflow the default stack otherwise. A failure on the big-stack
thread reaches the caller AS ITSELF, with its original stack trace, so assertions
read error text straight off the caught exception. Do not reintroduce a wrapper
exception in RunWithLargeStack: one used to live there and it hid every real
message behind a generic one, which made a whole class of failure look like a
single unknown cause.

Test conventions: xUnit v3 plus SilverAssertions fluent assertions
(x.Should().Be(y)), coverlet.collector for coverage, one <Class>Tests.cs per
fenced area, snake_case test method names, //Arrange //Act //Assert comments, and
TestContext.Current.CancellationToken threaded through any call that accepts a
CancellationToken.

WHAT EACH TEST CLASS FENCES
---------------------------
    SchemeReaderTests       reader coverage: numbers, the numeric tower, strings,
                            characters, keywords, vectors, #{...}#, #nil,
                            comments, the quote family as mid-token symbol
                            constituents (Hello' is ONE symbol) with datum-start
                            quoting as the control, and the Printer.WriteString
                            round trip that a host path depends on -- Windows and
                            POSIX shapes both, including the paths whose next
                            character spells a VALID escape and would otherwise
                            read clean as a DIFFERENT path, with the raw \U splice
                            fenced as the control
    InterpreterTests        the core evaluator without psyntax -- arithmetic,
                            closures, tail calls, letrec, lambda*, hash tables,
                            catch/throw, and the throw-handler contract:
                            with-throw-handler runs its handler BEFORE the stack
                            unwinds (real pre-unwind semantics via .NET exception
                            filters) and the throw then keeps propagating; catch
                            honours its optional pre-unwind handler the same way
    PsyntaxBootstrapTests   the milestone gate: psyntax loads, macroexpand returns
                            Tree-IL, syntax-rules runs, macro expansion is
                            HYGIENIC, and the vendored srfi-1 and ice-9 match both
                            load and work
    GuileCompatibilityTests the surface beyond psyntax: module autoloading,
                            quasisyntax (including a tail unsyntax), the
                            non-finite reals, SRFI-13 strings, SRFI-14 character
                            sets, SRFI-9 records, generalized setters, stable
                            sorting, GOOPS over built-in classes, string ports,
                            reader hash extensions, once-evaluated case keys,
                            format directives, format errors propagating as
                            catchable throws, list-copy preserving improper tails,
                            Guile arrays (literals, make-shared-array views,
                            transpose), the bitwise family (including logcount),
                            procedure-arguments via the synthesized 'arglist
                            property, the (system vm program) / (ice-9 iconv) /
                            (ice-9 soft-ports) shims, escape-only prompts (aborts
                            pass through catch), put-string, SRFI-13 string= and
                            string-concatenate-reverse, module-map, (ice-9 list)'s
                            rassoc family, and pretty-print writing and wrapping
                            through the soft-port machinery
    AnonymousModuleMacroTests  the lazy naming-and-registration of anonymous
                            modules that lets psyntax resolve imported macros
                            inside them, plus defined?
    DocumentationSupportTests  the surface a documentation generator stands on:
                            GOOPS evaluating a slot's #:init-value, char-ci<?
                            folding UPWARD as libguile/chars.c does,
                            procedure-documentation answering a docstring rather
                            than a name, a curried definition carrying its
                            docstring on the OUTERMOST lambda, and load-from-path
                            going through whatever primitive-load-path is bound at
                            call time
    SourceLocationTests     source locations from the reader through psyntax into
                            a procedure's printed representation -- the line shown
                            one-based and the column as it stands, a named
                            procedure showing neither address nor location, a
                            define NOT naming a value it merely computed, and the
                            program-print re-entry latch sticking after a
                            non-local exit while a normal return leaves it clear
    SoftPortBufferingTests  the soft port's 1024-byte buffer and 252-byte transfer
                            quantum, every expectation a flush sequence measured
                            against the pinned oracle
    PrimitiveGenericTests   extending a generic-capable primitive, and that the
                            extension is visible from ANOTHER module -- the case
                            the whole mechanism exists for; that ordinary
                            arithmetic still falls through to the subr;
                            generic-capability? matching Guile's own declarations;
                            the setter a #:accessor carries; the renaming half of
                            a use-modules #:select; and the Guile-core names those
                            three exposed as missing -- the
                            floor/ceiling/truncate/euclidean division family,
                            finite?, string-capitalize and substring/shared
    PortProcedureTests      the file-reading layer -- #:encoding decoding the same
                            byte two different ways, the two end-of-file
                            conventions, a port closed even when the procedure
                            throws -- plus the mode-string opener: open-file
                            round-tripping, "a" keeping what "w" truncates, "rb"
                            reading one character per byte, "r+" refused rather
                            than half-served, close-port flushing an output FILE
                            port, and file-port? telling a file from a string port
                            and from (current-error-port)
    BinaryPortTests         set-port-encoding! actually switching a live port's
                            codec (latin1 octets leave one byte each), the
                            opendir / readdir / closedir / rmdir / delete-file
                            family, and read-char / peek-char
    ExpansionCacheTests     the cache's four rules: c&e recording without
                            re-evaluation, per-interpreter deserialization,
                            identity-preserving round-trips, and corruption always
                            reading as a MISS, never as a failed boot
    SelectImportTests       use-modules #:select from both sides -- the selected or
                            renamed name arrives, the unselected one does not, and
                            a clause without #:select still imports the whole
                            module
    AlistRemoveTests        assq-remove! / assv-remove! / assoc-remove! unlinking
                            exactly ONE entry -- the FIRST match -- as
                            libguile/alist.c documents
    DestructiveAppendTests  append! re-linking its arguments rather than copying
                            them (the identity IS the contract), both appends
                            rejecting an improper or non-list argument before the
                            last with the oracle-measured wrong-type-arg shapes,
                            plus eval-string
    HostEqualityTests       equal? dispatching to a host object's own equality
                            handler, the way scm_equal_p ends at a smob's equal_p
    NaryEqualityTests       eq? / eqv? / equal? as N-ARY predicates, and the
                            optional equality predicate of member and assoc
    ListMutationTests       list-set! and list-cdr-set!, to libguile/list.c's
                            behaviour (both answer the VALUE), plus the index
                            walk's oracle-measured failures: off a proper list is
                            out-of-range naming argument 2, an improper tail is
                            wrong-type-arg naming argument 1, and a negative index
                            dies in the size_t conversion with subr #f
    ComplexNumberTests      the complex literals, arithmetic and accessors,
                            including the exact-zero collapse
    UnicodeNameTests        char->formal-name / formal-name->char against the
                            Unicode Character Database's own contents, including
                            answering #f for algorithmically named CJK and Hangul
                            rather than deriving
    RdelimTests             (ice-9 rdelim) end to end: all four handle-delim modes
                            of read-line, read-delimited gobbling or splitting its
                            delimiter, read-string reading the REMAINDER,
                            unread-char stacking most recent first, and write-line
                            DISPLAYING (the fence that caught it writing quotes)
    PopenTests              (ice-9 popen) against real child processes: an input
                            pipe streaming lines in order, an output pipe reaching
                            the child's stdin (proved by the bytes on disk),
                            close-pipe's encoded wait status, and the loud
                            OPEN_BOTH refusal. POSIX-shell facts skip on Windows
    PosixTests              system's wait-status encoding (7 travels as 1792),
                            system* bypassing the shell, stat's size and type with
                            the #f-for-missing arm, and the broken-down-time
                            conventions -- epoch 0 as Thursday 1970-01-01 with
                            every struct tm off-by-one asserted, strftime's
                            directives, and the %s round trip through tm:gmtoff
    RegexPosixTests         the POSIX regular-expression contract, each
                            divergence-prone point fenced from both sides: ASCII
                            [[:digit:]] against a Unicode digit, the literal
                            backslash in brackets, ^ at a start offset vs
                            regexp/notbol, unmatched groups answering #f, and the
                            loud regexp/basic and regexp/noteol refusals
    GetoptLongTests         (ice-9 getopt-long) vendored verbatim -- an end-to-end
                            fence for common-list's #:select rename, (ice-9 match),
                            (ice-9 regex) and SRFI-9 records all at once
    ModernExceptionTests    the modern exception API from BOTH sides of the old/new
                            interop: catch seeing a raised exception's kind and
                            args, a handler seeing a C# primitive's throw as a
                            converted &assertion-failure, the pre-unwind ordering
                            of a non-unwinding handler and the &non-continuable a
                            returning one provokes, raise-continuable's value
                            flowing back through a guard's re-raise chain,
                            #:unwind-for-type in both symbol and type form, the
                            compound object model, print-exception, and the loud
                            wrong-type-arg refusals
    WrongTypeArgumentTests  the wrong-typed-argument contract from both layers: a
                            Scheme catch on 'wrong-type-arg sees a primitive's
                            type failure, the positioned subr/position message, the
                            Primitive.Invoke net translating a bare cast (including
                            one in a HOST-registered primitive), the net's
                            selectivity (a primitive's own SchemeThrow passes
                            untouched), and the well-typed controls
    RecordInheritanceTests  Guile's single-inheritance record model: parent fields
                            laid out first, subtype-accepting predicates, the
                            "parent type is final" refusal, (immutable name) specs
                            refusing a modifier, the struct view of a record, and
                            the default-record-printer rendering
    NarrowImportTests       the Interpreter.NarrowModuleImports OPT-IN switch from
                            both positions: exported names arrive and private ones
                            do not, the interface view is LIVE (later exports
                            arrive) and shares variable cells (set! works through
                            it), #:select is unaffected, and the vendored srfi-1
                            and (ice-9 exceptions) keep working narrow --
                            including macro bindings through the view
    Srfi43Tests             (srfi srfi-43): the INDEX-FIRST calling convention its
                            iteration procedures have (which R7RS's vector-map does
                            not), and the ranged vector-copy its importers reach
                            through the range-capable core binding
    Srfi13RangeTests        the optional [start end] ranges of the SRFI-13 string
                            family: every fence a case whose ranged answer DIFFERS
                            from the unranged one, so a range that is accepted and
                            ignored cannot pass -- font-table.ly's middle-dot split
                            end to end, string-reverse / string-titlecase
                            transforming the region INSIDE a whole-string copy,
                            string-delete / string-filter / string-trim /
                            string-tokenize answering from the window alone,
                            string-any / string-every answering the predicate's own
                            last value, char-set and predicate criteria through the
                            ranged twins, out-of-range raised catchably for a bad
                            bound on every member, and a wrong-typed criterion
                            raising the positioned wrong-type-arg even over an
                            empty window
    LilyPondSchemeManualTests  the pure-Scheme REPL transcripts of the "LilyPond's
                            Scheme" manual (Urs Liska and others, CC BY-SA 4.0,
                            github.com/jeanas/lilyponds-scheme), replayed as
                            define-then-use sessions -- the user's-eye contract:
                            reader dot edge cases ('(apple .2), '(red. 4)),
                            accessor shorthand composition, append attaching its
                            LAST argument as it stands, acons/assq first-match
                            answers, quasiquote splicing, map stopping at the
                            shortest list, srfi-1 accessors via use-modules, printed
                            procedure signatures, and the manual's deliberate
                            failures raising the same throw KEYS. LilyPond-layer
                            names (red, color?, fraction?, ly:*) are excluded on
                            purpose -- those are the consumer's
    SmokeTests              the library assembly loads at all, and every vendored
                            .scm resource arrives WITHOUT carriage returns -- the
                            sweep that stops a CRLF checkout being packed into a
                            release

KEEP THE Srfi13RangeTests PROPERTY WHEN TOUCHING THAT FAMILY: a fence whose ranged
and unranged answers AGREE passes an accepts-and-ignores implementation, which is
exactly the defect the class exists to catch.

PACKAGING AND PUBLISHING
========================
Packing is driven by the library csproj itself -- GeneratePackageOnBuild is true,
so `dotnet build` in Release produces the .nupkg. There is no separate pack
script.

VERSIONING is date-stamped and auto-incrementing, computed in the csproj from
System.DateTime.UtcNow: 1.<years since _VersionBaseYear>.<day of year>.<minute of
day UTC>. Every field is derived from the clock, so:

  * the version strictly increases over time;
  * EVERY BUILD PRODUCES A NEW VERSION, and with GeneratePackageOnBuild that
    means a fresh .nupkg on every build;
  * two builds within the SAME UTC minute produce the SAME version -- do not
    publish two packages from within one minute;
  * this is not SemVer. Minor encodes the year and major is pinned to 1, so
    major/minor do not signal API compatibility.

To re-baseline the minor number, change _VersionBaseYear in the csproj.

WHAT SHIPS INSIDE THE .nupkg (all from the repo root, via <None Pack="true">):
    icon-codebrix-128.png      the package icon
    README.md                  the PackageReadmeFile
    AGENT-README.txt           the consumer documentation
    THIRD-PARTY-NOTICES.txt    the attribution ledger
    LICENSE                    the full LGPL-3 text
    LICENSE.GPL                the full GPL-3 text

BOTH license texts must travel: LGPL-3.0 incorporates the terms of GPL-3.0 by
reference, so a LICENSE carrying only the LGPL-3 text is incomplete.
PackageLicenseExpression supplies the SPDX label for nuget.org; these two entries
put the full texts inside the package so a consumer who never visits the repo can
still read the terms and the warranty disclaimer.

EMBEDDED RESOURCES: every Scheme\**\*.scm and every Unicode\*.deflate (the latter
with an explicit LogicalName of CodeBrix.LilyScheme.Unicode.<file>).

PROVENANCE AND VENDORED SOURCES
===============================
Guile revision audited: v3.0.11-172-g472589569. Twenty-nine .scm files from the
GNU Guile source tree are vendored under src/CodeBrix.LilyScheme/Scheme/, every
one byte-identical to its upstream original (verified with cmp).
THIRD-PARTY-NOTICES.txt is the complete, living ledger: per-file attribution,
line counts, upstream mapping, seven additional copyright holders beyond the FSF,
one public-domain file, and one file (ice-9/quasisyntax.scm, Andre van Tonder)
under its own MIT-style grant rather than the LGPL.

THE LEDGER IS UPDATED IN THE SAME COMMIT as any change that incorporates, adapts,
modifies or removes third-party source. The standing check is that every .scm
under Scheme/ appears in section 1.2's inventory, with its line count, per-file
copyright years and upstream mapping -- all four places, since a file can be
listed in one and missed in the others. Scheme/lilyscheme/prelude.scm is the
port's own and is deliberately not inventoried.

NEVER EDIT A FILE UNDER Scheme/ THAT CAME FROM GUILE. They are verbatim by
design, which is what makes re-syncing a straight copy plus cmp.
Scheme/lilyscheme/prelude.scm is ours and may be edited.

AND THEY MUST BE LF
-------------------
Every file under Scheme/ is an <EmbeddedResource>, so the bytes on disk at BUILD
time are baked into the assembly and shipped in the package -- a CRLF working
tree is not a local inconvenience, it is a broken artifact for every consumer on
every platform. A CR is whitespace between forms and is NOT whitespace inside a
string literal, and the multi-line format-directive literals of ice-9/format.scm
are full of them: format's parser then runs off the end of its string and recurses
through format-error until the process dies with an uncatchable stack overflow.
Nothing warns. (It is the same observable failure as the once-per-clause `case'
bug below, from an unrelated cause -- so read a silent stack overflow in
Scheme-heavy code as EITHER.)

Two layers hold it, and both are load-bearing:

* .gitattributes at the repo root pins *.scm to eol=lf. The committed blobs are
  already LF, so this only stops a Windows checkout (core.autocrlf=true)
  expanding them on the way out -- which also keeps the vendored files
  byte-identical to Guile's for the cmp re-sync.
* SchemeBootstrap reduces CRLF to LF as it reads each resource. The .gitattributes
  governs a CHECKOUT and nothing else; this is what makes the ARTIFACT correct
  when the bytes arrive some other way -- a source zip, a contributor configured
  differently, an editor that saves CRLF. Only the PAIR is rewritten; a lone CR
  is left alone, because silently rewriting a deliberate carriage return inside a
  string literal would be the same class of quiet corruption. SmokeTests sweeps
  every resource.

The LGPL compliance obligations documented in
~/GitHome/CodeBrix.Library.Dev-private/info/LGPL_GUIDANCE.txt apply.

C# PROVENANCE
-------------
Where a C# file is a translation of specific libguile source it carries, on its
namespace line:

    namespace CodeBrix.LilyScheme.<Area>; //was previously: libguile/<file>.c;
    // Modified by Jeremy Ellis on <YYYY-MM-DD> as part of the CodeBrix port.

and preserves the upstream copyright header verbatim above the usings. The
modification notice is required by LGPL-3 (via GPL-3 section 5(a)) and is NOT
satisfied by the provenance comment alone. Files written from published
specifications -- R7RS, the SRFI documents, the Tree-IL node definitions -- are
new-in-family, carry no //was previously: comment and NO fabricated upstream
header, and are not listed in the ledger. Where a file's status is ambiguous, it
is treated as a translation.

CODING CONVENTIONS
==================
* Target framework is always net10.0 -- no multi-targeting.
* <Nullable> is never enabled family-wide. Do not write `?' on reference types
  (string?, MyClass?) and never use the null-forgiveness `!' operator.
  Value-type nullables (int?, bool?, MyEnum?) are fine -- those are Nullable<T>.
* No `global using' directives anywhere.
* File-scoped namespaces only (namespace X;), never block-scoped.
* using directives are fully qualified, at the top of the file, System.* first
  then alphabetical, with no blank lines inside the block, and never below the
  namespace line.
* Every public type and public/protected member needs an XML doc comment.
  CS1591 is fixed at the source, never suppressed.
* No project-level warning suppression (<NoWarn>, <WarningLevel>0</WarningLevel>,
  <TreatWarningsAsErrors>false</...>) on any csproj in this repo.
* Tests: xUnit v3, SilverAssertions, coverlet.collector, and
  TestContext.Current.CancellationToken threaded through anything that takes a
  CancellationToken.

THE PORT'S OWN LORE
===================
This section is the Guile-fidelity and porting record: why the implementation is
shaped the way it is, which experiments were tried and reverted, and which
behaviours were MEASURED against a pinned oracle rather than reasoned about. It
is maintainer material. Consumer-visible consequences of all of it are in
AGENT-README.txt.

HOW THE BOOTSTRAP WORKS, AND THE TWO THINGS THAT BREAK IT SILENTLY
------------------------------------------------------------------
Guile ships its macro expander twice: as psyntax.scm, written in syntax-case, and
as psyntax-pp.scm, the same expander already macro-expanded into core Scheme so
it can be loaded by an implementation that does not yet have a macro expander.
That is how Guile bootstraps itself, and it is how this library gets full
syntax-case without anyone hand-writing an expander:

    source text
       -> reader                (Reader/SchemeReader.cs)
       -> core evaluator        (Runtime/Evaluator.cs)   [bootstrap only]
       -> psyntax               (vendored psyntax-pp.scm)
       -> Tree-IL structs
       -> Tree-IL evaluator     (TreeIl/TreeIlEvaluator.cs)

Guile's macroexpand does NOT return s-expressions. It returns structs built from
the %expanded-vtables vector -- Tree-IL. There are exactly eighteen node types,
mirrored from libguile/expand.h in Values/SchemeStruct.cs. GETTING THEIR FIELD
ORDER WRONG BREAKS EVERYTHING SILENTLY, because psyntax constructs them
positionally.

* syntax-empty-wrap is (() . ()), a cons of two empty lists -- a psyntax wrap is
  (marks . substs), and libguile/syntax.c builds it as
  scm_cons (SCM_EOL, SCM_EOL). Get this wrong and every identifier resolves as a
  top-level reference instead of a lexical one, with no error: lambda parameters
  simply come out unbound.
* primitive-eval receives ALREADY-EXPANDED Tree-IL. psyntax's top-level-eval and
  local-eval hooks are both (lambda (x mod) (primitive-eval x)). If primitive-eval
  only handles source forms, every macro definition silently stores an unevaluated
  struct and the macro is never found.

ABOUT boot-9.scm, AND WHY THE PRELUDE EXISTS
--------------------------------------------
boot-9.scm is vendored but NOT loaded. It builds Guile's module system, record
types, port types and exception hierarchy from scratch on low-level vtable
layouts, and it opens by asserting (current-module) is #f because it runs before
any module system exists. This library supplies the module system from C#, so
boot-9 cannot load verbatim. Scheme/lilyscheme/prelude.scm provides the derived
syntax instead -- and, or, cond, case, when, unless, do, let-values,
define-values, receive, and-let*, while, parameterize, cond-expand, defmacro, the
define-module / use-modules / define-public family, and (ice-9 optargs)'s
let-keywords / let-keywords*.

The last pair are in the prelude rather than loaded from the vendored optargs.scm
because that file's expansion calls parse-lambda-case, a Guile VM primitive with
no analogue here. lambda* already carries the whole keyword protocol in C#, so
both macros expand to a lambda* applied to the rest list.

psyntax's core forms are only quote, if, lambda, let, letrec, begin, set! and
define. Even `and' and `or' are boot-9 macros, which is why the prelude has to
define them.

boot-9 also defines a good many ordinary PROCEDURES -- identity, const, and=>,
->bool and their neighbours -- and because the file never loads, none of them
exists unless something puts it there. Those live in Primitives/CorePrimitives.cs.
Treat "it is in Scheme/ice-9/boot-9.scm" as saying nothing at all about whether a
name is bound: that file is reference material, and a name found only there is a
name that is UNBOUND at runtime. Check with (defined? 'name) rather than by
grepping the vendored source -- a diagnosis of "present but not visible from this
module" is almost always really "never loaded".

The same applies to Guile's port procedures, which live in ice-9/ports.scm and
ice-9/textual-ports.scm rather than in boot-9 and are likewise not loaded.
open-input-file, call-with-input-file, call-with-port, get-string-all and
get-string-n are implemented in Primitives/PortPrimitives.cs, core-side rather
than in a module, which is the standing posture here: this library's scope is
deliberately WIDER than Guile's per-module scope and never narrower.

quasisyntax is NOT part of psyntax. Guile pulls it into the core environment from
boot-9.scm line 424; the prelude does the same include, because #` templates are
what the consumer's music-function layer is built on.

MODULE AUTOLOADING, AND THE SHIMS
---------------------------------
(use-modules (srfi srfi-1)) loads the vendored srfi-1.scm the first time the
module is named, exactly as Guile autoloads. Without it a freshly resolved module
is simply EMPTY and every name it would supply comes out unbound -- which reads as
dozens of unrelated failures rather than one missing mechanism.

The vendored srfi-1.scm expects some of its own names FROM THE CORE and marks them
so -- a line reading ";; filter!  <= in the core" sits where the definition would
be, and the name is then re-exported. A core that does not define such a name
leaves it unbound for everything importing (srfi srfi-1). Grep the vendored file
for "in the core" before concluding that an srfi-1 name is missing on purpose.

SchemeBootstrap.SelfProvidedModules lists the modules that must NOT be autoloaded:
(oop goops) is superseded by the C# GOOPS, (ice-9 optargs) and
(ice-9 and-let-star) by the prelude, (ice-9 boot-9) cannot load at all, and five
SHIM modules are provided from C# at bootstrap:

  * (system vm program), whose program? answers #f for everything because there
    is no VM. The vendored ice-9/session.scm needs it.
  * (ice-9 iconv), whose string->bytevector / bytevector->string run over .NET's
    encodings. The consumer's qr-code layer imports from it.
  * (ice-9 soft-ports), whose keyword-form make-soft-port builds output ports over
    a Scheme write-string procedure (input soft ports are refused loudly until
    something demands them). The vendored pretty-print.scm builds its truncating
    writer on it, together with the escape-only call-with-prompt /
    abort-to-prompt / make-prompt-tag protocol in ControlPrimitives -- aborting
    OUT of the thunk works in full, re-entering the continuation fails loudly.
  * (ice-9 popen), whose pipes run over System.Diagnostics.Process.
  * (ice-9 unicode), whose char->formal-name / formal-name->char read a shipped
    table because Guile implements them over GNU libunistring and .NET has no
    character names at all.

/!\ (ice-9 unicode) answers #f for a CJK ideograph or a Hangul syllable rather
than DERIVING its algorithmic name -- that is Guile's behaviour, measured against
316 occurrences of a "no glyph for character" warning across 79 distinct
characters in a reference corpus, all 316 agreeing including the one negative.
Python's unicodedata does the opposite; do not "fix" it.

THE IMPORT-SIDE DIVERGENCE, AND THE EXPERIMENT THAT WAS REVERTED
----------------------------------------------------------------
By DEFAULT a use-modules WITHOUT #:select puts the WHOLE module on the use list
rather than its public interface, so visible scope is WIDER than Guile's, never
narrower -- the behaviour the consuming LilyPond layer's module world was verified
under. Interpreter.NarrowModuleImports = true closes it. NarrowImportTests fences
both positions of the switch. FLIPPING THE DEFAULT still wants a session that can
sweep the consuming layer behind it.

WHEN TWO IMPORTS BIND ONE NAME, THE FIRST IMPORT WINS -- A MEASURED DIVERGENCE,
KEPT. Guile's duplicate-binding handlers (default chain
(replace warn-override-core warn last)) resolve toward the LAST module used and
honor #:replace; a newest-first search reproducing that was BUILT AND REVERTED the
same day, because it broke macro resolution across the consuming layer's module
world (make-engraver read as an unbound variable, seven engraving tests red) --
that world's scope chains were verified under first-wins. The practical cost is
confined to names a module #:replace's over core: (srfi srfi-43)'s vector-copy /
vector->list / list->vector resolve to the CORE bindings for importers, and the
core vector-copy now takes [start [end]] (libguile/vectors.c's own signature), so
the common arities agree; only srfi-43's fourth (fill) argument is out of reach.
DO NOT REINTRODUCE NEWEST-FIRST without budgeting a full corpus sweep and suite
run for it. A module's OWN binding beats every import in both readings.

The EXPORT side is Guile's and is unchanged by that divergence. The public
interface is built fresh on every ask rather than cached, because a module goes on
growing: the consuming layer loads more than fifty files INTO (lily) after the
module is created, and an interface captured on first ask would answer for
whatever had been exported at that moment. Returning the whole module here instead
is not a small thing: LilyPond generates its Internals Reference by walking
(module-public-interface (resolve-module '(lily))) and documenting every procedure
in it that has a docstring, so the wide answer documented eighty-two private
helpers upstream does not.

A define-module clause keyword may be spelled #:export or as the keyword-like
SYMBOL :export -- boot-9 normalizes the latter with keyword-like-symbol->keyword,
and define-module* here does the same. The vendored srfi-1.scm uses exactly that
spelling; before the normalization its whole export list went unrecorded, which
the wide import silently hid.

MODULE MECHANICS THAT COST REAL DEBUGGING TIME
----------------------------------------------
* module-add! MUST install the VARIABLE, not the value. Code relies on the
  sharing: define-session-public hands every parser scope the very variable that
  lives in (lily), commenting that this is so "both set! and define will affect the
  original variable". Implementing module-add! as an alias for module-define!
  compiles, loads and passes every test that only READS the name -- and then hands
  readers the VARIABLE OBJECT as if it were the value.
* module-name NAMES an anonymous module on first ask -- a fresh generated name,
  under which the module is simultaneously REGISTERED (SchemeModule.EnsureName;
  boot-9 does the same with a gensym). psyntax round-trips module identity BY NAME
  inside hygiene wraps, so an imported macro used in a module that cannot be named
  back does not resolve as a macro at all. The consuming layer's anonymous parser
  scopes hit exactly this: every define-music-function in the init layer failed on
  an unbound variable named after its first parameter, and the consumer worked
  around it by naming its scopes until the lazy naming landed here. defined?
  exists for the same layer. AnonymousModuleMacroTests fences the mechanism.
* A HOST THAT AUTOLOADS MODULES MUST SAVE THE CURRENT MODULE.
  ModuleRegistry.ModuleLoader is called from Resolve; SchemeBootstrap's own
  vendored-module loader wraps that load in a save/restore of
  Interpreter.CurrentModule, and a host installing its own loader must do the same
  -- Guile's autoloader is a save-module-excursion for this reason. The consumer
  hit exactly this: 668 bindings that belonged in (lily) were defined in
  (lily curried-definitions) instead, and it went unnoticed for a long time because
  (lily) uses that module, so ordinary lookups still found everything. What it
  broke was SHADOWING -- GOOPS methods specialising +, -, * and < on host types
  were found only after the root module's arithmetic had already answered.
* THE eval EXCURSION BELONGS IN THE eval PRIMITIVE AND NOWHERE ELSE. Putting it
  inside TreeIlEvaluator.ExpandAndEval looks equivalent and is not: that is also
  the per-form loader path, and a (define-module ...) at the head of a file takes
  effect BY changing the current module, so restoring it afterwards silently undoes
  the declaration and every later form in the file lands in the caller's module.

EXTENDING A PRIMITIVE IS GLOBAL; EXTENDING A NAME IS NOT
--------------------------------------------------------
define-method does two different things depending on what the name already holds.
On a fresh name it defines a generic in the current module. On a GENERIC-CAPABLE
PRIMITIVE it defines nothing: goops.scm's
(define-method (add-method! (proc <procedure>) (m <method>)) ...) calls
enable-primitive-generic! on the subr, which hangs the generic off the PRIMITIVE
ITSELF, and the method goes there. Because every module that imports the core sees
that one object, the extension is global.

Getting this wrong is invisible from the defining module. Defining a fresh generic
there instead passes every test that subtracts pitches in that same module, and
every OTHER module still resolves the raw numeric '-' and throws wrong-type-arg --
which is how 28 files' worth of accidental and \transpose failures looked like
unrelated arithmetic bugs. Primitives/PrimitiveGenerics.cs holds the mechanism and
the roster of capable names, which was READ OUT of libguile rather than recalled:
an undeclared name simply never dispatches, with no diagnostic. DO NOT REACH FOR A
MODULE-ORDERING FIX -- reordering the consumer's module construction diverges from
ly_make_module shadowing for every binding, and does not make the extension global
anyway.

The apply path selects a method first and invokes the primitive when none applies,
so ordinary arithmetic is untouched.

Note that make-procedure-with-setter and procedure-with-setter? are installed by
GuileCorePrimitives.InstallSetters. Placeholders answering "no setter" used to sit
in ControlPrimitives as well, and worked only because ControlPrimitives installs
FIRST -- swapping the two Install calls would have made every accessor silently
discard its setter. They are gone.

MEASURED AGAINST THE PINNED ORACLE
----------------------------------
The following were MEASURED against a pinned GNU Guile / GNU LilyPond oracle
rather than reasoned about from documents. Changing any of them needs a new
measurement, not an argument.

* THE SOFT-PORT BUFFER MODEL. A soft output port buffers 1024 bytes. A write that
  fits is appended, and a full buffer flushes; a write that does not fit tops the
  buffer up by whole 252-byte QUANTA and flushes, so an empty buffer transfers
  1008 = 4 x 252 at a time and leaves 16 bytes unused. Both constants were measured
  and then confirmed by prediction on fills the model had not been shown. This is
  not an internal detail: pretty-print's truncating writer aborts from INSIDE
  write-string, so when the buffer flushes decides where that abort lands -- and an
  abort landing inside the procedure printer latches print_error for the rest of
  the process. Writing straight through made procedures start printing in the
  low-level form earlier than Guile does, and differed on twenty-four entries of a
  generated manual. SoftPortBufferingTests fences the model.
* THE PROGRAM-PRINT RE-ENTRY LATCH. libguile/programs.c's scm_i_program_print sets
  a file-static print_error flag, calls out to the Scheme printer, and clears the
  flag afterwards; while it is set it prints the low-level "#<program ADDR CODE>"
  instead. It never recovers, because an abort that lands inside the printer leaves
  the flag SET for the rest of the process. LilyPond knows: scm->string carries a
  regex whose only job is to normalise it, and the generated manual shows that form
  206 times against 29 ordinary ones. Printer holds the latch across the EMIT
  rather than the render, because the render builds a string and cannot abort.
  Standing rule: upstream's own defects are reproduced, not corrected.
* THE APPEND FAMILY'S FAILURE SHAPES. append raises wrong-type-arg naming the
  argument's position, the words "empty list", and the offending TAIL; append!
  distinguishes a non-pair argument ("pair") from an improper tail ("empty list").
  Both once walked whatever they were given -- (append '(1 2 3 4 . 5) 6) answered
  (1 2 3 4 . 6), silently dropping the tail, and append! skipped a non-list
  argument entirely.
* THE INDEX-WALKING FAMILY'S FAILURES. list-ref / list-set! / list-cdr-set! raise
  out-of-range naming argument 2 when they run off a proper list, wrong-type-arg
  naming argument 1 on an improper tail, and a NEGATIVE index dies inside the
  size_t conversion -- subr #f -- before the procedure's name enters the story.
  A catch on 'out-of-range stands on the distinction, and list-ref used to answer
  it with a wrong-type-arg instead.
* char-ci<? AND ITS FAMILY FOLD UPWARD. libguile/chars.c compares scm_c_upcase (x)
  against scm_c_upcase (y) in all five comparisons.
* THE READER'S TREATMENT OF ' ` , AS SYMBOL CONSTITUENTS mid-token, with quote
  syntax at datum start only.
* THE SRFI-13 RANGE SHAPES, all read out of libguile/srfi-13.c rather than out of
  the SRFI document. Most of the family once DECLARED the range and ignored it,
  which is the worst arity to ship: the call succeeds and the unranged answer comes
  back. font-table.ly splits a glyph name at the dot nearest its middle with one
  ranged string-rindex and one ranged string-index, so the ignored range moved the
  split with no diagnostic while every count stayed green.
* THE WAIT-STATUS ENCODING, recorded on PosixPrimitives.EncodeWaitStatus: .NET
  reports a signal-killed child as exit code 128+signal (the shell convention)
  rather than through a separate WIFSIGNALED channel.
* THE UNICODE NAME TABLE'S EXCLUSIONS (see the autoload section above).

OTHER RECORDED DECISIONS
------------------------
* WHY sort IS A MERGE SORT. List<T>.Sort is an introsort that VALIDATES its
  comparer and throws "IComparer.Compare() returns inconsistent results" when the
  Scheme predicate is not a strict weak ordering. The consuming layer passes
  predicates that are not. A merge sort asks only "does b come before a", so it
  copes -- and it is stable, which is what stable-sort promises. Do not swap it
  back.
* WHY case BINDS ITS KEY ONCE. The prelude's case macro rebinds a compound key
  expression in a let before dispatching (R7RS 7.3's own pattern). The clause
  recursion splices the key into every memv test, so without that rebind a
  side-effecting key evaluates once PER CLAUSE -- and ice-9/format.scm dispatches on
  (case (char-upcase (next-char)) ...), which then silently re-read the format
  string, ran off its end, and recursed through format-error until the process died
  with an uncatchable stack overflow. Do not "simplify" the extra rule away, and
  treat any silent SIGABRT in Scheme-heavy code as a possible once-per-clause
  evaluation somewhere until proven otherwise.
* WHY A REWRITE MUST NOT REBUILD PAIRS UNCONDITIONALLY. Source properties are keyed
  by object identity. CurriedDefinitions runs over every form of every file before
  psyntax sees it, and rebuilding unconditionally erased the entire layer's
  locations; it now returns the ORIGINAL object when nothing changed, and copies
  properties across when a rebuild is genuinely needed.
* WHY THE ARRAY SURFACE STOPS WHERE IT DOES. libguile/arrays.c's scm_is_array also
  counts strings, bitvectors and bytevectors. Those are not accepted here, because
  nothing has asked for them yet; a caller that does will get the same "Not an
  array" a missing name would give, which is a visible failure rather than a wrong
  answer.
* WHY EVERY PORT TRACKS ITS POSITION, AND WHAT THAT MOVED (2026-08-30).
  port-column answered for a soft port and a string port and returned a flat 0 for
  every other, which broke (ice-9 pretty-print) outright -- see AGENT-README
  pitfall 53 for the mechanism and the measured update rules. PortPosition is now
  the ONE place a line and column advance, shared by the reader, the tracking
  output writer and the soft port, because Guile updates both directions with one
  function; that is why a tab counts the same read or written.
  THE INPUT HALF IS NOT COSMETIC AND CONSUMERS MUST BE TOLD. A datum's
  source-properties ARE the port's line and column at its first character
  (measured: "\n\n   (hello world)" records line 2 column 3), so two things
  changed that a consumer can see. First, the reader counts a TAB to the tab stop,
  so "\t(x)" records column 8 where it used to record 1 -- every source location
  on a tab-indented line moves. Second, set-port-line! / set-port-column! on an
  input port are real, so LilyPond's parser-ly-from-scheme.scm synchronisation
  actually works and #{ ... #} embedded Scheme now carries the location of its real
  source instead of the copy's. CodeBrix.LilyPort therefore owes its FULL battery
  on this pin bump, not the calibrated pin-bump bar.
* THE READER'S ERROR SURFACE, AND THE ONE PLACE IT DELIBERATELY DIFFERS
  (2026-08-30). A syntax error is now a read-error condition -- see AGENT-README
  pitfall 54 for the shape, the position convention and the strictness changes.
  SchemeThrow is NO LONGER SEALED for this: SchemeReaderException derives from it,
  which is what makes one object both a Scheme condition and the C# type hosts
  have always caught. Every catch clause in the library is written
  catch (SchemeThrow ...), so a subclass is handled like any other condition.
  THE DIVERGENCE, kept on purpose and measured: an unterminated #{...}# symbol.
  The oracle does not raise a read error there at all -- it reaches end of input
  and calls char=? on the eof object, so the user sees
    (wrong-type-arg "char=?" "Wrong type argument in position ~A (expecting ~A): ~S"
                    (1 "character" #<eof>) (#<eof>))
  which is an internal accident, not a designed message. This implementation
  raises a read-error saying "unterminated #{...}# symbol". It is the ONE case in
  the whole survey where the two disagree, and reproducing upstream would mean
  reproducing a crash-shaped error that names a procedure the user never called.
  Revisit only if something turns out to depend on the wrong-type-arg shape.
* WHAT THE ERROR SURVEY FOUND, AND WHAT WAS DONE WITH IT (2026-08-30). The survey
  turned up three things beyond the error surface, and all three were taken -- see
  AGENT-README pitfall 55. Bytevectors printed as "System.Byte[]"; symbols printed
  their bare name; and the array literal reader refused rank zero, printed a rank-1
  array with a digit upstream omits, and reported a ragged literal with the wrong
  condition KEY. The symbol rules came from a table of 33 punctuation characters
  probed twice each (a<c>b and <c> alone) because the forcing set and the escaping
  set are DIFFERENT and neither is guessable: ( forces the extended syntax and is
  hex-escaped inside it, ; forces it and stays literal, ' forces it only first.
  ⚠ WHAT WAS NOT TAKEN, and why: upstream's TYPED ARRAYS (#2f64(...)). The four
  end-of-input cases match exactly; the two cases where a prefix is followed by a
  literal are refused here with a read-error where upstream reports a wrong-type-arg
  out of make-generalized-vector or length. Reading them as untyped arrays was tried
  and REVERTED -- it answered #2(()) for #2x(a), a plausible WRONG value, which is
  worse than either error. Implementing typed arrays is its own piece of work and
  needs the uniform-vector types this package does not have.
  ⚠ AND #u8(...) IS STILL REFUSED. It is SRFI-4 u8vector syntax; #vu8(...) is the
  R6RS spelling and is read. Upstream prints the two differently (#u8 vs #vu8) even
  though the bytes are the same, so supporting the reader half alone would trade one
  divergence for another. SRFI-4 remains a declared non-goal.
* WHY set-port-encoding! IS REAL. It was once a no-op that accepted its arguments
  and did nothing, which turned every octet above 0x7F into two UTF-8 bytes --
  nothing failed, and the corruption was only ever visible to whatever later READ
  the file. A stub that answers plausibly is worse than one that throws.
* WHY READS ASK FOR FileShare.ReadWrite. Windows ENFORCES share modes and POSIX
  does not, so File.ReadAllBytes's default FileShare.Read refuses a file that
  anything else holds open for writing. Scheme here is entitled to leave a port
  open, and Guile on a POSIX host reads such a file without complaint. Every read
  behind open-file, open-input-file, call-with-input-file and load therefore goes
  through Runtime/HostFile.
* WHY THE EXCEPTION DISPATCH DIFFERS FROM boot-9's FLUID WALK. The classic side
  here is .NET exceptions rather than prompts: a non-continuable raise-exception
  simply throws the SchemeThrow its object decodes to, and .NET propagation visits
  every intervening frame innermost first -- which IS boot-9's handler-stack order.
  Only raise-continuable walks an explicit per-interpreter handler stack, because a
  non-unwinding handler's return value must flow back to the raise point and no
  .NET throw can do that. A non-unwinding handler runs INSIDE an exception filter,
  pre-unwind; its non-local exits are carried out through the catch block and
  rethrown, because the CLR silently swallows any exception escaping a filter --
  guard's clause dispatch escapes its handler by abort-to-prompt and depends on
  this. RECORDED DIVERGENCE: a non-local exit from a non-unwinding handler
  continues from the with-exception-handler frame rather than from the raise point,
  the same bounded shape as the with-throw-handler divergence in ControlPrimitives.
* WHY THE stat SLOTS .NET CANNOT ANSWER HOLD #f. A visible non-answer rather than a
  plausible zero. The tm: accessor family is copied verbatim into the prelude from
  boot-9.scm:2037 because boot-9 never loads; posix.scm's stat:/passwd:/group:
  accessors are loaded by the prelude with the same include-from-path that loads
  quasisyntax. posix.scm's tail defines getpwent-family wrappers over
  getpw/setpw/getgr/setgr, which are UNBOUND -- calling one is a visible
  unbound-variable error, the ABOUT boot-9 posture.
* THE REGEXP TRANSLATION LAYER. The surface is libguile/regex-posix.c's, exactly
  enough for the vendored ice-9/regex.scm to load VERBATIM on top. Three constructs
  are translated, all inside bracket expressions: [[:class:]] (with digit and
  xdigit fixed to ASCII, where .NET's \d is Unicode -- the discriminating fence), a
  ] in first position (a POSIX literal), and a backslash (a POSIX LITERAL inside
  brackets, an escape in .NET). Everything else passes through -- ERE is a subset of
  .NET's syntax there. RECORDED DIVERGENCE: alternation is .NET's leftmost-FIRST,
  not POSIX's leftmost-longest. Nothing in the corpus or the fences turns on it; a
  caller that does gets its own ruling that day.
* THE EXPANSION CACHE'S c&e RULE. In psyntax's default `e' mode a top-level
  define-syntax installs its macro purely as an expansion-time side effect and
  returns void-ish Tree-IL -- a replayed boot rebuilds every value binding and NO
  macros, and dies on the first LIVE expansion afterwards. c&e is upstream's own
  file-compilation mode: the expander EVALUATES each form itself and returns Tree-IL
  that rebuilds the same state. Because the expander already evaluated, LoadExpanded
  records WITHOUT evaluating again -- re-evaluating re-runs every form, and a re-run
  (define-module ...) re-creates the module out from under the expander's own state.
  The consumer keys its cache file on the LilyScheme + engine assembly MVIDs plus
  every embedded .scm resource's content; the family's minute-stamped versioning
  means ANY rebuild changes the MVIDs, so the first boot after a rebuild re-records
  once (~half a minute) and every boot until the next rebuild replays (~50 ms for
  the whole layer). ExpansionCacheTests fences all four rules.

NOTES
=====
* Do not add a .github/workflows/*.yml file to this repository.
* The AI-agent pointer stubs (AGENTS.md, CLAUDE.md, .clinerules, .cursorrules,
  .cursor/rules/agent-readme.mdc, .windsurfrules,
  .github/copilot-instructions.md, .junie/guidelines.md) all point at
  README-INDEX.txt and are maintained centrally across the CodeBrix family. Do not
  edit them here.
* AGENT-README.txt is packed into the .nupkg. Anything added to it ships to every
  consumer, so repository-internal material belongs in this file instead.
