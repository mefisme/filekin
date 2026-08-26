namespace Filekin.Core.Commands.App.External;

/// <summary>
/// Opens external operating-system surfaces from the current Files folder — the External Terminal
/// Escape Hatch (UX-DESIGN.md): users are never forced into the embedded workspace. Implementations
/// perform the OS-specific launch and surface a failure as <see cref="System.InvalidOperationException"/>
/// so the platform-neutral commands can report it; the app commands that call this only validate the
/// request.
/// </summary>
public interface IExternalLauncher
{
    /// <summary>Opens the user's default external terminal with its working directory at <paramref name="folderPath"/>.</summary>
    void OpenTerminal(string folderPath);

    /// <summary>
    /// Launches <paramref name="program"/> (resolved on the PATH / by association) as an independent
    /// external process whose working directory is <paramref name="folderPath"/>.
    /// </summary>
    void OpenExternal(string folderPath, string program, IReadOnlyList<string> arguments);
}
