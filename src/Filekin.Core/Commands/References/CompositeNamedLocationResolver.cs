namespace Filekin.Core.Commands.References;

/// <summary>Queries named-location sources in priority order and returns the first match.</summary>
public sealed class CompositeNamedLocationResolver : INamedLocationResolver
{
    private readonly IReadOnlyList<INamedLocationResolver> _resolvers;

    public CompositeNamedLocationResolver(params INamedLocationResolver[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        if (resolvers.Any(static resolver => resolver is null))
        {
            throw new ArgumentException("A named-location resolver cannot be null.", nameof(resolvers));
        }

        _resolvers = resolvers;
    }

    public bool TryResolve(string name, out string path)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var resolver in _resolvers)
        {
            if (resolver.TryResolve(name, out path))
            {
                return true;
            }
        }

        path = string.Empty;
        return false;
    }
}
