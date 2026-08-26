namespace Filekin.Core.Terminal.Emulation;

/// <summary>Input bytes a terminal must return in response to a VT query from the hosted app.</summary>
public sealed class TerminalResponseEventArgs : EventArgs
{
    public TerminalResponseEventArgs(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Response = response;
    }

    public string Response { get; }
}

