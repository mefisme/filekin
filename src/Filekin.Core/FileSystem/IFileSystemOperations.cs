namespace Filekin.Core.FileSystem;

public enum FileSystemEntryKind
{
    None,
    File,
    Directory,
}

/// <summary>
/// The filesystem side-effects the app-owned file-operation commands depend on, expressed as a
/// narrow port so the command logic stays platform-neutral and unit-testable (DECISIONS.md,
/// 2026-08-24 — parsing/operation models must not be coupled to WPF or to a specific platform).
/// Ordinary copy/move use standard .NET filesystem APIs in the implementation; <see cref="Recycle"/>
/// uses the Windows-native Recycle Bin path (DECISIONS.md, 2026-08-24 — "Normal Delete Respects
/// Windows Recycle Bin Behavior"). All paths passed here are fully-qualified.
/// </summary>
public interface IFileSystemOperations
{
    /// <summary>Reports whether <paramref name="path"/> is a file, a directory, or absent.</summary>
    FileSystemEntryKind GetKind(string path);

    /// <summary>
    /// Creates <paramref name="path"/> and any missing parents. Succeeds silently when it already
    /// exists, so a caller that reuses an existing folder needs no separate check.
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>Copies a file, or a directory and its contents recursively, to a new full path.</summary>
    void Copy(string sourcePath, string destinationPath);

    /// <summary>Moves (or renames) a file or directory to a new full path.</summary>
    void Move(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a file or directory using native Recycle Bin behavior and reports whether the exact
    /// recoverable item is known. A successful delete is not necessarily recoverable on every volume
    /// or under every Windows Recycle Bin setting.
    /// </summary>
    RecycleOutcome Recycle(string path);
}
