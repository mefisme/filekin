using Filekin.Core.Terminal;

namespace Filekin.App.ViewModels;

/// <summary>One hosted terminal session created by a command outcome.</summary>
public sealed record TerminalLaunchOutcome(ITerminalSession Session, string Title);
