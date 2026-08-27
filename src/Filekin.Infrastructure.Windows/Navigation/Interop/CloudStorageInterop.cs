using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Navigation.Interop;

internal static partial class CloudStorageInterop
{
    // The output buffer is a pinned Span<char> rather than a StringBuilder: source-generated
    // P/Invoke does not marshal StringBuilder (SYSLIB1051).
    [LibraryImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHLoadIndirectString(
        string source,
        Span<char> output,
        uint outputCharacters,
        IntPtr reserved);
}
