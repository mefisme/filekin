using System.Globalization;

namespace Filekin.Core.FileSystem;

/// <summary>
/// Formats a byte count as a short human-readable size. One formatter for the whole product, so the
/// Files listing, the Recycle Bin, Drives, and the Info sheet cannot drift apart on how they round.
/// </summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long? bytes)
    {
        if (bytes is not { } value)
        {
            return "—";
        }

        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var number = unit == 0
            ? value.ToString(CultureInfo.CurrentCulture)
            : size.ToString("0.#", CultureInfo.CurrentCulture);
        return $"{number} {Units[unit]}";
    }
}
