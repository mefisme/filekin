using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.References.Interop;

/// <summary>
/// Native entry point for resolving a Windows known folder by its <c>KNOWNFOLDERID</c>. Per the
/// official <c>SHGetKnownFolderPath</c> documentation the returned string is allocated by the shell
/// and the caller must free it with <c>CoTaskMemFree</c> whether the call succeeds or not; the
/// returned path has no trailing backslash. Used only for folders without a
/// <see cref="System.Environment.SpecialFolder"/> equivalent (for example Downloads).
/// </summary>
internal static partial class KnownFolderInterop
{
    // FOLDERID_Downloads {374DE290-123F-4565-9164-39C4925E467B}
    internal static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [LibraryImport("shell32.dll")]
    internal static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
