using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Filekin.App.ViewModels;

internal sealed record FileLaunchResult(bool Succeeded, string Message);

/// <summary>
/// Opens a file through its Windows file association, the behavior GUI Open follows
/// (UX-DESIGN.md — "GUI Open follows Windows associations/default behavior"). Uses shell execution so
/// the registered default application handles the file. Ordinary launch failures become a result so
/// every caller can report them without crashing Filekin or claiming an action succeeded when it did not.
/// </summary>
internal static class FileLauncher
{
    public static FileLaunchResult TryOpen(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new FileLaunchResult(true, string.Empty);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new FileLaunchResult(false, ex.Message);
        }
    }
}
