using System.Security;

namespace Filekin.Infrastructure.Windows.Settings;

/// <summary>
/// The single in-memory owner of <c>settings.json</c>. Every durable preference — saved Locations,
/// theme, the startup location, and user-registered interactive programs — is read from and written
/// through this one object, so two settings surfaces can never write competing whole-document
/// snapshots and silently drop each other's changes.
///
/// A mutation writes durable configuration first and publishes the new snapshot only on success, so
/// a failed write can never leave the running UI disagreeing with the file (ARCHITECTURE.md —
/// "Saved Location Management").
/// </summary>
public sealed class UserSettingsService
{
    private readonly FilekinSettingsStore _store;
    private FilekinSettings _current = new();

    public UserSettingsService()
        : this(new FilekinSettingsStore())
    {
    }

    public UserSettingsService(FilekinSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Raised after a mutation has been written and the new snapshot published.</summary>
    public event EventHandler? Changed;

    /// <summary>The readable file backing these settings, shown in the Advanced settings category.</summary>
    public string SettingsPath => _store.SettingsPath;

    public FilekinSettings Current => _current;

    /// <summary>
    /// Loads settings, creating the file on first run so an advanced user has something to inspect.
    /// A failure to create it is reported as a warning, never as a startup failure.
    /// </summary>
    public async Task<SettingsLoadResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        _current = result.Settings;

        if (result.FileExists)
        {
            return result;
        }

        try
        {
            await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (IsPersistenceFailure(ex))
        {
            return result with
            {
                Warnings = [.. result.Warnings, $"Could not create settings.json: {ex.Message}"],
            };
        }
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to the current snapshot, writes the result, and publishes it.
    /// The in-memory snapshot is left untouched when the write fails.
    /// </summary>
    public async Task<SettingsSaveResult> UpdateAsync(
        Func<FilekinSettings, FilekinSettings> mutate,
        string successMessage = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var replacement = mutate(_current);
        try
        {
            await _store.SaveAsync(replacement, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPersistenceFailure(ex))
        {
            return SettingsSaveResult.Fail($"Could not save settings.json: {ex.Message}");
        }

        _current = replacement;
        Changed?.Invoke(this, EventArgs.Empty);
        return SettingsSaveResult.Ok(successMessage);
    }

    private static bool IsPersistenceFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException;
}
