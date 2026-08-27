using System.Text;

namespace Filekin.Infrastructure.Windows.Inspection;

/// <summary>What a cheap look at the start of a file said about it.</summary>
/// <param name="EncodingName">A readable encoding name for the Info sheet.</param>
/// <param name="Preamble">How many bytes of byte-order mark to skip when reading the whole file.</param>
public sealed record TextFileProbe(string EncodingName, int Preamble);

/// <summary>
/// Decides whether a file is text and, when it is, counts its lines on demand.
///
/// The encoding row is free: the same first block that decides "is this text?" also carries the
/// byte-order mark, so it is shown immediately. The line count is not free — it reads every byte —
/// so it stays behind an explicit action, like the hash (DECISIONS.md, 2026-08-27).
/// </summary>
public static class TextFileReader
{
    private const int SniffBytes = 8192;

    /// <summary>
    /// Looks at the first block of <paramref name="path"/>. Returns <c>null</c> when the file looks
    /// binary — the classic test: real text does not contain a NUL byte.
    /// </summary>
    public static TextFileProbe? Sniff(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            Span<byte> buffer = stackalloc byte[SniffBytes];
            var read = stream.ReadAtLeast(buffer, SniffBytes, throwOnEndOfStream: false);
            var block = buffer[..read];

            if (StartsWith(block, [0xEF, 0xBB, 0xBF]))
            {
                return new TextFileProbe("UTF-8 with BOM", 3);
            }

            if (StartsWith(block, [0xFF, 0xFE, 0x00, 0x00]))
            {
                return new TextFileProbe("UTF-32 LE", 4);
            }

            if (StartsWith(block, [0xFF, 0xFE]))
            {
                return new TextFileProbe("UTF-16 LE", 2);
            }

            if (StartsWith(block, [0xFE, 0xFF]))
            {
                return new TextFileProbe("UTF-16 BE", 2);
            }

            if (block.Contains<byte>(0))
            {
                return null;
            }

            // No BOM and no NUL. Valid UTF-8 byte sequences are the common case; anything else is
            // reported as unknown 8-bit rather than guessed at a specific code page.
            //
            // Whether the block is the whole file matters: a multi-byte sequence cut in half by the
            // 8 KB boundary proves nothing, but the same half sequence at a real end-of-file is a
            // genuinely broken encoding.
            var isWholeFile = read < SniffBytes;
            return IsValidUtf8(block, isWholeFile)
                ? new TextFileProbe("UTF-8", 0)
                : new TextFileProbe("8-bit text", 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Counts the lines in <paramref name="path"/>. A trailing newline does not add an empty final
    /// line, so a three-line file that ends with a newline reports three.
    /// </summary>
    public static async Task<int> CountLinesAsync(
        string path,
        TextFileProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(probe);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        using var reader = new StreamReader(stream, DecodeAs(probe), detectEncodingFromByteOrderMarks: true);

        var lines = 0;
        var sawContent = false;
        var buffer = new char[64 * 1024];
        var endedWithNewLine = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            sawContent = true;
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] == '\n')
                {
                    lines++;
                }
            }

            endedWithNewLine = buffer[read - 1] == '\n';
        }

        if (!sawContent)
        {
            return 0;
        }

        return endedWithNewLine ? lines : lines + 1;
    }

    private static Encoding DecodeAs(TextFileProbe probe) => probe.EncodingName switch
    {
        "UTF-16 LE" => Encoding.Unicode,
        "UTF-16 BE" => Encoding.BigEndianUnicode,
        "UTF-32 LE" => Encoding.UTF32,
        "8-bit text" => Encoding.Latin1,
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    private static bool StartsWith(ReadOnlySpan<byte> block, ReadOnlySpan<byte> prefix) =>
        block.Length >= prefix.Length && block[..prefix.Length].SequenceEqual(prefix);

    private static bool IsValidUtf8(ReadOnlySpan<byte> block, bool isWholeFile)
    {
        var index = 0;
        while (index < block.Length)
        {
            var lead = block[index];
            var extra = lead switch
            {
                < 0x80 => 0,
                >= 0xC2 and <= 0xDF => 1,
                >= 0xE0 and <= 0xEF => 2,
                >= 0xF0 and <= 0xF4 => 3,
                _ => -1,
            };

            if (extra < 0)
            {
                return false;
            }

            if (index + extra >= block.Length)
            {
                // Truncated by the sniff boundary: undecidable, so give it the benefit of the doubt.
                // Truncated at a real end-of-file: the sequence is simply broken.
                return !isWholeFile;
            }

            for (var offset = 1; offset <= extra; offset++)
            {
                if ((block[index + offset] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            index += extra + 1;
        }

        return true;
    }
}
