using System.Runtime.InteropServices;
using Filekin.Core.Commands.References;
using Filekin.Infrastructure.Windows.References.Interop;

namespace Filekin.Infrastructure.Windows.References;

/// <summary>
/// Resolves the built-in Windows known-folder references (<c>@desktop</c>, <c>@documents</c>,
/// <c>@downloads</c>, <c>@pictures</c>, <c>@music</c>, <c>@videos</c>, <c>@home</c>) that Filekin
/// exposes as standard places (FEATURES.md — "/places"; DECISIONS.md — <c>/tidy @downloads</c>).
/// Most map to a <see cref="Environment.SpecialFolder"/>; Downloads has no such enum value and is
/// resolved through <c>SHGetKnownFolderPath</c>. User-defined Locations layer on top of this via the
/// same <see cref="INamedLocationResolver"/> port.
/// </summary>
public sealed class WindowsKnownFolderLocations : INamedLocationResolver
{
    public bool TryResolve(string name, out string path)
    {
        ArgumentNullException.ThrowIfNull(name);

        path = name.ToLowerInvariant() switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "downloads" => ResolveKnownFolder(KnownFolderInterop.Downloads),
            _ => string.Empty,
        };

        return path.Length > 0;
    }

    private static string ResolveKnownFolder(Guid folderId)
    {
        var pointer = IntPtr.Zero;
        try
        {
            var result = KnownFolderInterop.SHGetKnownFolderPath(in folderId, 0, IntPtr.Zero, out pointer);
            return result == 0 && pointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(pointer) ?? string.Empty
                : string.Empty;
        }
        finally
        {
            // The shell allocates ppszPath and requires the caller to free it whether the call
            // succeeded or not.
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}
