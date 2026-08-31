using System;
using System.Globalization;

namespace Filekin.App.ViewModels;

/// <summary>
/// One line in the agent project's running account of what happened. The surface keeps these instead
/// of overwriting a single status line, because a line that replaces itself hides the very thing a
/// person is trying to follow.
/// </summary>
public sealed class AgentEventViewModel
{
    public AgentEventViewModel(DateTimeOffset at, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        At = at;
        Text = text;
        Time = at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public DateTimeOffset At { get; }

    /// <summary>The clock time, so a long run can be read back in order.</summary>
    public string Time { get; }

    public string Text { get; }
}
