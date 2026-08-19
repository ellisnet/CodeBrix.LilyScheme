// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// The remainder of Guile's core surface: SRFI-13 string procedures, SRFI-14 character
/// sets, generalized setters, the option interfaces and the small system layer.
/// <para>
/// Guile implements all of these in C, so there is no Scheme source to vendor -- its
/// <c>(srfi srfi-13)</c> and <c>(srfi srfi-14)</c> modules are re-export shims over the
/// C implementations. These are written against the SRFI documents and the Guile
/// reference manual, and are new-in-family.
/// </para>
/// </summary>
public static class GuileCorePrimitives
{
    /// <summary>Installs the primitives.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallCharSets(interpreter);
        InstallStrings(interpreter);
        InstallSymbols(interpreter);
        InstallSetters(interpreter);
        InstallOptions(interpreter);
        InstallSystem(interpreter);
        InstallRandom(interpreter);
        InstallStringPorts(interpreter);
        InstallRecords(interpreter);
        InstallRegularExpressions(interpreter);
        BuiltinClasses.Install(interpreter);
    }

    private static void InstallCharSets(Interpreter interpreter)
    {
        foreach (KeyValuePair<string, CharSet> entry in NamedCharSets())
        {
            interpreter.DefineValue(entry.Key, entry.Value);
        }

        interpreter.DefinePrimitive("char-set?", 1, 1, a => a[0] is CharSet);

        interpreter.DefinePrimitive("char-set", 0, -1, a => CharSet.Of(Characters(a, 0)));

        interpreter.DefinePrimitive("list->char-set", 1, 2, a =>
        {
            List<char> members = new List<char>();
            foreach (object item in Pair.ToList(a[0]))
            {
                members.Add(Character(item, "list->char-set"));
            }

            return Combine(members, a, 1);
        });

        interpreter.DefinePrimitive("string->char-set", 1, 2, a =>
            Combine(new List<char>(StringPrimitives.Text(a[0], "string->char-set")), a, 1));

        interpreter.DefinePrimitive("char-set-contains?", 2, 2, a =>
            AsCharSet(a[0], "char-set-contains?").Contains(Character(a[1], "char-set-contains?")));

        interpreter.DefinePrimitive("char-set-complement", 1, 1, a =>
            CharSet.Complement(AsCharSet(a[0], "char-set-complement")));

        interpreter.DefinePrimitive("char-set-union", 0, -1, a =>
            CharSet.Union(AsCharSets(a, "char-set-union")));

        interpreter.DefinePrimitive("char-set-intersection", 0, -1, a =>
            CharSet.Intersection(AsCharSets(a, "char-set-intersection")));

        interpreter.DefinePrimitive("char-set-difference", 1, -1, a =>
        {
            List<CharSet> sets = AsCharSets(a, "char-set-difference");
            CharSet first = sets[0];
            sets.RemoveAt(0);
            return CharSet.Difference(first, sets);
        });

        interpreter.DefinePrimitive("char-set-adjoin", 1, -1, a =>
        {
            CharSet baseSet = AsCharSet(a[0], "char-set-adjoin");
            List<char> added = Characters(a, 1);
            return CharSet.Union(new List<CharSet> { baseSet, CharSet.Of(added) });
        });
    }

    private static void InstallStrings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("string-tokenize", 1, 2, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-tokenize");
            CharSet set = a.Length > 1 ? AsCharSet(a[1], "string-tokenize") : CharSet.Graphic;
            List<object> tokens = new List<object>();
            StringBuilder current = new StringBuilder();
            foreach (char c in text)
            {
                if (set.Contains(c))
                {
                    current.Append(c);
                }
                else if (current.Length > 0)
                {
                    tokens.Add(new MutableString(current.ToString()));
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                tokens.Add(new MutableString(current.ToString()));
            }

            return Pair.ListFrom(tokens);
        });

        interpreter.DefinePrimitive("string-split", 2, 2, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-split");
            List<object> parts = new List<object>();
            StringBuilder current = new StringBuilder();
            foreach (char c in text)
            {
                if (Matches(a[1], c, interpreter))
                {
                    parts.Add(new MutableString(current.ToString()));
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            parts.Add(new MutableString(current.ToString()));
            return Pair.ListFrom(parts);
        });

        interpreter.DefinePrimitive("string-trim", 1, 2, a =>
            new MutableString(TrimEnds(StringPrimitives.Text(a[0], "string-trim"), a, interpreter, true, false)));

        interpreter.DefinePrimitive("string-trim-right", 1, 2, a =>
            new MutableString(TrimEnds(StringPrimitives.Text(a[0], "string-trim-right"), a, interpreter, false, true)));

        interpreter.DefinePrimitive("string-trim-both", 1, 2, a =>
            new MutableString(TrimEnds(StringPrimitives.Text(a[0], "string-trim-both"), a, interpreter, true, true)));

        interpreter.DefinePrimitive("string-rindex", 2, 4, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-rindex");
            for (int i = text.Length - 1; i >= 0; i--)
            {
                if (Matches(a[1], text[i], interpreter))
                {
                    return (long)i;
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("string-count", 2, 4, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-count");
            long count = 0;
            foreach (char c in text)
            {
                if (Matches(a[1], c, interpreter))
                {
                    count++;
                }
            }

            return count;
        });

        interpreter.DefinePrimitive("string-any", 2, 2, a =>
        {
            foreach (char c in StringPrimitives.Text(a[1], "string-any"))
            {
                object result = interpreter.Evaluator.Apply(a[0], new object[] { SchemeChar.Get(c) });
                if (Evaluator.IsTrue(result))
                {
                    return result;
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("string-every", 2, 2, a =>
        {
            object result = true;
            foreach (char c in StringPrimitives.Text(a[1], "string-every"))
            {
                result = interpreter.Evaluator.Apply(a[0], new object[] { SchemeChar.Get(c) });
                if (!Evaluator.IsTrue(result))
                {
                    return false;
                }
            }

            return result;
        });

        interpreter.DefinePrimitive("string-take", 2, 2, a =>
            new MutableString(StringPrimitives.Text(a[0], "string-take").Substring(0, Count(a[1]))));

        interpreter.DefinePrimitive("string-drop", 2, 2, a =>
            new MutableString(StringPrimitives.Text(a[0], "string-drop").Substring(Count(a[1]))));

        interpreter.DefinePrimitive("string-take-right", 2, 2, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-take-right");
            return new MutableString(text.Substring(text.Length - Count(a[1])));
        });

        interpreter.DefinePrimitive("string-drop-right", 2, 2, a =>
        {
            string text = StringPrimitives.Text(a[0], "string-drop-right");
            return new MutableString(text.Substring(0, text.Length - Count(a[1])));
        });

        interpreter.DefinePrimitive("string-pad", 2, 5, a => Pad(a, true));
        interpreter.DefinePrimitive("string-pad-right", 2, 5, a => Pad(a, false));

        interpreter.DefinePrimitive("string-reverse", 1, 3, a =>
        {
            char[] characters = StringPrimitives.Text(a[0], "string-reverse").ToCharArray();
            Array.Reverse(characters);
            return new MutableString(new string(characters));
        });

        interpreter.DefinePrimitive("string-titlecase", 1, 3, a =>
        {
            StringBuilder builder = new StringBuilder(StringPrimitives.Text(a[0], "string-titlecase"));
            bool startOfWord = true;
            for (int i = 0; i < builder.Length; i++)
            {
                builder[i] = startOfWord ? char.ToUpperInvariant(builder[i]) : char.ToLowerInvariant(builder[i]);
                startOfWord = !char.IsLetter(builder[i]);
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-delete", 2, 4, a =>
            FilterString(a, interpreter, "string-delete", keepMatches: false));

        interpreter.DefinePrimitive("string-filter", 2, 4, a =>
            FilterString(a, interpreter, "string-filter", keepMatches: true));

        interpreter.DefinePrimitive("string-map", 2, -1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in StringPrimitives.Text(a[1], "string-map"))
            {
                object mapped = interpreter.Evaluator.Apply(a[0], new object[] { SchemeChar.Get(c) });
                builder.Append((char)((SchemeChar)mapped).CodePoint);
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-for-each", 2, -1, a =>
        {
            foreach (char c in StringPrimitives.Text(a[1], "string-for-each"))
            {
                interpreter.Evaluator.Apply(a[0], new object[] { SchemeChar.Get(c) });
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("string-concatenate", 1, 1, a =>
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in Pair.ToList(a[0]))
            {
                builder.Append(StringPrimitives.Text(item, "string-concatenate"));
            }

            return new MutableString(builder.ToString());
        });

        // SRFI-13's string-concatenate-reverse: the list is reversed before
        // concatenation, with an optional final string of which only the first END
        // characters are taken. pretty-print's truncating writer accumulates its
        // chunks in reverse and rebuilds the text with this.
        interpreter.DefinePrimitive("string-concatenate-reverse", 1, 3, a =>
        {
            List<object> items = Pair.ToList(a[0]);
            StringBuilder builder = new StringBuilder();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                builder.Append(StringPrimitives.Text(items[i], "string-concatenate-reverse"));
            }

            if (a.Length > 1)
            {
                string final = StringPrimitives.Text(a[1], "string-concatenate-reverse");
                int end = a.Length > 2 ? (int)SchemeNumber.ToBigInteger(a[2]) : final.Length;
                if (end < 0 || end > final.Length)
                {
                    throw new SchemeThrow(
                        Symbol.Intern("out-of-range"),
                        Pair.List(
                            new MutableString("string-concatenate-reverse"),
                            new MutableString("Argument out of range: ~S"),
                            Pair.List(a[2]),
                            false));
                }

                builder.Append(final, 0, end);
            }

            return new MutableString(builder.ToString());
        });

        interpreter.DefinePrimitive("string-ci=?", 1, -1, a =>
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (!string.Equals(
                        StringPrimitives.Text(a[i], "string-ci=?"),
                        StringPrimitives.Text(a[i + 1], "string-ci=?"),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static void InstallSymbols(Interpreter interpreter)
    {
        // Case-insensitive character ordering; R7RS names these separately from the
        // case-sensitive family rather than taking a flag.
        DefineCharCi(interpreter, "char-ci=?", result => result == 0);
        DefineCharCi(interpreter, "char-ci<?", result => result < 0);
        DefineCharCi(interpreter, "char-ci>?", result => result > 0);
        DefineCharCi(interpreter, "char-ci<=?", result => result <= 0);
        DefineCharCi(interpreter, "char-ci>=?", result => result >= 0);

        // (procedure-documentation proc) -- the procedure's DOCSTRING, or #f when it has
        // none. It used to answer the procedure's NAME, which is a different question and
        // never #f for a named procedure: LilyPond's documentation generator prints the
        // answer straight into the manual, so every markup command would have documented
        // itself with its own name as its description.
        //
        // The docstring reaches a Tree-IL closure through the lambda's meta alist, which
        // is where psyntax puts it (see TreeIlClosure.Documentation). A procedure-property
        // of the same name wins if something set one explicitly, as Guile's own
        // set-procedure-property! does.
        interpreter.DefinePrimitive("procedure-documentation", 1, 1, a =>
        {
            if (!(a[0] is Procedure procedure))
            {
                return false;
            }

            foreach (object entry in Pair.ToList(procedure.Properties))
            {
                if (entry is Pair pair
                    && pair.Car is Symbol key
                    && string.Equals(key.Name, "documentation", StringComparison.Ordinal))
                {
                    return pair.Cdr;
                }
            }

            return procedure is TreeIl.TreeIlClosure closure && closure.Documentation != null
                ? (object)new MutableString(closure.Documentation)
                : false;
        });

        // An uninterned symbol: equal to nothing but itself, which is what makes it safe
        // as a hidden key.
        interpreter.DefinePrimitive("make-symbol", 1, 1, a =>
            Symbol.Generate(StringPrimitives.Text(a[0], "make-symbol")));

        interpreter.DefinePrimitive("symbol<?", 1, -1, a =>
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (string.CompareOrdinal(
                        TypeChecks.AsSymbol(a[i], "symbol<?", i + 1).Name,
                        TypeChecks.AsSymbol(a[i + 1], "symbol<?", i + 2).Name) >= 0)
                {
                    return false;
                }
            }

            return true;
        });
    }

    /// <summary>
    /// Defines one of the case-insensitive character comparisons.
    /// <para>
    /// The fold is UPWARD, and that is not interchangeable with folding down.
    /// <c>libguile/chars.c</c> compares <c>scm_c_upcase (x)</c> against
    /// <c>scm_c_upcase (y)</c> in all five of them (lines 238, 268, 299, 329, 360), so
    /// every letter folds BELOW the punctuation that sits between the two ASCII cases
    /// — <c>[ \ ] ^ _ `</c> — instead of above it. Folding down instead agrees with
    /// Guile on every pair of letters and disagrees on every letter-versus-backslash
    /// pair, which is invisible until something sorts identifiers that begin with one:
    /// LilyPond's Internals Reference lists \=, \%, \* and \~ after the alphabet, and
    /// the port listed them before it.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <param name="name">The primitive's name.</param>
    /// <param name="accept">Maps a comparison result onto the answer.</param>
    private static void DefineCharCi(Interpreter interpreter, string name, Func<int, bool> accept)
        => interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                int left = char.ToUpperInvariant(
                    (char)TypeChecks.AsChar(a[i], name, i + 1).CodePoint);
                int right = char.ToUpperInvariant(
                    (char)TypeChecks.AsChar(a[i + 1], name, i + 2).CodePoint);
                if (!accept(left.CompareTo(right)))
                {
                    return false;
                }
            }

            return true;
        });

    private static void InstallSetters(Interpreter interpreter)
    {
        // Guile's generalized set!: (set! (proc args ...) value) expands to
        // ((setter proc) args ... value), so a procedure has to be able to carry one.
        interpreter.DefinePrimitive("make-procedure-with-setter", 2, 2, a =>
        {
            object getter = a[0];
            Primitive wrapper = new Primitive(
                (getter as Procedure)?.Name ?? "procedure-with-setter",
                0,
                -1,
                arguments => interpreter.Evaluator.Apply(getter, arguments));
            wrapper.Setter = a[1];
            return wrapper;
        });

        interpreter.DefinePrimitive("procedure-with-setter?", 1, 1, a =>
            a[0] is Procedure procedure && procedure.Setter != null);

        interpreter.DefinePrimitive("setter", 1, 1, a =>
        {
            if (a[0] is Procedure procedure && procedure.Setter != null)
            {
                return procedure.Setter;
            }

            throw new SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(
                    new MutableString("setter"),
                    new MutableString("Not a procedure with setter: ~S"),
                    Pair.List(a[0]),
                    false));
        });

        interpreter.DefinePrimitive("set-procedure-setter!", 2, 2, a =>
        {
            if (a[0] is Procedure procedure)
            {
                procedure.Setter = a[1];
            }

            return Unspecified.Instance;
        });
    }

    private static void InstallOptions(Interpreter interpreter)
    {
        // Guile's option interfaces. LilyPond turns backtraces off at startup and never
        // reads any of these back, so recording the flags is enough; nothing in
        // LilyScheme changes behaviour on them.
        foreach (string family in new[] { "debug", "read", "print" })
        {
            string captured = family;
            interpreter.DefinePrimitive(captured + "-enable", 0, -1, a => Nil.Instance);
            interpreter.DefinePrimitive(captured + "-disable", 0, -1, a => Nil.Instance);
            interpreter.DefinePrimitive(captured + "-options", 0, 1, a => Nil.Instance);
            interpreter.DefinePrimitive(captured + "-options-interface", 0, 1, a => Nil.Instance);
        }

        // (debug-set! stack 0) names its option, it does not evaluate it, so debug-set!
        // and its siblings have to be syntax. They are defined in the prelude.
        interpreter.DefinePrimitive("gettext", 1, 3, a => a[0]);
        interpreter.DefinePrimitive("ngettext", 3, 5, a =>
            ToLong(a[2]) == 1 ? a[0] : a[1]);
        interpreter.DefinePrimitive("textdomain", 0, 1, a => new MutableString("lilypond"));
        interpreter.DefinePrimitive("bindtextdomain", 1, 2, a => new MutableString(string.Empty));
        interpreter.DefinePrimitive("bind-textdomain-codeset", 1, 2, a => new MutableString("UTF-8"));
    }

    private static void InstallSystem(Interpreter interpreter)
    {
        // (uname) returns a five-element vector; the utsname:* accessors index into it.
        interpreter.DefinePrimitive("uname", 0, 0, a => new object[]
        {
            new MutableString(SystemName()),
            new MutableString(Environment.MachineName),
            new MutableString(Environment.OSVersion.Version.ToString()),
            new MutableString(Environment.OSVersion.VersionString),
            new MutableString(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()),
        });

        DefineVectorAccessor(interpreter, "utsname:sysname", 0);
        DefineVectorAccessor(interpreter, "utsname:nodename", 1);
        DefineVectorAccessor(interpreter, "utsname:release", 2);
        DefineVectorAccessor(interpreter, "utsname:version", 3);
        DefineVectorAccessor(interpreter, "utsname:machine", 4);

        interpreter.DefinePrimitive("getenv", 1, 1, a =>
        {
            string value = Environment.GetEnvironmentVariable(StringPrimitives.Text(a[0], "getenv"));
            return value == null ? (object)false : new MutableString(value);
        });

        interpreter.DefinePrimitive("setenv", 2, 2, a =>
        {
            Environment.SetEnvironmentVariable(
                StringPrimitives.Text(a[0], "setenv"),
                a[1] is MutableString || a[1] is string ? StringPrimitives.Text(a[1], "setenv") : null);
            return Unspecified.Instance;
        });

        // Guile's explicit collection request. LilyPond calls it between sessions and
        // around the point-and-click cleanup; on the CLR the runtime owns collection
        // policy, so this asks rather than commands, which is all (gc) ever promised.
        interpreter.DefinePrimitive("gc", 0, 0, a =>
        {
            GC.Collect();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("getpid", 0, 0, a => (long)Environment.ProcessId);
        interpreter.DefinePrimitive("current-time", 0, 0, a => (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        interpreter.DefinePrimitive("file-exists?", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "file-exists?");
            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        });

        // (mkdir path [mode]) -- Guile raises a system-error when the directory already
        // exists, and LilyPond's backend-library.scm mkdir-if-not-exist CATCHES that to
        // decide whether it had to create one. Creating silently instead would make that
        // helper answer "I created it" every time. The permission mode is accepted and
        // ignored: .NET has no portable chmod, and the callers pass it for a umask effect
        // rather than to make the directory unreadable.
        interpreter.DefinePrimitive("mkdir", 1, 2, a =>
        {
            string path = StringPrimitives.Text(a[0], "mkdir");
            if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
            {
                throw new SchemeThrow(
                    Symbol.Intern("system-error"),
                    Pair.List(
                        new MutableString("mkdir"),
                        new MutableString("~A"),
                        Pair.List(new MutableString("File exists")),
                        false));
            }

            System.IO.Directory.CreateDirectory(path);
            return Unspecified.Instance;
        });

        InstallDirectoryStreams(interpreter);
    }

    /// <summary>
    /// Guile's directory-walking primitives: <c>opendir</c>, <c>readdir</c>,
    /// <c>closedir</c>, <c>directory-stream?</c>, plus <c>rmdir</c> and
    /// <c>delete-file</c>.
    /// <para>
    /// A directory stream is an opaque object that yields one entry NAME per
    /// <c>readdir</c> and the EOF object when the directory is exhausted. The entries
    /// include <c>.</c> and <c>..</c>, because Guile hands back what the C library hands
    /// it and every caller written against Guile filters them itself — a stream that
    /// helpfully omitted them would silently change what such a loop counts.
    /// </para>
    /// <para>
    /// The failure shape is Guile's too: a <c>system-error</c> throw carrying the
    /// procedure name, so a caller's <c>catch</c> sees what it sees under Guile.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallDirectoryStreams(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("opendir", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "opendir");
            if (!System.IO.Directory.Exists(path))
            {
                throw SystemError("opendir", "No such file or directory");
            }

            return new DirectoryStream(path);
        });

        interpreter.DefinePrimitive("readdir", 1, 1, a =>
        {
            if (!(a[0] is DirectoryStream stream))
            {
                throw WrongTypeArgument("readdir", a[0]);
            }

            string entry = stream.Next();
            return entry == null ? (object)EofObject.Instance : new MutableString(entry);
        });

        interpreter.DefinePrimitive("closedir", 1, 1, a =>
        {
            if (!(a[0] is DirectoryStream stream))
            {
                throw WrongTypeArgument("closedir", a[0]);
            }

            stream.Close();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("directory-stream?", 1, 1, a => a[0] is DirectoryStream);

        interpreter.DefinePrimitive("rmdir", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "rmdir");
            if (!System.IO.Directory.Exists(path))
            {
                throw SystemError("rmdir", "No such file or directory");
            }

            try
            {
                // NON-RECURSIVE, which is rmdir(2). A caller that has not emptied the
                // directory must get the error rather than losing its contents.
                System.IO.Directory.Delete(path, false);
            }
            catch (System.IO.IOException)
            {
                throw SystemError("rmdir", "Directory not empty");
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("delete-file", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "delete-file");
            if (!System.IO.File.Exists(path))
            {
                throw SystemError("delete-file", "No such file or directory");
            }

            System.IO.File.Delete(path);
            return Unspecified.Instance;
        });
    }

    private static SchemeThrow WrongTypeArgument(string procedure, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedure),
                new MutableString("Wrong type argument: ~S"),
                Pair.List(value),
                false));

    private static SchemeThrow SystemError(string procedure, string message)
        => new SchemeThrow(
            Symbol.Intern("system-error"),
            Pair.List(
                new MutableString(procedure),
                new MutableString("~A"),
                Pair.List(new MutableString(message)),
                false));

    private static void InstallRandom(Interpreter interpreter)
    {
        Random[] state = { new Random(0) };
        interpreter.DefineValue("*random-state*", new MutableString("random-state"));

        interpreter.DefinePrimitive("seed->random-state", 1, 1, a =>
        {
            state[0] = new Random(unchecked((int)ToLong(a[0])));
            return new MutableString("random-state");
        });

        interpreter.DefinePrimitive("random", 1, 2, a =>
        {
            if (a[0] is double limit)
            {
                return state[0].NextDouble() * limit;
            }

            long bound = ToLong(a[0]);
            return bound <= 0 ? 0L : (long)state[0].NextInt64(bound);
        });

        interpreter.DefinePrimitive("random:uniform", 0, 1, a => state[0].NextDouble());
    }

    private static void InstallStringPorts(Interpreter interpreter)
    {
        // Guile's ports track their own position; the documentation generators read it
        // back to decide when to wrap a line.
        interpreter.DefinePrimitive("port-column", 1, 1, a =>
        {
            if (a[0] is SchemeOutputPort port)
            {
                if (port.Writer is SoftPortWriter soft)
                {
                    return soft.Column;
                }

                if (port.Writer is System.IO.StringWriter writer)
                {
                    string text = writer.ToString();
                    int newline = text.LastIndexOf('\n');
                    return (long)(text.Length - newline - 1);
                }
            }

            return 0L;
        });

        interpreter.DefinePrimitive("port-line", 1, 1, a =>
        {
            if (a[0] is SchemeOutputPort port)
            {
                if (port.Writer is SoftPortWriter soft)
                {
                    return soft.Line;
                }

                if (port.Writer is System.IO.StringWriter writer)
                {
                    long lines = 0;
                    foreach (char c in writer.ToString())
                    {
                        if (c == '\n')
                        {
                            lines++;
                        }
                    }

                    return lines;
                }
            }

            return 0L;
        });

        interpreter.DefinePrimitive("set-port-column!", 2, 2, a => Unspecified.Instance);
        interpreter.DefinePrimitive("set-port-line!", 2, 2, a => Unspecified.Instance);
        interpreter.DefinePrimitive("set-port-filename!", 2, 2, a => Unspecified.Instance);
        interpreter.DefinePrimitive("drain-input", 1, 1, a => new MutableString(string.Empty));

        interpreter.DefinePrimitive("ftell", 1, 1, a =>
            a[0] is SchemeOutputPort port && port.Writer is System.IO.StringWriter writer
                ? (long)writer.ToString().Length
                : 0L);

        interpreter.DefinePrimitive("open-output-string", 0, 0, a =>
            new SchemeOutputPort(new System.IO.StringWriter()));

        interpreter.DefinePrimitive("get-output-string", 1, 1, a =>
            a[0] is SchemeOutputPort port && port.Writer is System.IO.StringWriter writer
                ? new MutableString(writer.ToString())
                : new MutableString(string.Empty));

        interpreter.DefinePrimitive("call-with-output-string", 1, 1, a =>
        {
            System.IO.StringWriter writer = new System.IO.StringWriter();
            interpreter.Evaluator.Apply(a[0], new object[] { new SchemeOutputPort(writer) });
            return new MutableString(writer.ToString());
        });

        // with-output-to-string redirects the CURRENT output port for the duration, so
        // a thunk that just calls display -- with no port argument -- is captured too.
        interpreter.DefinePrimitive("with-output-to-string", 1, 1, a =>
        {
            System.IO.TextWriter saved = interpreter.OutputWriter;
            System.IO.StringWriter writer = new System.IO.StringWriter();
            interpreter.OutputWriter = writer;
            try
            {
                interpreter.Evaluator.Apply(a[0], Array.Empty<object>());
            }
            finally
            {
                interpreter.OutputWriter = saved;
            }

            return new MutableString(writer.ToString());
        });

        interpreter.DefinePrimitive("call-with-input-string", 2, 2, a =>
            interpreter.Evaluator.Apply(
                a[1],
                new object[] { new SchemeInputPort(StringPrimitives.Text(a[0], "call-with-input-string"), "<string>") }));
    }

    private static void InstallRecords(Interpreter interpreter)
    {
        // Guile builds define-record-type on these four, and so does the prelude. A
        // record is a vector whose slot 0 holds its type, which is what makes the
        // predicate cheap and the type unforgeable from Scheme.
        //
        // The single-inheritance half is boot-9.scm's records section: #:parent lays
        // out the parent's fields FIRST, record-type-fields answers the complete
        // layout, only an #:extensible? #t type may be a parent ("parent type is
        // final"), and a field spec is a bare symbol (mutable) or an
        // (immutable name) / (mutable name) pair — the spelling the vendored
        // ice-9/exceptions.scm uses. #:uid (the prefab registry), #:opaque? and
        // #:allow-duplicate-field-names? are REFUSED loudly until something needs them.
        interpreter.DefinePrimitive("make-record-type", 2, -1, a =>
        {
            string name = StringPrimitives.Text(a[0], "make-record-type");
            List<object> specs = Pair.ToList(a[1]);
            List<object> fields = new List<object>(specs.Count);
            bool[] mutability = new bool[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i] is Symbol plain)
                {
                    fields.Add(plain);
                    mutability[i] = true;
                }
                else if (specs[i] is Pair spec
                         && spec.Car is Symbol marker
                         && (marker.Name == "immutable" || marker.Name == "mutable")
                         && spec.Cdr is Pair tail
                         && tail.Car is Symbol fieldName
                         && tail.Cdr is Nil)
                {
                    fields.Add(fieldName);
                    mutability[i] = marker.Name == "mutable";
                }
                else
                {
                    throw new SchemeThrow(
                        Symbol.Intern("misc-error"),
                        Pair.List(
                            new MutableString("make-record-type"),
                            new MutableString("bad field declaration: ~S"),
                            Pair.List(specs[i]),
                            false));
                }
            }

            RecordType parent = null;
            bool extensible = false;
            int cursor = 2;
            if (cursor < a.Length && !(a[cursor] is Keyword))
            {
                // The optional positional printer; custom record printing is not
                // modelled, so it is accepted and ignored as before.
                cursor++;
            }

            for (; cursor + 1 < a.Length; cursor += 2)
            {
                if (!(a[cursor] is Keyword keyword))
                {
                    throw new SchemeThrow(
                        Symbol.Intern("wrong-type-arg"),
                        Pair.List(
                            new MutableString("make-record-type"),
                            new MutableString("Wrong type (expecting keyword): ~S"),
                            Pair.List(a[cursor]),
                            false));
                }

                switch (keyword.Name.Name)
                {
                    case "parent":
                        if (a[cursor + 1] is RecordType parentType)
                        {
                            parent = parentType;
                        }
                        else if (!(a[cursor + 1] is bool falseParent && !falseParent))
                        {
                            throw new SchemeThrow(
                                Symbol.Intern("misc-error"),
                                Pair.List(
                                    new MutableString("make-record-type"),
                                    new MutableString("expected parent to be a record type: ~S"),
                                    Pair.List(a[cursor + 1]),
                                    false));
                        }

                        break;
                    case "extensible?":
                        extensible = !(a[cursor + 1] is bool flag && !flag);
                        break;
                    default:
                        throw new SchemeThrow(
                            Symbol.Intern("misc-error"),
                            Pair.List(
                                new MutableString("make-record-type"),
                                new MutableString("unsupported make-record-type option: ~S"),
                                Pair.List(keyword),
                                false));
                }
            }

            if (parent != null && !parent.Extensible)
            {
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("make-record-type"),
                        new MutableString("parent type is final: ~S"),
                        Pair.List(parent),
                        false));
            }

            return new RecordType(name, fields, parent, extensible, mutability);
        });

        interpreter.DefinePrimitive("record-type?", 1, 1, a => a[0] is RecordType);

        // Guile's record-type-name answers the type-name SYMBOL (boot-9's name-sym).
        interpreter.DefinePrimitive("record-type-name", 1, 1, a =>
            a[0] is RecordType type ? (object)Symbol.Intern(type.Name) : false);

        interpreter.DefinePrimitive("record-type-fields", 1, 1, a =>
            a[0] is RecordType type ? Pair.ListFrom(type.Fields) : (object)Nil.Instance);

        interpreter.DefinePrimitive("record-type-parent", 1, 1, a =>
            (object)AsRecordType(a[0], "record-type-parent").Parent ?? false);

        interpreter.DefinePrimitive("record-type-parents", 1, 1, a =>
        {
            RecordType type = AsRecordType(a[0], "record-type-parents");
            object[] parents = new object[type.Ancestors.Count];
            for (int i = 0; i < parents.Length; i++)
            {
                parents[i] = type.Ancestors[i];
            }

            return parents;
        });

        interpreter.DefinePrimitive("record-type-has-parent?", 2, 2, a =>
            AsRecordType(a[0], "record-type-has-parent?")
                .HasParent(AsRecordType(a[1], "record-type-has-parent?")));

        interpreter.DefinePrimitive("record-type-extensible?", 1, 1, a =>
            AsRecordType(a[0], "record-type-extensible?").Extensible);

        interpreter.DefinePrimitive("record?", 1, 1, a =>
            a[0] is object[] vector && vector.Length > 0 && vector[0] is RecordType);

        interpreter.DefinePrimitive("record-constructor", 1, 2, a =>
        {
            RecordType type = AsRecordType(a[0], "record-constructor");
            List<object> argumentFields = a.Length > 1 && !(a[1] is DefaultArgument)
                ? Pair.ToList(a[1])
                : new List<object>(type.Fields);

            return new Primitive(type.Name, argumentFields.Count, argumentFields.Count, arguments =>
            {
                object[] instance = new object[type.Fields.Count + 1];
                instance[0] = type;
                for (int i = 0; i < argumentFields.Count; i++)
                {
                    instance[type.IndexOf(argumentFields[i]) + 1] = arguments[i];
                }

                return instance;
            });
        });

        // The predicate accepts instances of SUBTYPES too — what an extensible type
        // promises, and what exception-predicate over &exception is built on.
        interpreter.DefinePrimitive("record-predicate", 1, 1, a =>
        {
            RecordType type = AsRecordType(a[0], "record-predicate");
            return new Primitive(type.Name + "?", 1, 1, arguments => type.IsInstance(arguments[0]));
        });

        interpreter.DefinePrimitive("record-accessor", 2, 2, a =>
        {
            RecordType type = AsRecordType(a[0], "record-accessor");
            int index = RecordFieldIndex(type, a[1], "record-accessor") + 1;
            return new Primitive(type.Name + "-ref", 1, 1, arguments =>
                arguments[0] is object[] vector && index < vector.Length ? vector[index] : false);
        });

        interpreter.DefinePrimitive("record-modifier", 2, 2, a =>
        {
            RecordType type = AsRecordType(a[0], "record-modifier");
            int fieldIndex = RecordFieldIndex(type, a[1], "record-modifier");
            if (!type.IsFieldMutable(fieldIndex))
            {
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("record-modifier"),
                        new MutableString("field is immutable: ~S"),
                        Pair.List(a[1]),
                        false));
            }

            int index = fieldIndex + 1;
            return new Primitive(type.Name + "-set!", 2, 2, arguments =>
            {
                if (arguments[0] is object[] vector && index < vector.Length)
                {
                    vector[index] = arguments[1];
                }

                return Unspecified.Instance;
            });
        });
    }

    private static int RecordFieldIndex(RecordType type, object field, string procedureName)
    {
        int index = type.IndexOf(field);
        if (index < 0)
        {
            throw new SchemeThrow(
                Symbol.Intern("misc-error"),
                Pair.List(
                    new MutableString(procedureName),
                    new MutableString("no such field in record type ~a: ~S"),
                    Pair.List(Symbol.Intern(type.Name), field),
                    false));
        }

        return index;
    }

    private static void InstallRegularExpressions(Interpreter interpreter)
    {
        // The surface is libguile/regex-posix.c's, exactly enough for the vendored
        // ice-9/regex.scm to load VERBATIM on top: make-regexp takes flag INTEGERS as
        // separate rest arguments (Guile ORs them itself, and detects regexp/basic by
        // equality with 0); regexp-exec answers Guile's match VECTOR — slot 0 the
        // target string, slot i+1 the (start . end) pair of group i, (-1 . -1) for a
        // group that did not participate — with an optional start offset and eflags.
        //
        // The PATTERN dialect is POSIX ERE translated onto .NET's engine: the
        // [[:class:]] forms, a leading ] in a bracket expression, and POSIX's
        // literal-backslash-inside-brackets rule are translated; everything else is
        // handed to .NET as it stands. TWO recorded divergences: alternation is
        // .NET's leftmost-FIRST rather than POSIX's leftmost-longest, and
        // regexp/basic (BRE) and regexp/noteol are REFUSED loudly rather than
        // half-served — see AGENT-README "POSIX REGULAR EXPRESSIONS".
        interpreter.DefineValue("regexp/basic", 0L);
        interpreter.DefineValue("regexp/extended", 1L);
        interpreter.DefineValue("regexp/icase", 2L);
        interpreter.DefineValue("regexp/newline", 4L);
        interpreter.DefineValue("regexp/notbol", 1L);
        interpreter.DefineValue("regexp/noteol", 2L);

        interpreter.DefinePrimitive("make-regexp", 1, -1, a =>
        {
            string pattern = StringPrimitives.Text(a[0], "make-regexp");
            System.Text.RegularExpressions.RegexOptions options
                = System.Text.RegularExpressions.RegexOptions.None;
            for (int i = 1; i < a.Length; i++)
            {
                long flag = (long)SchemeNumber.ToBigInteger(a[i]);
                if (flag == 0)
                {
                    throw new SchemeThrow(
                        Symbol.Intern("misc-error"),
                        Pair.List(
                            new MutableString("make-regexp"),
                            new MutableString("regexp/basic is not supported by this port"),
                            false,
                            false));
                }

                if ((flag & 2) != 0)
                {
                    options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                }

                if ((flag & 4) != 0)
                {
                    // REG_NEWLINE: ^ and $ match at newlines, and . already excludes
                    // the newline in .NET's default mode.
                    options |= System.Text.RegularExpressions.RegexOptions.Multiline;
                }
            }

            try
            {
                return new System.Text.RegularExpressions.Regex(TranslatePosixPattern(pattern), options);
            }
            catch (ArgumentException ex)
            {
                throw new SchemeThrow(
                    Symbol.Intern("regular-expression-syntax"),
                    Pair.List(
                        new MutableString("make-regexp"),
                        new MutableString(ex.Message),
                        false,
                        false));
            }
        });

        interpreter.DefinePrimitive("regexp?", 1, 1, a =>
            a[0] is System.Text.RegularExpressions.Regex);

        interpreter.DefinePrimitive("regexp-exec", 2, 4, a =>
        {
            if (!(a[0] is System.Text.RegularExpressions.Regex regex))
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("regexp-exec"),
                        new MutableString("Not a regexp: ~S"),
                        Pair.List(a[0]),
                        false));
            }

            string text = StringPrimitives.Text(a[1], "regexp-exec");
            int start = a.Length > 2 ? (int)SchemeNumber.ToBigInteger(a[2]) : 0;
            long eflags = a.Length > 3 ? (long)SchemeNumber.ToBigInteger(a[3]) : 0;
            if ((eflags & 2) != 0)
            {
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("regexp-exec"),
                        new MutableString("regexp/noteol is not supported by this port"),
                        false,
                        false));
            }

            System.Text.RegularExpressions.Match match;
            int offset;
            if ((eflags & 1) != 0)
            {
                // regexp/notbol: ^ must not match at the start position. .NET's
                // Match(input, startat) anchors ^ to index 0 only, which is exactly
                // that when start > 0 — the only way ice-9/regex.scm's fold-matches
                // ever passes the flag.
                match = regex.Match(text, start);
                offset = 0;
            }
            else
            {
                // Default POSIX semantics: the search runs over the substring, so ^
                // matches AT the start offset, and every position is shifted back —
                // regex-posix.c's own offset arithmetic.
                match = regex.Match(start == 0 ? text : text.Substring(start));
                offset = start;
            }

            if (!match.Success)
            {
                return false;
            }

            int[] groupNumbers = regex.GetGroupNumbers();
            object[] vector = new object[groupNumbers.Length + 1];
            vector[0] = a[1];
            for (int i = 0; i < groupNumbers.Length; i++)
            {
                System.Text.RegularExpressions.Group group = match.Groups[groupNumbers[i]];
                vector[i + 1] = group.Success
                    ? new Pair((long)(group.Index + offset), (long)(group.Index + group.Length + offset))
                    : new Pair(-1L, -1L);
            }

            return vector;
        });

        // The three accessors are also core-side (LilyPond's output-svg.scm reaches
        // them through the module, but the port's scope is deliberately wider); their
        // bodies are ice-9/regex.scm's, over the same vector, including the
        // unmatched-group answer: #f, never an empty string.
        interpreter.DefinePrimitive("match:start", 1, 2, a =>
        {
            long start = (long)((Pair)MatchSlot(a, "match:start")).Car;
            return start == -1 ? (object)false : start;
        });

        interpreter.DefinePrimitive("match:end", 1, 2, a =>
        {
            long end = (long)((Pair)MatchSlot(a, "match:end")).Cdr;
            return end == -1 ? (object)false : end;
        });

        interpreter.DefinePrimitive("match:substring", 1, 2, a =>
        {
            Pair range = (Pair)MatchSlot(a, "match:substring");
            long start = (long)range.Car;
            long end = (long)range.Cdr;
            if (start == -1 || end == -1)
            {
                return false;
            }

            object[] match = (object[])a[0];
            string text = StringPrimitives.Text(match[0], "match:substring");
            return new MutableString(text.Substring((int)start, (int)(end - start)));
        });
    }

    /// <summary>Reads group n's (start . end) pair out of a match vector.</summary>
    /// <param name="a">The primitive's arguments: the match and an optional group index.</param>
    /// <param name="procedureName">The caller, for the error message.</param>
    /// <returns>The pair.</returns>
    private static object MatchSlot(object[] a, string procedureName)
    {
        object[] match = a[0] as object[];
        int group = a.Length > 1 ? (int)SchemeNumber.ToBigInteger(a[1]) : 0;
        if (match == null || group + 1 >= match.Length || !(match[group + 1] is Pair))
        {
            throw new SchemeThrow(
                Symbol.Intern("wrong-type-arg"),
                Pair.List(
                    new MutableString(procedureName),
                    new MutableString("Not a match structure with group ~S: ~S"),
                    Pair.List((long)group, a[0]),
                    false));
        }

        return match[group + 1];
    }

    /// <summary>
    /// Translates a POSIX extended regular expression onto .NET's dialect.
    /// <para>
    /// Three constructs are translated, all inside bracket expressions: the
    /// <c>[[:class:]]</c> character classes; a <c>]</c> in first position, which POSIX
    /// reads as a literal and .NET as an empty class; and a backslash, which POSIX
    /// reads as a LITERAL inside brackets while .NET reads an escape. The collating
    /// forms <c>[. .]</c> and <c>[= =]</c> are refused. Outside brackets the pattern
    /// is handed to .NET as it stands — ERE's syntax is a subset of .NET's there.
    /// </para>
    /// </summary>
    /// <param name="pattern">The POSIX pattern.</param>
    /// <returns>The translated pattern.</returns>
    private static string TranslatePosixPattern(string pattern)
    {
        StringBuilder result = new StringBuilder(pattern.Length);
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                result.Append(c);
                result.Append(pattern[i + 1]);
                i += 2;
                continue;
            }

            if (c != '[')
            {
                result.Append(c);
                i++;
                continue;
            }

            // A bracket expression. Walk it with POSIX's own rules.
            result.Append('[');
            i++;
            if (i < pattern.Length && pattern[i] == '^')
            {
                result.Append('^');
                i++;
            }

            if (i < pattern.Length && pattern[i] == ']')
            {
                // Literal ] in first position.
                result.Append("\\]");
                i++;
            }

            while (i < pattern.Length && pattern[i] != ']')
            {
                if (pattern[i] == '[' && i + 1 < pattern.Length
                    && (pattern[i + 1] == ':' || pattern[i + 1] == '.' || pattern[i + 1] == '='))
                {
                    char kind = pattern[i + 1];
                    int close = pattern.IndexOf(new string(new[] { kind, ']' }), i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        // Not a closed class form; POSIX reads the [ literally.
                        result.Append("\\[");
                        i++;
                        continue;
                    }

                    string name = pattern.Substring(i + 2, close - i - 2);
                    if (kind != ':')
                    {
                        throw new SchemeThrow(
                            Symbol.Intern("regular-expression-syntax"),
                            Pair.List(
                                new MutableString("make-regexp"),
                                new MutableString("collating forms are not supported by this port: ~S"),
                                Pair.List(new MutableString(name)),
                                false));
                    }

                    result.Append(NamedClassExpansion(name));
                    i = close + 2;
                    continue;
                }

                if (pattern[i] == '\\')
                {
                    // POSIX: a backslash inside a bracket expression is LITERAL.
                    result.Append("\\\\");
                    i++;
                    continue;
                }

                result.Append(pattern[i]);
                i++;
            }

            if (i < pattern.Length)
            {
                result.Append(']');
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Expands a POSIX character-class name into .NET class content. The mappings are
    /// the Unicode readings a UTF-8 glibc locale gives, except <c>digit</c> and
    /// <c>xdigit</c>, which POSIX fixes to ASCII.
    /// </summary>
    /// <param name="name">The class name between <c>[:</c> and <c>:]</c>.</param>
    /// <returns>The .NET class content.</returns>
    private static string NamedClassExpansion(string name)
    {
        switch (name)
        {
            case "alpha": return "\\p{L}";
            case "upper": return "\\p{Lu}";
            case "lower": return "\\p{Ll}";
            case "digit": return "0-9";
            case "xdigit": return "0-9A-Fa-f";
            case "alnum": return "\\p{L}0-9";
            case "space": return "\\s";
            case "blank": return " \\t";
            case "punct": return "\\p{P}\\p{S}";
            case "cntrl": return "\\p{Cc}";
            case "graph": return "\\p{L}\\p{M}\\p{N}\\p{P}\\p{S}";
            case "print": return "\\p{L}\\p{M}\\p{N}\\p{P}\\p{S} ";
            default:
                throw new SchemeThrow(
                    Symbol.Intern("regular-expression-syntax"),
                    Pair.List(
                        new MutableString("make-regexp"),
                        new MutableString("unknown character class: ~S"),
                        Pair.List(new MutableString(name)),
                        false));
        }
    }

    private static RecordType AsRecordType(object value, string procedureName)
    {
        if (value is RecordType type)
        {
            return type;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Not a record type: ~S"),
                Pair.List(value),
                false));
    }

    private static void DefineVectorAccessor(Interpreter interpreter, string name, int index)
        => interpreter.DefinePrimitive(name, 1, 1, a =>
            a[0] is object[] vector && index < vector.Length ? vector[index] : (object)false);

    private static string SystemName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Darwin";
        }

        return "Linux";
    }

    private static IEnumerable<KeyValuePair<string, CharSet>> NamedCharSets()
    {
        yield return new KeyValuePair<string, CharSet>("char-set:letter", CharSet.Letter);
        yield return new KeyValuePair<string, CharSet>("char-set:digit", CharSet.Digit);
        yield return new KeyValuePair<string, CharSet>("char-set:letter+digit", CharSet.LetterOrDigit);
        yield return new KeyValuePair<string, CharSet>("char-set:whitespace", CharSet.Whitespace);
        yield return new KeyValuePair<string, CharSet>("char-set:punctuation", CharSet.Punctuation);
        yield return new KeyValuePair<string, CharSet>("char-set:graphic", CharSet.Graphic);
        yield return new KeyValuePair<string, CharSet>("char-set:printing", CharSet.Printing);
        yield return new KeyValuePair<string, CharSet>("char-set:lower-case", CharSet.LowerCase);
        yield return new KeyValuePair<string, CharSet>("char-set:upper-case", CharSet.UpperCase);
        yield return new KeyValuePair<string, CharSet>("char-set:blank", CharSet.Blank);
        yield return new KeyValuePair<string, CharSet>("char-set:full", CharSet.Full);
        yield return new KeyValuePair<string, CharSet>("char-set:empty", CharSet.Empty);
    }

    private static CharSet Combine(List<char> members, object[] arguments, int baseIndex)
    {
        CharSet built = CharSet.Of(members);
        if (arguments.Length > baseIndex && arguments[baseIndex] is CharSet existing)
        {
            return CharSet.Union(new List<CharSet> { existing, built });
        }

        return built;
    }

    private static List<char> Characters(object[] arguments, int start)
    {
        List<char> characters = new List<char>();
        for (int i = start; i < arguments.Length; i++)
        {
            characters.Add(Character(arguments[i], "char-set"));
        }

        return characters;
    }

    private static char Character(object value, string procedureName)
    {
        if (value is SchemeChar character)
        {
            return (char)character.CodePoint;
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument: ~S"),
                Pair.List(value),
                false));
    }

    private static CharSet AsCharSet(object value, string procedureName)
    {
        if (value is CharSet set)
        {
            return set;
        }

        if (value is SchemeChar character)
        {
            return CharSet.Of(new[] { (char)character.CodePoint });
        }

        if (value is MutableString || value is string)
        {
            return CharSet.Of(StringPrimitives.Text(value, procedureName));
        }

        throw new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Not a character set: ~S"),
                Pair.List(value),
                false));
    }

    private static List<CharSet> AsCharSets(object[] arguments, string procedureName)
    {
        List<CharSet> sets = new List<CharSet>();
        foreach (object argument in arguments)
        {
            sets.Add(AsCharSet(argument, procedureName));
        }

        return sets;
    }

    // SRFI-13 accepts a character, a character set or a predicate wherever it says
    // "char/char-set/pred"; every one of the three shows up in LilyPond's Scheme.
    private static bool Matches(object criterion, char value, Interpreter interpreter)
    {
        switch (criterion)
        {
            case SchemeChar character:
                return character.CodePoint == value;
            case CharSet set:
                return set.Contains(value);
            case Procedure _:
                return Evaluator.IsTrue(
                    interpreter.Evaluator.Apply(criterion, new object[] { SchemeChar.Get(value) }));
            default:
                return false;
        }
    }

    private static string TrimEnds(string text, object[] arguments, Interpreter interpreter, bool left, bool right)
    {
        object criterion = arguments.Length > 1 ? arguments[1] : CharSet.Whitespace;
        int start = 0;
        int end = text.Length;
        if (left)
        {
            while (start < end && Matches(criterion, text[start], interpreter))
            {
                start++;
            }
        }

        if (right)
        {
            while (end > start && Matches(criterion, text[end - 1], interpreter))
            {
                end--;
            }
        }

        return text.Substring(start, end - start);
    }

    private static object FilterString(object[] arguments, Interpreter interpreter, string name, bool keepMatches)
    {
        // SRFI-13 has string-delete and string-filter taking the criterion FIRST, but
        // Guile also accepts the string first for backward compatibility.
        object criterion = arguments[0];
        object subject = arguments[1];
        if (subject is Procedure || subject is CharSet || subject is SchemeChar)
        {
            object swap = criterion;
            criterion = subject;
            subject = swap;
        }

        StringBuilder builder = new StringBuilder();
        foreach (char c in StringPrimitives.Text(subject, name))
        {
            if (Matches(criterion, c, interpreter) == keepMatches)
            {
                builder.Append(c);
            }
        }

        return new MutableString(builder.ToString());
    }

    private static object Pad(object[] arguments, bool onLeft)
    {
        string text = StringPrimitives.Text(arguments[0], "string-pad");
        int width = Count(arguments[1]);
        char filler = arguments.Length > 2 && arguments[2] is SchemeChar character
            ? (char)character.CodePoint
            : ' ';

        if (text.Length >= width)
        {
            // SRFI-13 truncates from the side opposite the padding.
            return new MutableString(onLeft
                ? text.Substring(text.Length - width)
                : text.Substring(0, width));
        }

        string padding = new string(filler, width - text.Length);
        return new MutableString(onLeft ? padding + text : text + padding);
    }

    private static int Count(object value) => (int)ToLong(value);

    private static long ToLong(object value)
    {
        switch (value)
        {
            case long number:
                return number;
            case int number:
                return number;
            case double number:
                return (long)number;
            default:
                return (long)SchemeNumber.ToDouble(value);
        }
    }

}

