using System.ComponentModel;
using System.Diagnostics;
using Filekin.Core.Commands.App.External;

namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>
/// The Windows <see cref="IExternalLauncher"/>. It escapes to a real, independent external process at
/// the current Files folder (UX-DESIGN.md — External Terminal Escape Hatch). For a terminal it prefers
/// Windows Terminal opened at the folder and falls back to PowerShell there; arbitrary programs and the
/// file-manager reveal launch through the shell so PATH resolution and file associations apply. Launch
/// failures surface as <see cref="InvalidOperationException"/> so the platform-neutral commands report
/// them instead of crashing.
/// </summary>
public sealed class WindowsExternalLauncher : IExternalLauncher
{
    public void OpenTerminal(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        // Prefer Windows Terminal opened at the folder; fall back to PowerShell in that directory.
        if (TryStart(new ProcessStartInfo("wt.exe") { UseShellExecute = true, ArgumentList = { "-d", folderPath } }))
        {
            return;
        }

        if (TryStart(new ProcessStartInfo("pwsh.exe") { UseShellExecute = true, WorkingDirectory = folderPath }))
        {
            return;
        }

        Start(
            new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folderPath },
            "an external terminal");
    }

    public void OpenExternal(string folderPath, string program, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(program) { UseShellExecute = true, WorkingDirectory = folderPath };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Start(startInfo, program);
    }

    private static bool TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            // The program is not installed or not on the PATH; let the caller try a fallback.
            return false;
        }
    }

    private static void Start(ProcessStartInfo startInfo, string what)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new InvalidOperationException($"Could not start {what}: {ex.Message}", ex);
        }
    }
}
