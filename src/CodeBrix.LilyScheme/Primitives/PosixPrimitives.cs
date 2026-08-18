// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyScheme.Primitives;

/// <summary>
/// The POSIX-flavored surface a general Guile program reaches for: subprocesses
/// (<c>system</c>, <c>system*</c> and the wait-status decoders), <c>stat</c> /
/// <c>lstat</c>, and the broken-down-time family (<c>localtime</c>, <c>gmtime</c>,
/// <c>strftime</c>).
/// <para>
/// The <c>stat:</c> and <c>tm:</c> accessors are NOT here — they are Guile's own,
/// loaded verbatim from the vendored <c>ice-9/posix.scm</c> by the prelude, reading
/// the vectors these primitives build. The vector layouts are therefore
/// <c>libguile/filesys.c</c>'s (18 slots) and <c>libguile/stime.c</c>'s (11 slots),
/// exactly.
/// </para>
/// </summary>
public static class PosixPrimitives
{
    /// <summary>Installs the POSIX primitives.</summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallSubprocesses(interpreter);
        InstallStat(interpreter);
        InstallBrokenDownTime(interpreter);
    }

    /// <summary>
    /// Encodes a child's exit code the way <c>waitpid</c> reports a normal exit, which
    /// is what Guile's <c>system</c> family answers and <c>status:exit-val</c> decodes.
    /// <para>
    /// DIVERGENCE, recorded: .NET reports a signal-killed child as exit code 128+signal
    /// (the shell convention) rather than through a separate WIFSIGNALED channel, so a
    /// child killed by SIGKILL reads here as exit value 137 — the same thing a shell's
    /// <c>$?</c> shows — and <c>status:term-sig</c> answers <see langword="false"/>.
    /// </para>
    /// </summary>
    /// <param name="exitCode">The child's exit code.</param>
    /// <returns>The encoded wait status.</returns>
    private static long EncodeWaitStatus(int exitCode) => ((long)(exitCode & 0xff)) << 8;

    private static void InstallSubprocesses(Interpreter interpreter)
    {
        // (system) answers whether a shell is available; (system "cmd") runs cmd
        // through it and returns the encoded wait status, child output inherited.
        interpreter.DefinePrimitive("system", 0, 1, a =>
        {
            if (a.Length == 0)
            {
                return File.Exists(ShellPath());
            }

            string command = StringPrimitives.Text(a[0], "system");
            using (Process process = StartProcess(ShellPath(), new[] { ShellCommandFlag(), command }, false, false))
            {
                process.WaitForExit();
                return EncodeWaitStatus(process.ExitCode);
            }
        });

        // (system* prog arg ...) runs prog DIRECTLY — no shell, no word splitting —
        // and returns the encoded wait status, as libguile/simpos.c documents.
        interpreter.DefinePrimitive("system*", 1, -1, a =>
        {
            string program = StringPrimitives.Text(a[0], "system*");
            string[] arguments = new string[a.Length - 1];
            for (int i = 1; i < a.Length; i++)
            {
                arguments[i - 1] = StringPrimitives.Text(a[i], "system*");
            }

            using (Process process = StartProcess(program, arguments, false, false))
            {
                process.WaitForExit();
                return EncodeWaitStatus(process.ExitCode);
            }
        });

        // The three decoders read the classic wait-status encoding. Under the .NET
        // limitation recorded on EncodeWaitStatus, every child status decodes through
        // status:exit-val and the other two answer #f.
        interpreter.DefinePrimitive("status:exit-val", 1, 1, a =>
        {
            long status = (long)SchemeNumber.ToBigInteger(a[0]);
            return (status & 0x7f) == 0 ? (object)((status >> 8) & 0xff) : false;
        });

        interpreter.DefinePrimitive("status:term-sig", 1, 1, a =>
        {
            long status = (long)SchemeNumber.ToBigInteger(a[0]);
            long signal = status & 0x7f;
            return signal != 0 && signal != 0x7f ? (object)signal : false;
        });

        interpreter.DefinePrimitive("status:stop-sig", 1, 1, a =>
        {
            long status = (long)SchemeNumber.ToBigInteger(a[0]);
            return (status & 0xff) == 0x7f ? (object)((status >> 8) & 0xff) : false;
        });
    }

    /// <summary>
    /// Starts a process with the standard streams inherited (the <c>system</c> shape)
    /// or selectively redirected (the pipe shapes the <c>(ice-9 popen)</c> shim
    /// builds — a read pipe captures the child's output and leaves its input alone,
    /// and a write pipe the reverse, exactly as Guile's popen wires its child).
    /// </summary>
    /// <param name="program">The program to run.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="redirectInput">Whether to capture the child's standard input.</param>
    /// <param name="redirectOutput">Whether to capture the child's standard output.</param>
    /// <returns>The started process.</returns>
    internal static Process StartProcess(string program, string[] arguments, bool redirectInput, bool redirectOutput)
    {
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(info);
        }
        catch (Exception ex)
        {
            throw new SchemeThrow(
                Symbol.Intern("system-error"),
                Pair.List(
                    new MutableString(program),
                    new MutableString("~A"),
                    Pair.List(new MutableString(ex.Message)),
                    false));
        }
    }

    /// <summary>Answers the shell used by <c>system</c> and <c>open-pipe</c>.</summary>
    /// <returns>The shell executable path.</returns>
    internal static string ShellPath()
        => OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";

    /// <summary>Answers the shell's run-one-command flag.</summary>
    /// <returns>The flag string.</returns>
    internal static string ShellCommandFlag() => OperatingSystem.IsWindows() ? "/c" : "-c";

    private static void InstallStat(Interpreter interpreter)
    {
        // (stat path [exception]) — #f as the second argument answers #f for a missing
        // path instead of throwing, which is scm_stat's own contract.
        interpreter.DefinePrimitive("stat", 1, 2, a =>
            StatVector(StringPrimitives.Text(a[0], "stat"), true, !(a.Length > 1 && a[1] is bool b && !b), "stat"));

        interpreter.DefinePrimitive("lstat", 1, 1, a =>
            StatVector(StringPrimitives.Text(a[0], "lstat"), false, true, "lstat"));
    }

    /// <summary>
    /// Builds Guile's 18-slot stat vector (<c>libguile/filesys.c</c>'s layout), read by
    /// the vendored <c>posix.scm</c> accessors.
    /// <para>
    /// The slots .NET cannot answer truthfully — dev, ino, nlink, uid, gid, rdev,
    /// blksize and blocks — hold <see langword="false"/> DELIBERATELY, a visible
    /// non-answer rather than a plausible zero. On Windows, mode and perms are
    /// <see langword="false"/> too, for the same reason.
    /// </para>
    /// </summary>
    private static object StatVector(string path, bool follow, bool throwOnMissing, string procedureName)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? (FileSystemInfo)new DirectoryInfo(path)
            : new FileInfo(path);
        bool isLink = info.LinkTarget != null;
        if (!info.Exists && !isLink)
        {
            if (!throwOnMissing)
            {
                return false;
            }

            throw new SchemeThrow(
                Symbol.Intern("system-error"),
                Pair.List(
                    new MutableString(procedureName),
                    new MutableString("~A: ~S"),
                    Pair.List(new MutableString("No such file or directory"), new MutableString(path)),
                    false));
        }

        if (follow && isLink)
        {
            FileSystemInfo target = info.ResolveLinkTarget(true);
            if (target != null && target.Exists)
            {
                info = target;
                isLink = false;
            }
        }

        object[] vector = new object[18];
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = false;
        }

        bool isDirectory = info is DirectoryInfo && info.Exists;
        long size = info is FileInfo file && file.Exists ? file.Length : 0L;
        long typeBits = isLink ? 0xA000L : isDirectory ? 0x4000L : 0x8000L;
        vector[7] = size;
        vector[8] = new DateTimeOffset(info.LastAccessTimeUtc).ToUnixTimeSeconds();
        vector[9] = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
        vector[10] = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
        vector[13] = Symbol.Intern(isLink ? "symlink" : isDirectory ? "directory" : "regular");
        vector[15] = NanosecondsWithinSecond(info.LastAccessTimeUtc);
        vector[16] = NanosecondsWithinSecond(info.LastWriteTimeUtc);
        vector[17] = NanosecondsWithinSecond(info.LastWriteTimeUtc);

        if (!OperatingSystem.IsWindows())
        {
            long perms = (long)(isLink && !follow
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute
                : File.GetUnixFileMode(path));
            vector[2] = typeBits | perms;
            vector[14] = perms;
        }

        return vector;
    }

    private static long NanosecondsWithinSecond(DateTime value)
        => (value.Ticks % TimeSpan.TicksPerSecond) * 100;

    private static void InstallBrokenDownTime(Interpreter interpreter)
    {
        // Both answer libguile/stime.c's 11-slot tm vector, read by the vendored
        // posix.scm tm: accessors. Per struct tm: mon is 0-based, year counts from
        // 1900, and — per Guile's documented tm:gmtoff — the offset is seconds WEST
        // of UTC. localtime's optional TZ argument is refused loudly: mapping POSIX
        // TZ strings onto .NET time zones would be a guess.
        interpreter.DefinePrimitive("localtime", 1, 2, a =>
        {
            if (a.Length > 1)
            {
                throw new SchemeThrow(
                    Symbol.Intern("misc-error"),
                    Pair.List(
                        new MutableString("localtime"),
                        new MutableString("the zone argument is not supported by this port"),
                        false,
                        false));
            }

            long seconds = (long)SchemeNumber.ToBigInteger(a[0]);
            DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            TimeZoneInfo zone = TimeZoneInfo.Local;
            DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, zone);
            bool isDst = zone.IsDaylightSavingTime(local);
            return TmVector(local, -(long)local.Offset.TotalSeconds, isDst ? 1 : 0, LocalZoneName(local, zone));
        });

        interpreter.DefinePrimitive("gmtime", 1, 1, a =>
        {
            long seconds = (long)SchemeNumber.ToBigInteger(a[0]);
            DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return TmVector(utc, 0, 0, "GMT");
        });

        interpreter.DefinePrimitive("strftime", 2, 2, a =>
        {
            string format = StringPrimitives.Text(a[0], "strftime");
            object[] tm = a[1] as object[];
            if (tm == null || tm.Length < 11)
            {
                throw new SchemeThrow(
                    Symbol.Intern("wrong-type-arg"),
                    Pair.List(
                        new MutableString("strftime"),
                        new MutableString("Not a broken-down time vector: ~S"),
                        Pair.List(a[1]),
                        false));
            }

            return new MutableString(Strftime(format, tm));
        });
    }

    private static object[] TmVector(DateTimeOffset moment, long secondsWestOfUtc, int isDst, string zoneName)
    {
        return new object[]
        {
            (long)moment.Second,
            (long)moment.Minute,
            (long)moment.Hour,
            (long)moment.Day,
            (long)(moment.Month - 1),
            (long)(moment.Year - 1900),
            (long)(int)moment.DayOfWeek,
            (long)(moment.DayOfYear - 1),
            (long)isDst,
            secondsWestOfUtc,
            zoneName == null ? (object)false : new MutableString(zoneName),
        };
    }

    private static string LocalZoneName(DateTimeOffset local, TimeZoneInfo zone)
        => zone.IsDaylightSavingTime(local) ? zone.DaylightName : zone.StandardName;

    private static long TmSlot(object[] tm, int index)
        => (long)SchemeNumber.ToBigInteger(tm[index]);

    /// <summary>
    /// Formats a tm vector per C <c>strftime</c>. The common directives are
    /// implemented; an unrecognised conversion is copied through verbatim, which is
    /// what glibc does with one.
    /// </summary>
    private static string Strftime(string format, object[] tm)
    {
        long sec = TmSlot(tm, 0);
        long min = TmSlot(tm, 1);
        long hour = TmSlot(tm, 2);
        long mday = TmSlot(tm, 3);
        long mon = TmSlot(tm, 4);
        long year = TmSlot(tm, 5) + 1900;
        long wday = TmSlot(tm, 6);
        long yday = TmSlot(tm, 7);
        string[] dayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        string[] monthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };

        StringBuilder result = new StringBuilder();
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                result.Append(format[i]);
                continue;
            }

            i++;
            switch (format[i])
            {
                case 'a': result.Append(dayNames[wday % 7].Substring(0, 3)); break;
                case 'A': result.Append(dayNames[wday % 7]); break;
                case 'b':
                case 'h': result.Append(monthNames[mon % 12].Substring(0, 3)); break;
                case 'B': result.Append(monthNames[mon % 12]); break;
                case 'c':
                    result.Append(Strftime("%a %b %e %H:%M:%S %Y", tm));
                    break;
                case 'C': result.Append((year / 100).ToString("00")); break;
                case 'd': result.Append(mday.ToString("00")); break;
                case 'D': result.Append(Strftime("%m/%d/%y", tm)); break;
                case 'e': result.Append(mday.ToString().PadLeft(2)); break;
                case 'F': result.Append(Strftime("%Y-%m-%d", tm)); break;
                case 'H': result.Append(hour.ToString("00")); break;
                case 'I': result.Append((hour % 12 == 0 ? 12 : hour % 12).ToString("00")); break;
                case 'j': result.Append((yday + 1).ToString("000")); break;
                case 'k': result.Append(hour.ToString().PadLeft(2)); break;
                case 'l': result.Append((hour % 12 == 0 ? 12 : hour % 12).ToString().PadLeft(2)); break;
                case 'm': result.Append((mon + 1).ToString("00")); break;
                case 'M': result.Append(min.ToString("00")); break;
                case 'n': result.Append('\n'); break;
                case 'p': result.Append(hour < 12 ? "AM" : "PM"); break;
                case 'r': result.Append(Strftime("%I:%M:%S %p", tm)); break;
                case 'R': result.Append(Strftime("%H:%M", tm)); break;
                case 'S': result.Append(sec.ToString("00")); break;
                case 's':
                    {
                        DateTimeOffset utcBase = new DateTimeOffset(
                            (int)year, (int)mon + 1, (int)mday, (int)hour, (int)min, (int)Math.Min(sec, 59), TimeSpan.Zero);
                        result.Append(utcBase.ToUnixTimeSeconds() + TmSlot(tm, 9));
                        break;
                    }

                case 't': result.Append('\t'); break;
                case 'T': result.Append(Strftime("%H:%M:%S", tm)); break;
                case 'u': result.Append(wday == 0 ? 7 : wday); break;
                case 'w': result.Append(wday); break;
                case 'y': result.Append((year % 100).ToString("00")); break;
                case 'Y': result.Append(year); break;
                case 'z':
                    {
                        long west = TmSlot(tm, 9);
                        long east = -west;
                        result.Append(east < 0 ? '-' : '+');
                        long magnitude = Math.Abs(east);
                        result.Append((magnitude / 3600).ToString("00"));
                        result.Append((magnitude % 3600 / 60).ToString("00"));
                        break;
                    }

                case 'Z':
                    if (tm[10] is MutableString zoneText)
                    {
                        result.Append(zoneText);
                    }

                    break;
                case '%': result.Append('%'); break;
                default:
                    result.Append('%');
                    result.Append(format[i]);
                    break;
            }
        }

        return result.ToString();
    }
}
