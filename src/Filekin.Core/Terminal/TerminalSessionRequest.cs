using Filekin.Core.Shell;

namespace Filekin.Core.Terminal;

/// <summary>
/// A request to open a hosted terminal session. The root process is always a shell
/// (PowerShell in v1). <see cref="ShellTerminalLaunchRequest"/> supplies the location the
/// shell initializes at and the optional command it runs once — the interactive tool for a
/// tool launch, or nothing for a plain shell or a non-filesystem provider delegation.
/// </summary>
public sealed record TerminalSessionRequest
{
    public TerminalSessionRequest(
        ShellTerminalLaunchRequest launch,
        string? title = null,
        TerminalSize? initialSize = null,
        bool loadProfile = true)
    {
        ArgumentNullException.ThrowIfNull(launch);

        Launch = launch;
        Title = title;
        InitialSize = initialSize ?? TerminalSize.Default;
        LoadProfile = loadProfile;
    }

    /// <summary>
    /// The initial location the root shell moves to and the optional command it runs once.
    /// </summary>
    public ShellTerminalLaunchRequest Launch { get; }

    /// <summary>
    /// Display intent for the tab (for example <c>Claude · App</c>). This is a label only;
    /// it does not affect the hosted session. May be null for a plain shell.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// The pseudoconsole size to create the session with.
    /// </summary>
    public TerminalSize InitialSize { get; }

    /// <summary>
    /// Whether the root shell loads the user's PowerShell profile. Defaults to true so a
    /// hosted terminal behaves like the user's real shell. See the open product question in
    /// HANDOFF.md before treating this default as final.
    /// </summary>
    public bool LoadProfile { get; }
}
