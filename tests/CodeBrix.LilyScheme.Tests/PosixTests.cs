// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// The POSIX surface: <c>system</c> / <c>system*</c> with the wait-status decoders,
/// <c>stat</c> with Guile's own accessors (vendored <c>ice-9/posix.scm</c>), and the
/// broken-down-time family.
/// <para>
/// The time expectations are hand-computed from <c>struct tm</c>'s documented
/// encoding — epoch 0 is Thursday 1970-01-01, so <c>tm:wday</c> is 4, <c>tm:mon</c>
/// is 0-based 0, <c>tm:year</c> counts from 1900 — the exact off-by-one conventions
/// an unfaithful vector would miss.
/// </para>
/// </summary>
public class PosixTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void system_reports_the_exit_code_through_the_wait_status_encoding()
    {
        //Arrange / Act
        // "exit 7" is a command both /bin/sh and cmd.exe accept, and 7 travels
        // through the <<8 encoding status:exit-val undoes. Raw 0 is the control:
        // an implementation returning the exit code UNENCODED reads 7 where the
        // encoding demands 1792.
        string result = Value(
            "(let ((s (system \"exit 7\")))"
            + "  (list (status:exit-val s) (= s 1792) (status:term-sig s)))");

        //Assert
        result.Should().Be("(7 #t #f)");
    }

    [Fact]
    public void system_star_runs_the_program_directly()
    {
        //Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("system* fence uses a POSIX shell");
        }

        //Act
        // The argument contains a shell metacharacter; system* must NOT interpret it
        // (no shell, no word splitting), so the child sees one literal argument.
        string result = Value("(status:exit-val (system* \"/bin/sh\" \"-c\" \"exit 3\"))");

        //Assert
        result.Should().Be("3");
    }

    [Fact]
    public void stat_answers_the_size_and_type_of_a_real_file()
    {
        //Arrange
        string path = Path.Combine(
            Path.GetTempPath(), "lilyscheme-stat-" + Path.GetRandomFileName());
        File.WriteAllText(path, "12345");
        try
        {
            //Act
            // Size hand-known (five bytes written), type from filesys.c's symbol set;
            // the directory beside it is the control that type is not constant.
            // TrimEnd takes BOTH separators: on Windows GetTempPath ends in a backslash,
            // which '/' alone leaves in place -- and stat of a path with a trailing
            // separator is a different question from stat of the directory itself.
            string temporaryDirectory = Path.GetTempPath()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string result = Value(
                $"(let ((s (stat {Printer.WriteString(path)})))"
                + "  (list (stat:size s) (stat:type s)"
                + $"        (stat:type (stat {Printer.WriteString(temporaryDirectory)}))))");

            //Assert
            result.Should().Be("(5 regular directory)");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void stat_with_false_answers_false_for_a_missing_path()
    {
        //Arrange / Act
        // scm_stat's own contract: the second argument #f converts the system-error
        // throw into a #f answer. The throwing default is the control.
        string result = Value(
            "(list (stat \"/lilyscheme-does-not-exist\" #f)"
            + "      (catch #t (lambda () (stat \"/lilyscheme-does-not-exist\"))"
            + "             (lambda args 'threw)))");

        //Assert
        result.Should().Be("(#f threw)");
    }

    [Fact]
    public void gmtime_zero_is_thursday_the_first_of_january_1970()
    {
        //Arrange / Act
        // Every off-by-one in struct tm at once: mon 0-based, year from 1900, wday 4
        // (a Thursday), yday 0-based, gmtoff 0.
        string result = Value(
            "(let ((tm (gmtime 0)))"
            + "  (list (tm:sec tm) (tm:min tm) (tm:hour tm) (tm:mday tm) (tm:mon tm)"
            + "        (tm:year tm) (tm:wday tm) (tm:yday tm) (tm:gmtoff tm)))");

        //Assert
        result.Should().Be("(0 0 0 1 0 70 4 0 0)");
    }

    [Fact]
    public void strftime_formats_the_common_directives()
    {
        //Arrange / Act
        // 86399 is 23:59:59 of day one; %j is 1-based and 3 digits; %a/%b are the
        // fixed C-locale abbreviations; %% is a literal.
        string result = Value(
            "(strftime \"%Y-%m-%d %H:%M:%S %a %b %j 100%%\" (gmtime 86399))");

        //Assert
        result.Should().Be("\"1970-01-01 23:59:59 Thu Jan 001 100%\"");
    }

    [Fact]
    public void strftime_seconds_since_epoch_round_trips()
    {
        //Arrange / Act
        // %s must reconstruct the epoch second from the broken-down fields plus
        // tm:gmtoff; gmtime makes the offset zero, so the number is exact.
        string result = Value("(strftime \"%s\" (gmtime 1234567))");

        //Assert
        result.Should().Be("\"1234567\"");
    }

    [Fact]
    public void localtime_and_gmtime_agree_through_the_offset()
    {
        //Arrange / Act
        // tm:gmtoff is seconds WEST of UTC (Guile's documented sign), so local
        // clock time re-encoded with the offset added lands back on the epoch
        // second — true in every time zone, so the fence is not machine-specific.
        string result = Value(
            "(let ((t 86400000))"
            + "  (= t (string->number (strftime \"%s\" (localtime t)))))");

        //Assert
        result.Should().Be("#t");
    }
}
