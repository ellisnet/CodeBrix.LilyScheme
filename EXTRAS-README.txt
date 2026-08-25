================================================================================
EXTRAS-README: CodeBrix.LilyScheme
Samples, tools and other content in this repository that is not part of a NuGet package
================================================================================

This repository ships one NuGet package and has no sample applications and no
demo projects. Two things in the tree are not part of the package:

tools/unicode-names/generate-unicode-names.py
=============================================
WHAT IT IS
    The generator for the Unicode formal-name table the library embeds as
    src/CodeBrix.LilyScheme/Unicode/unicode-names.deflate -- the table that
    (ice-9 unicode)'s char->formal-name and formal-name->char answer from, and
    that Unicode.UnicodeCharacterNames reads. Guile implements both procedures
    over GNU libunistring; there is no managed equivalent, so the names ship.

WHY IT EXISTS AS A TOOL RATHER THAN A BUILD STEP
    Regenerating the table from a different Unicode Character Database is a
    DELIBERATE ACT, not a refresh. Character names are stable once assigned, but
    each Unicode release adds thousands, so the version the table was built from
    is recorded in the table's own first line and verified by --check. Nothing in
    the build regenerates it.

HOW TO RUN IT
    python3 tools/unicode-names/generate-unicode-names.py \
        [--check] [--version X.Y.Z] [UnicodeData.txt] [out.deflate]

    With no arguments it reads /usr/share/unicode/UnicodeData.txt and writes
    src/CodeBrix.LilyScheme/Unicode/unicode-names.deflate. --check verifies the
    shipped table against the source instead of rewriting it. The UCD version is
    read from the ReadMe.txt beside UnicodeData.txt when there is one, and can be
    given explicitly with --version; the script refuses to guess.

    Python 3 with the standard library only -- no packages to install.

WHAT IT DEMONSTRATES, AND THE ONE DECISION WORTH KNOWING
    Only rows of UnicodeData.txt that carry a LITERAL name go in. A row whose
    name field is bracketed (<control>, <CJK Ideograph, First>,
    <Hangul Syllable, Last>, ...) is a range marker or an unnamed character --
    exactly the ALGORITHMIC ranges, which a library MAY derive arithmetically
    instead of looking up. THAT EXCLUSION IS A MEASUREMENT, NOT A
    SIMPLIFICATION: Guile answers #f for a CJK ideograph rather than deriving
    "CJK UNIFIED IDEOGRAPH-898B", measured against 316 occurrences of a
    "no glyph for character" warning across 79 distinct characters in a reference
    corpus, all 316 agreeing including the one negative. Python's own unicodedata
    DOES derive the algorithmic names and would have been the wrong authority to
    copy.

    The shipped form is plain text -- a header line, then "HEX;NAME" per line,
    deflated -- rather than a packed binary, on purpose: a reader can check what
    was shipped with one zlib call and no format knowledge. The header line is
    also what carries the Unicode License's modification notice with the BYTES
    rather than only in the documentation.

LICENSING
    UnicodeData.txt is Unicode, Inc.'s, under the Unicode License; the derived
    table is recorded in THIRD-PARTY-NOTICES.txt section 6. Only the code point
    and the name are taken; the other thirteen fields are dropped, and that
    modification is stated in the table's own header line, in the script, and in
    the notices file.

tests/CodeBrix.LilyScheme.Tests
==============================
The xUnit v3 test suite. It is not part of the package and is not a sample, but
it IS the executable specification for the library's behaviour -- including the
host-embedding surface, which WrongTypeArgumentTests exercises through
Interpreter.DefinePrimitive, and the expansion cache, which ExpansionCacheTests
records, serializes and replays end to end.

The suite is LONG-RUNNING by design. Consumers should read it rather than run
it; AGENT-README.txt's WORKING EXAMPLES ON GITHUB section links the files worth
reading, by task. Maintainers run it with `dotnet test CodeBrix.LilyScheme.slnx'
before shipping -- see MAINTAINER-README.txt for what each class fences.
