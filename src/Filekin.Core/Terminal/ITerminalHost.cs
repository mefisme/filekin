namespace Filekin.Core.Terminal;

/// <summary>
/// Creates hosted terminal sessions. The concrete host owns the platform mechanism
/// (Windows ConPTY in v1); callers depend only on this abstraction.
/// </summary>
public interface ITerminalHost
{
    ITerminalSession Start(TerminalSessionRequest request);
}
