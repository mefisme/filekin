using Filekin.Core.Terminal;

namespace Filekin.Infrastructure.Windows.Terminal;

/// <summary>
/// <see cref="ITerminalHost"/> backed by Windows ConPTY. Each started session runs a root
/// PowerShell process behind the terminal boundary.
/// </summary>
public sealed class ConPtyTerminalHost : ITerminalHost
{
    private readonly string _powerShellExecutable;

    public ConPtyTerminalHost()
        : this(null)
    {
    }

    /// <param name="powerShellExecutable">
    /// Full path to the PowerShell executable to host. When null the executable is resolved
    /// via <see cref="PowerShellExecutableLocator"/>.
    /// </param>
    public ConPtyTerminalHost(string? powerShellExecutable)
    {
        _powerShellExecutable = powerShellExecutable ?? PowerShellExecutableLocator.Resolve();
    }

    public ITerminalSession Start(TerminalSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ConPtyTerminalSession.Create(_powerShellExecutable, request);
    }
}
