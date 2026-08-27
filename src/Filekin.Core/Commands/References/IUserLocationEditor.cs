namespace Filekin.Core.Commands.References;

/// <summary>Mutates the durable user-defined Locations shared by the sidebar and <c>@</c> resolver.</summary>
public interface IUserLocationEditor
{
    IReadOnlyList<NamedLocation> Locations { get; }

    Task<UserLocationEditResult> AddAsync(
        string name,
        string path,
        CancellationToken cancellationToken = default);

    Task<UserLocationEditResult> SetPathAsync(
        string name,
        string path,
        CancellationToken cancellationToken = default);

    Task<UserLocationEditResult> RenameAsync(
        string name,
        string newName,
        CancellationToken cancellationToken = default);

    Task<UserLocationEditResult> UpdateAsync(
        string name,
        string newName,
        string path,
        CancellationToken cancellationToken = default);

    Task<UserLocationEditResult> RemoveAsync(
        string name,
        CancellationToken cancellationToken = default);
}

public sealed record UserLocationEditResult(bool Succeeded, string Message)
{
    public static UserLocationEditResult Ok(string message) => new(true, message);

    public static UserLocationEditResult Fail(string message) => new(false, message);
}
