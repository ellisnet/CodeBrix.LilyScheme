# generate-unicode-names.py -- build the Unicode formal-name table LilyScheme ships.
#
# This file is part of CodeBrix.LilyScheme.
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
# it under the terms of the GNU Lesser General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# ----------------------------------------------------------------------------
# WHY THIS EXISTS
#
# Guile's (ice-9 unicode) exports char->formal-name and formal-name->char, and
# implements both in libguile/unicode.c over GNU libunistring's uniname. Neither
# is available to a managed reimplementation, so LilyScheme carries the table.
#
# WHAT GOES IN, AND WHAT DELIBERATELY DOES NOT.  Only rows of UnicodeData.txt
# that carry a LITERAL name.  A row whose name field is bracketed (<control>,
# <CJK Ideograph, First>, <Hangul Syllable, Last>, ...) is a range marker or an
# unnamed character, and those are exactly the ALGORITHMIC ranges -- the ones a
# library may derive arithmetically instead of looking up.
#
# THAT EXCLUSION IS A MEASUREMENT, NOT A SIMPLIFICATION.  Guile answers #f for a
# CJK ideograph rather than deriving "CJK UNIFIED IDEOGRAPH-898B": measured
# against 316 occurrences of LilyPond's "no glyph for character" warning across
# 79 distinct characters in a reference corpus rendered by GNU LilyPond 2.27.2,
# which prints the name char->formal-name returns.  All 316 agree, including the
# one negative.  Python's own unicodedata DOES derive the algorithmic names and
# would have been the wrong authority to copy.
#
# /!\ THE TABLE IS VERSION-DEPENDENT.  Character names are stable once assigned,
# but each Unicode release adds thousands, so the version the table was built
# from is recorded in its first line and checked by --check.  Regenerating from
# a different UCD is a deliberate act, not a refresh.
#
# LICENSING.  UnicodeData.txt is Unicode, Inc.'s, under the Unicode License; the
# derived table is recorded in THIRD-PARTY-NOTICES.txt section 6.  Only the code
# point and the name are taken; the other thirteen fields are dropped, and that
# modification is stated in the table's own header line, in this script and in
# the notices file.
#
# USAGE
#     python3 generate-unicode-names.py [--check] [UnicodeData.txt] [out.deflate]
#
# The UCD version is read from the ReadMe.txt beside UnicodeData.txt when there
# is one, and may be given explicitly with --version.
# ----------------------------------------------------------------------------

import os
import re
import sys
import zlib

DEFAULT_SOURCE = "/usr/share/unicode/UnicodeData.txt"
DEFAULT_OUTPUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "src", "CodeBrix.LilyScheme", "Unicode", "unicode-names.deflate")

VERSION_IN_README = re.compile(r"Version (\d+\.\d+\.\d+) of the Unicode Standard")


def unicode_version(source_path, override):
    """The UCD version the table is being built from.

    UnicodeData.txt carries no version of its own -- by format it is bare
    semicolon-separated rows -- so it comes from the ReadMe.txt beside it, which
    is where the UCD states it.
    """
    if override:
        return override

    readme = os.path.join(os.path.dirname(os.path.abspath(source_path)), "ReadMe.txt")
    if os.path.exists(readme):
        with open(readme, encoding="utf-8", errors="replace") as handle:
            match = VERSION_IN_README.search(handle.read())
            if match:
                return match.group(1)

    raise SystemExit(
        "cannot determine the UCD version: no ReadMe.txt beside %s states one. "
        "Pass --version X.Y.Z." % source_path)


def literal_names(path):
    """[(code point, name)] for every row UnicodeData.txt gives a LITERAL name."""
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            fields = line.split(";")
            if len(fields) < 2:
                continue
            name = fields[1]
            # A bracketed name field is a range marker or an unnamed character --
            # the algorithmic ranges, which Guile does not name.
            if name.startswith("<"):
                continue
            rows.append((int(fields[0], 16), name))
    rows.sort()
    return rows


def encode(rows, version):
    """The shipped form: a header line, then 'HEX;NAME' per line, deflated.

    Plain text rather than a packed binary, on purpose: a reader can check what
    was shipped with one zlib call and no format knowledge, and the ~50 KB a
    delta-coded binary would save is not worth making the asset unreadable.
    The header line is what carries the Unicode License's modification notice
    with the BYTES rather than only in the documentation.
    """
    header = (
        "# Unicode Character Database %s -- code point and formal name only.\n"
        "# MODIFIED: derived from UnicodeData.txt by "
        "tools/unicode-names/generate-unicode-names.py; the other thirteen "
        "fields are dropped, as are the algorithmically-named ranges.\n"
        "# Copyright (c) 1991-2023 Unicode, Inc. Distributed under the Terms of "
        "Use in https://www.unicode.org/copyright.html -- see "
        "THIRD-PARTY-NOTICES.txt section 6.\n" % version)
    body = "".join("%X;%s\n" % row for row in rows)
    return zlib.compress((header + body).encode("ascii"), 9)


def main():
    arguments = sys.argv[1:]
    checking = False
    override = None

    rest = []
    index = 0
    while index < len(arguments):
        if arguments[index] == "--check":
            checking = True
        elif arguments[index] == "--version" and index + 1 < len(arguments):
            index += 1
            override = arguments[index]
        else:
            rest.append(arguments[index])
        index += 1

    source = rest[0] if rest else DEFAULT_SOURCE
    output = rest[1] if len(rest) > 1 else DEFAULT_OUTPUT

    if not os.path.exists(source):
        print("no UnicodeData.txt at %s -- pass its path as the first argument"
              % source, file=sys.stderr)
        return 2

    version = unicode_version(source, override)
    rows = literal_names(source)
    blob = encode(rows, version)

    if checking:
        if not os.path.exists(output):
            print("no committed table at %s" % output, file=sys.stderr)
            return 1
        with open(output, "rb") as handle:
            committed = handle.read()
        if committed != blob:
            print("*** the committed table differs from %s at UCD %s "
                  "(%d vs %d bytes) ***"
                  % (source, version, len(committed), len(blob)), file=sys.stderr)
            return 1
        print("unicode-names --check holds: UCD %s, %d names, %d bytes"
              % (version, len(rows), len(blob)))
        return 0

    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, "wb") as handle:
        handle.write(blob)
    print("wrote %s: UCD %s, %d names, %d bytes compressed"
          % (output, version, len(rows), len(blob)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
