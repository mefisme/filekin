using System.Runtime.InteropServices;
using Filekin.Core.FileSystem;
using Filekin.Infrastructure.Windows.FileSystem.Interop;

namespace Filekin.Infrastructure.Windows.FileSystem;

/// <summary>
/// The Windows implementation of the app-owned filesystem operations. Ordinary copy/move use
/// standard .NET filesystem APIs; <see cref="Recycle"/> uses the Windows-native Recycle Bin path via
/// <c>SHFileOperationW</c> with <c>FOF_ALLOWUNDO</c> (DECISIONS.md, 2026-08-24 — "Normal Delete
/// Respects Windows Recycle Bin Behavior"; ENGINEERING-GUARDRAILS.md — Windows-native APIs for
/// Recycle Bin). Callers pass fully-qualified paths, which the Recycle Bin path requires.
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

    public void Recycle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // SHFileOperation requires a fully-qualified path for the Recycle Bin to be used, and pFrom
        // must be double-null terminated. Pin a copy of the path with the extra terminator so the
        // native side sees a valid single-entry buffer.
        var buffer = new char[path.Length + 2];
        path.CopyTo(0, buffer, 0, path.Length);
        buffer[path.Length] = '\0';
        buffer[path.Length + 1] = '\0';

        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var operation = new ShellFileOperationInterop.ShFileOpStruct
            {
                wFunc = ShellFileOperationInterop.FO_DELETE,
                pFrom = pinned.AddrOfPinnedObject(),
                pTo = IntPtr.Zero,
                fFlags = (ushort)(ShellFileOperationInterop.FOF_ALLOWUNDO | ShellFileOperationInterop.FOF_NO_UI),
            };

            var result = ShellFileOperationInterop.SHFileOperation(ref operation);
            if (result != 0)
            {
                throw new IOException(
                    $"Deleting '{path}' failed. SHFileOperation returned 0x{result:X}.");
            }

            if (operation.fAnyOperationsAborted != 0)
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
