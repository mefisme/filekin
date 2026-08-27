namespace Filekin.Core.Operations;

/// <summary>
/// One successful filesystem relocation. Consumers such as operation history and saved-Location
/// rebasing need both sides; an affected-path list alone loses where the item came from.
/// </summary>
public sealed record PathRelocation(string SourcePath, string DestinationPath);
