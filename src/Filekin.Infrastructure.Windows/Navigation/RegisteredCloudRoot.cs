namespace Filekin.Infrastructure.Windows.Navigation;

/// <summary>A cloud-storage filesystem root registered with the Windows shell for this user.</summary>
public sealed record RegisteredCloudRoot(string DisplayName, string Path);

public interface IRegisteredCloudRootSource
{
    IReadOnlyList<RegisteredCloudRoot> GetCurrentUserRoots();
}
