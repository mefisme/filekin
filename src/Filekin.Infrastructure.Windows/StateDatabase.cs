namespace Filekin.Infrastructure.Windows;

/// <summary>
/// Filekin's `state.db` holds both operation history and agent coordination, so its
/// <c>PRAGMA user_version</c> describes the whole file. Every store in this assembly reads and writes
/// that one number; raise it here when any of them adds to the shared schema.
/// </summary>
internal static class StateDatabase
{
    public const int SchemaVersion = 8;
}
