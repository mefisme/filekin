using System.Runtime.InteropServices;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.FileSystem.Interop;

namespace Filekin.Infrastructure.Windows.FileSystem;

/// <summary>
/// The Windows implementation of the app-owned filesystem operations. Ordinary copy/move use
/// standard .NET filesystem APIs; <see cref="Recycle"/> uses the Windows-native Recycle Bin path and
/// brackets it with opaque shell-identity snapshots. This preserves normal Windows behavior while
/// distinguishing the exact new entry from older entries that share its original path.
/// </summary>
public sealed class WindowsFileSystemOperations : IFileSystemOperations
{
    public FileSystemEntryKind GetKind(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            return FileSystemEntryKind.File;
        }

        if (Directory.Exists(path))
        {
            return FileSystemEntryKind.Directory;
        }

        return FileSystemEntryKind.None;
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Copy(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, destinationPath);
        }
        else
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
    }

    public void Move(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
        }
    }

    public RecycleOutcome Recycle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var kind = GetKind(path);
        if (kind == FileSystemEntryKind.None)
        {
            throw new FileNotFoundException($"The recycle target no longer exists: {path}", path);
        }

        var recycleBin = new WindowsRecycleBin();
        var before = recycleBin.List();
        RecycleCore(path);
        var exactNewItem = FindExactNewItem(before, recycleBin.List(), path);
        return exactNewItem is null
            ? RecycleOutcome.Informational(
                path,
                kind,
                "Windows completed the delete without exposing one exact new Recycle Bin item.")
            : RecycleOutcome.Restorable(exactNewItem);
    }

    internal static RecycledItem? FindExactNewItem(
        IReadOnlyList<RecycledItem> before,
        IReadOnlyList<RecycledItem> after,
        string originalPath)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        var priorIdentities = before
            .Select(static item => item.RecycleBinIdentity)
            .Where(static identity => !string.IsNullOrWhiteSpace(identity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = after.Where(item =>
                !string.IsNullOrWhiteSpace(item.RecycleBinIdentity) &&
                !priorIdentities.Contains(item.RecycleBinIdentity) &&
                string.Equals(item.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static void RecycleCore(string path)
    {
        var buffer = new char[path.Length + 2];
        path.CopyTo(0, buffer, 0, path.Length);
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var operation = new ShellFileOperationInterop.ShFileOpStruct
            {
                Function = ShellFileOperationInterop.FoDelete,
                From = pinned.AddrOfPinnedObject(),
                Flags = (ushort)(ShellFileOperationInterop.FofAllowUndo | ShellFileOperationInterop.FofNoUi),
            };
            var result = ShellFileOperationInterop.SHFileOperation(ref operation);
            if (result != 0)
            {
                throw new IOException($"Deleting '{path}' failed. SHFileOperation returned 0x{result:X}.");
            }

            if (operation.AnyOperationsAborted != 0)
            {
                throw new IOException($"Deleting '{path}' was aborted before completion.");
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var target = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var target = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, target);
        }
    }
}
