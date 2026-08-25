namespace Filekin.Core.Commands.References;

/// <summary>
/// Resolves a named workspace location — a Windows known folder such as <c>@downloads</c> or a
/// user-defined Location — to an absolute filesystem path. The intrinsic <c>@thisfolder</c> and
/// <c>@selection</c> references are handled by <see cref="ReferenceResolver"/> directly and are not
/// routed here. User-defined Locations are a later subsystem; this port lets them extend the known
/// names without changing the resolver.
/// </summary>
public interface INamedLocationResolver
{
    bool TryResolve(string name, out string path);
}

/// <summary>A resolver that recognizes no named locations; used where only intrinsic references apply.</summary>
public sealed class EmptyNamedLocationResolver : INamedLocationResolver
{
    public static EmptyNamedLocationResolver Instance { get; } = new();

    public bool TryResolve(string name, out string path)
    {
        path = string.Empty;
        return false;
    }
}
