// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, or (at your option) any later version.

using System.IO;
using CodeBrix.LilyScheme.Caching;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Fences for the expansion cache: a recorded boot must replay into a fresh
/// interpreter with the SAME bindings — including Scheme-defined MACROS, which mode-e
/// expansion installs only as an expansion-time side effect and which the c&amp;e
/// recording therefore has to carry explicitly (the ly-syntax-constructors regression,
/// 2026-08-12). Identity inside the recorded graph is load-bearing too: gensym lookup
/// is reference equality, so the serializer preserves object identity, and two
/// deserializations must share NOTHING, because recorded constants become live
/// mutable data per interpreter.
/// </summary>
public class ExpansionCacheTests
{
    private const string FenceSource =
        "(define fence-value (* 6 7))"
        + "(define-syntax-rule (fence-twice x) (+ x x))"
        + "(define fence-used (fence-twice 10))";

    private static Interpreter NewCoreInterpreter()
    {
        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);
        return interpreter;
    }

    private static object Eval(Interpreter interpreter, string source)
    {
        object result = null;
        foreach (object form in SchemeReader.ReadAll(source, "<fence>"))
        {
            result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
        }

        return result;
    }

    private static ExpansionCache RecordFence(out string sourceHash)
    {
        ExpansionCache recorder = new ExpansionCache();
        Interpreter recording = NewCoreInterpreter();
        recording.ExpansionCache = recorder;
        SchemeBootstrap.LoadExpanded(recording, FenceSource, "fence.scm");
        sourceHash = ExpansionCache.HashSource(FenceSource);
        return recorder;
    }

    [Fact]
    public void a_replayed_recording_rebuilds_values_and_macros()
    {
        //Arrange
        object value = null;
        object usedMacro = null;
        object liveUseAfterReplay = null;
        bool controlHadMacro = true;
        Interpreter.RunWithLargeStack(() =>
        {
            ExpansionCache recorder = RecordFence(out _);
            using MemoryStream stream = new MemoryStream();
            ExpansionCacheFile.Write(recorder, stream, "fence-key");
            stream.Position = 0;
            ExpansionCache replayed = ExpansionCacheFile.Read(stream, "fence-key");

            // CONTROL: a fresh interpreter does NOT know the macro on its own — the
            // assertion below must pass because of the replay, not ambient state.
            Interpreter control = NewCoreInterpreter();
            try
            {
                Eval(control, "(fence-twice 1)");
            }
            catch (SchemeThrow)
            {
                controlHadMacro = false;
            }

            //Act
            Interpreter fresh = NewCoreInterpreter();
            fresh.ExpansionCache = replayed;
            SchemeBootstrap.LoadExpanded(fresh, FenceSource, "fence.scm");
            value = Eval(fresh, "fence-value");
            usedMacro = Eval(fresh, "fence-used");

            // The regression fence: the recorded MACRO must survive replay and expand
            // a NEW, live form. 21 + 21 is hand-computed from the rule's template.
            liveUseAfterReplay = Eval(fresh, "(fence-twice 21)");
        });

        //Assert
        controlHadMacro.Should().BeFalse();
        value.Should().Be(42L);
        usedMacro.Should().Be(20L);
        liveUseAfterReplay.Should().Be(42L);
    }

    [Fact]
    public void a_replayed_file_is_not_expanded_again()
    {
        //Arrange
        long expansionsDuringReplay = -1;
        long expansionsWithoutCache = -1;
        bool hit = false;
        Interpreter.RunWithLargeStack(() =>
        {
            ExpansionCache recorder = RecordFence(out string sourceHash);
            using MemoryStream stream = new MemoryStream();
            ExpansionCacheFile.Write(recorder, stream, "fence-key");
            stream.Position = 0;
            ExpansionCache replayed = ExpansionCacheFile.Read(stream, "fence-key");
            hit = replayed.TryGetFile("fence.scm", sourceHash, out _);

            // CONTROL: the SAME source loaded with no cache at all. Zero is only
            // evidence that the replay skipped expansion if the identical load
            // WITHOUT the cache expands something.
            Interpreter uncached = NewCoreInterpreter();
            LoadDiagnostics.Reset();
            SchemeBootstrap.LoadExpanded(uncached, FenceSource, "fence.scm");
            expansionsWithoutCache = LoadDiagnostics.ExpandedFormCountOnThisThread;

            Interpreter fresh = NewCoreInterpreter();
            fresh.ExpansionCache = replayed;

            //Act
            // The PER-THREAD count, not the process-wide one: test classes run in
            // parallel, and a sibling class expanding Scheme between the Reset and
            // the read added its spans to the process-wide counter -- which made this
            // fence fail about one run in many, on nothing to do with the cache.
            LoadDiagnostics.Reset();
            SchemeBootstrap.LoadExpanded(fresh, FenceSource, "fence.scm");
            expansionsDuringReplay = LoadDiagnostics.ExpandedFormCountOnThisThread;
        });

        //Assert
        hit.Should().BeTrue();
        expansionsWithoutCache.Should().BeGreaterThan(0L);
        expansionsDuringReplay.Should().Be(0L);
    }

    [Fact]
    public void a_changed_source_is_a_miss_and_an_unchanged_source_is_a_hit()
    {
        //Arrange
        bool sameSourceHits = false;
        bool changedSourceMisses = true;
        Interpreter.RunWithLargeStack(() =>
        {
            ExpansionCache recorder = RecordFence(out string sourceHash);

            //Act
            sameSourceHits = recorder.TryGetFile("fence.scm", sourceHash, out _);
            changedSourceMisses = !recorder.TryGetFile(
                "fence.scm", ExpansionCache.HashSource(FenceSource + " "), out _);
        });

        //Assert
        sameSourceHits.Should().BeTrue();
        changedSourceMisses.Should().BeTrue();
    }

    [Fact]
    public void a_wrong_key_or_corrupt_file_answers_null_never_a_partial_cache()
    {
        //Arrange
        ExpansionCache recorder = null;
        Interpreter.RunWithLargeStack(() => recorder = RecordFence(out _));
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".lsxc");
        ExpansionCache rightKey;
        ExpansionCache wrongKey;
        ExpansionCache corrupt;
        try
        {
            ExpansionCacheFile.WriteFile(recorder, path, "fence-key");

            //Act
            rightKey = ExpansionCacheFile.TryReadFile(path, "fence-key");
            wrongKey = ExpansionCacheFile.TryReadFile(path, "other-key");

            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);
            corrupt = ExpansionCacheFile.TryReadFile(path, "fence-key");
        }
        finally
        {
            File.Delete(path);
        }

        //Assert
        (rightKey != null).Should().BeTrue();
        rightKey.IsReplay.Should().BeTrue();
        (wrongKey == null).Should().BeTrue();
        (corrupt == null).Should().BeTrue();
    }

    [Fact]
    public void identity_is_preserved_within_a_graph_and_by_name_never()
    {
        //Arrange
        Symbol gensym = Symbol.Generate(" fence");
        Symbol sameNamedTwin = Symbol.CreateUninterned(gensym.Name);
        MutableString shared = new MutableString("shared");

        // One gensym referenced twice, a same-named-but-distinct twin, a shared
        // string referenced twice, and a cycle back to the head pair.
        Pair head = new Pair(gensym, new Pair(gensym, new Pair(sameNamedTwin, new Pair(shared, new Pair(shared, Nil.Instance)))));
        ((Pair)((Pair)((Pair)((Pair)head.Cdr).Cdr).Cdr).Cdr).Cdr = head;

        ExpansionCache recorder = new ExpansionCache();
        recorder.RecordFile("graph.scm", ExpansionCache.HashSource("x"), new object[] { head });

        //Act
        using MemoryStream stream = new MemoryStream();
        ExpansionCacheFile.Write(recorder, stream, "k");
        stream.Position = 0;
        ExpansionCache loaded = ExpansionCacheFile.Read(stream, "k");
        loaded.TryGetFile("graph.scm", ExpansionCache.HashSource("x"), out var forms).Should().BeTrue();
        Pair round = (Pair)forms[0];
        Symbol first = (Symbol)round.Car;
        Symbol second = (Symbol)((Pair)round.Cdr).Car;
        Symbol twin = (Symbol)((Pair)((Pair)round.Cdr).Cdr).Car;
        object sharedOne = ((Pair)((Pair)((Pair)round.Cdr).Cdr).Cdr).Car;
        Pair last = (Pair)((Pair)((Pair)((Pair)round.Cdr).Cdr).Cdr).Cdr;

        //Assert
        ReferenceEquals(first, second).Should().BeTrue();
        first.IsUninterned.Should().BeTrue();
        first.Name.Should().Be(gensym.Name);
        // Same NAME, distinct object at record time — must stay distinct: gensym
        // identity is reference equality, never the name.
        ReferenceEquals(first, twin).Should().BeFalse();
        twin.Name.Should().Be(first.Name);
        ReferenceEquals(sharedOne, last.Car).Should().BeTrue();
        ReferenceEquals(last.Cdr, round).Should().BeTrue();
    }

    [Fact]
    public void two_deserializations_share_no_objects()
    {
        //Arrange
        ExpansionCache recorder = new ExpansionCache();
        string hash = ExpansionCache.HashSource("x");
        recorder.RecordFile(
            "graph.scm", hash, new object[] { new Pair(new MutableString("mutable"), Nil.Instance) });
        using MemoryStream stream = new MemoryStream();
        ExpansionCacheFile.Write(recorder, stream, "k");

        //Act — recorded constants become live mutable data, so every interpreter
        // must get its own graph; sharing one would leak mutations across boots.
        stream.Position = 0;
        ExpansionCache first = ExpansionCacheFile.Read(stream, "k");
        stream.Position = 0;
        ExpansionCache second = ExpansionCacheFile.Read(stream, "k");
        first.TryGetFile("graph.scm", hash, out var formsA).Should().BeTrue();
        second.TryGetFile("graph.scm", hash, out var formsB).Should().BeTrue();

        //Assert
        ReferenceEquals(formsA[0], formsB[0]).Should().BeFalse();
        ReferenceEquals(((Pair)formsA[0]).Car, ((Pair)formsB[0]).Car).Should().BeFalse();
    }
}
