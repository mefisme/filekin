using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Filekin.App.ViewModels;

/// <summary>
/// Opens a file through its Windows file association, the behavior GUI Open follows
/// (UX-DESIGN.md — "GUI Open follows Windows associations/default behavior"). Uses shell execution so
/// the registered default application handles the file. Failures (no association, launch refused) are
/// swallowed here rather than crashing the shell; a richer, user-visible error path belongs with the
/// command-execution work, not the file listing.
/// </summary>
internal static class FileLauncher
{
    public static void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // No default handler, or the shell refused to launch it. Leave the selection as-is.
        }
    }
}
