// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Text;

namespace CodeBrix.LilyScheme.Runtime;

/// <summary>
/// Filesystem reads that keep POSIX sharing behaviour on every platform.
/// <para>
/// Windows ENFORCES share modes and POSIX does not. <c>File.ReadAllBytes</c> and
/// <c>File.ReadAllText</c> ask for <see cref="FileShare.Read"/>, which refuses a file
/// that anything else currently holds open for WRITING -- so reading a file back while
/// its output port is still open throws <see cref="IOException"/> on Windows and
/// succeeds everywhere else.
/// </para>
/// <para>
/// Scheme code is entitled to do exactly that. The ports handed out here are buffered
/// and Scheme is not required to close them -- see PORTS ARE FLUSHED BY WHOEVER OWNS
/// THE RUN in AGENT-README.txt -- and Guile on a POSIX host reads such a file without
/// complaint, seeing whatever has been flushed so far. Refusing it on one platform
/// makes the SAME Scheme program throw there and not elsewhere.
/// </para>
/// <para>
/// Asking for <see cref="FileShare.ReadWrite"/> is what the Scheme layer above already
/// assumes. It changes nothing on Linux or macOS, where the share mode was never
/// consulted in the first place.
/// </para>
/// </summary>
internal static class HostFile
{
    /// <summary>Reads every byte of a file, tolerating a concurrent writer.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The bytes on disk at the time of the read.</returns>
    internal static byte[] ReadAllBytes(string path)
    {
        // Copied through a MemoryStream rather than sized from stream.Length: a writer
        // still appending means the length is a moving target, and a short read against
        // a preallocated buffer would silently answer trailing zero bytes.
        using (FileStream stream = Open(path))
        using (MemoryStream buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>Reads a file as text, tolerating a concurrent writer.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The decoded text.</returns>
    internal static string ReadAllText(string path)
    {
        // UTF-8 with byte-order-mark detection is File.ReadAllText's own contract, kept
        // here so that swapping the share mode is the ONLY behaviour that changes.
        using (FileStream stream = Open(path))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            return reader.ReadToEnd();
        }
    }

    private static FileStream Open(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}