/// <summary>
/// Guile's directory stream — the object <c>opendir</c> answers and <c>readdir</c> walks.
/// <para>
/// The whole listing is taken at <c>opendir</c> time and handed out one entry at a time.
/// That is not what the C library does, and the difference is deliberate: a caller that
/// DELETES entries while walking (which is exactly what a clean-up loop does) would
/// otherwise be mutating the thing it is iterating, and .NET's enumerator would object
/// where C's <c>readdir</c> merely leaves the result unspecified.
/// </para>
/// <para>
/// <c>.</c> and <c>..</c> lead the listing, because Guile yields them and callers written
/// against Guile filter them by hand.
/// </para>
/// </summary>
public sealed class DirectoryStream
{
    private readonly List<string> _entries = new List<string>();
    private int _position;
    private bool _closed;

    /// <summary>Opens a stream over one directory's entries.</summary>
    /// <param name="path">The directory to list.</param>
    public DirectoryStream(string path)
    {
        Path = path;
        _entries.Add(".");
        _entries.Add("..");
        foreach (string entry in System.IO.Directory.GetFileSystemEntries(path))
        {
            _entries.Add(System.IO.Path.GetFileName(entry));
        }
    }

    /// <summary>Gets the directory the stream was opened on.</summary>
    public string Path { get; }

    /// <summary>Returns the next entry name, or <see langword="null"/> when exhausted.</summary>
    /// <returns>The entry name.</returns>
    public string Next()
        => _closed || _position >= _entries.Count ? null : _entries[_position++];

    /// <summary>Closes the stream; every later read answers end-of-file.</summary>
    public void Close() => _closed = true;

    /// <inheritdoc/>
    public override string ToString() => "#<directory-stream " + Path + ">";
}
