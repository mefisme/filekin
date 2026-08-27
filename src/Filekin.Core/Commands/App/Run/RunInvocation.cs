namespace Filekin.Core.Commands.App.Run;

/// <summary>A validated <c>/run</c> request: one or more launch targets plus optional arguments.</summary>
public sealed record RunInvocation(IReadOnlyList<string> Targets, IReadOnlyList<string> Arguments);
