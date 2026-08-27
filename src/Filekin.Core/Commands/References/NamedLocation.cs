namespace Filekin.Core.Commands.References;

/// <summary>A user-defined named filesystem destination that becomes an <c>@name</c> reference.</summary>
public sealed record NamedLocation(string Name, string Path);
