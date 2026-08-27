using Filekin.Core.Commands.References;

namespace Filekin.Infrastructure.Windows.Settings;

/// <summary>
/// The single settings-backed Location catalog used by sidebar navigation, command references, and
/// both mouse and command-line editing flows. It reads and writes through the shared
/// <see cref="UserSettingsService"/>, so a Location edit never overwrites a preference changed from
/// the Settings surface.
/// </summary>
public sealed class SettingsBackedLocationCatalog : INamedLocationResolver, IUserLocationEditor
{
    private readonly UserSettingsService _settings;
    private readonly UserNamedLocationResolver _resolver = new();
    private IReadOnlyList<NamedLocation> _locations = [];

    public SettingsBackedLocationCatalog()
        : this(new UserSettingsService())
    {
    }

    public SettingsBackedLocationCatalog(FilekinSettingsStore store)
        : this(new UserSettingsService(store))
    {
    }

    public SettingsBackedLocationCatalog(UserSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public IReadOnlyList<NamedLocation> Locations => _locations;

    public async Task<SettingsLoadResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _settings.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Publish();
        return result;
    }

    public bool TryResolve(string name, out string path) => _resolver.TryResolve(name, out path);

    public Task<UserLocationEditResult> AddAsync(
        string name,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidate(name, path, out var normalizedName, out var normalizedPath, out var error))
        {
            return Task.FromResult(UserLocationEditResult.Fail(error));
        }

        if (FindIndex(normalizedName) >= 0)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"A Location named @{normalizedName} already exists."));
        }

        var locations = _settings.Current.Locations.ToList();
        locations.Add(new SavedLocation { Name = normalizedName, Path = normalizedPath });
        return SaveMutationAsync(
            locations,
            $"Added @{normalizedName} → {normalizedPath}",
            cancellationToken);
    }

    public Task<UserLocationEditResult> SetPathAsync(
        string name,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!FilekinSettingsStore.TryNormalizeLocationName(name, out var normalizedName, out var nameError))
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location name {nameError}."));
        }

        if (!FilekinSettingsStore.TryNormalizeLocationPath(path, out var normalizedPath))
        {
            return Task.FromResult(UserLocationEditResult.Fail("A Location path must be an absolute filesystem path."));
        }

        var index = FindIndex(normalizedName);
        if (index < 0)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location @{normalizedName} does not exist."));
        }

        var locations = _settings.Current.Locations.ToList();
        var existing = locations[index];
        locations[index] = new SavedLocation
        {
            Name = existing.Name,
            Path = normalizedPath,
            AdditionalProperties = existing.AdditionalProperties,
        };
        return SaveMutationAsync(
            locations,
            $"Updated @{existing.Name} → {normalizedPath}",
            cancellationToken);
    }

    public Task<UserLocationEditResult> RenameAsync(
        string name,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (!FilekinSettingsStore.TryNormalizeLocationName(name, out var normalizedName, out var currentNameError))
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location name {currentNameError}."));
        }

        if (!FilekinSettingsStore.TryNormalizeLocationName(newName, out var normalizedNewName, out var newNameError))
        {
            return Task.FromResult(UserLocationEditResult.Fail($"New Location name {newNameError}."));
        }

        var index = FindIndex(normalizedName);
        if (index < 0)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location @{normalizedName} does not exist."));
        }

        var duplicateIndex = FindIndex(normalizedNewName);
        if (duplicateIndex >= 0 && duplicateIndex != index)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"A Location named @{normalizedNewName} already exists."));
        }

        var locations = _settings.Current.Locations.ToList();
        var existing = locations[index];
        locations[index] = new SavedLocation
        {
            Name = normalizedNewName,
            Path = existing.Path,
            AdditionalProperties = existing.AdditionalProperties,
        };
        return SaveMutationAsync(
            locations,
            $"Renamed @{existing.Name} → @{normalizedNewName}",
            cancellationToken,
            renamedFrom: existing.Name,
            renamedTo: normalizedNewName);
    }

    public Task<UserLocationEditResult> RemoveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (!FilekinSettingsStore.TryNormalizeLocationName(name, out var normalizedName, out var nameError))
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location name {nameError}."));
        }

        var index = FindIndex(normalizedName);
        if (index < 0)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location @{normalizedName} does not exist."));
        }

        var locations = _settings.Current.Locations.ToList();
        var existing = locations[index];
        locations.RemoveAt(index);
        return SaveMutationAsync(
            locations,
            $"Removed @{existing.Name}. The folder was not deleted.",
            cancellationToken);
    }

    public Task<UserLocationEditResult> UpdateAsync(
        string name,
        string newName,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!FilekinSettingsStore.TryNormalizeLocationName(name, out var normalizedName, out var currentNameError))
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location name {currentNameError}."));
        }

        if (!TryValidate(newName, path, out var normalizedNewName, out var normalizedPath, out var error))
        {
            return Task.FromResult(UserLocationEditResult.Fail(error));
        }

        var index = FindIndex(normalizedName);
        if (index < 0)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"Location @{normalizedName} does not exist."));
        }

        var duplicateIndex = FindIndex(normalizedNewName);
        if (duplicateIndex >= 0 && duplicateIndex != index)
        {
            return Task.FromResult(UserLocationEditResult.Fail($"A Location named @{normalizedNewName} already exists."));
        }

        var locations = _settings.Current.Locations.ToList();
        var existing = locations[index];
        locations[index] = new SavedLocation
        {
            Name = normalizedNewName,
            Path = normalizedPath,
            AdditionalProperties = existing.AdditionalProperties,
        };
        return SaveMutationAsync(
            locations,
            $"Updated @{normalizedNewName} → {normalizedPath}",
            cancellationToken,
            renamedFrom: existing.Name,
            renamedTo: normalizedNewName);
    }

    private async Task<UserLocationEditResult> SaveMutationAsync(
        List<SavedLocation> locations,
        string successMessage,
        CancellationToken cancellationToken,
        string? renamedFrom = null,
        string? renamedTo = null)
    {
        var result = await _settings.UpdateAsync(
            current => current with
            {
                Locations = locations,
                OpenFilesAtLaunch = Retarget(current.OpenFilesAtLaunch, renamedFrom, renamedTo),
            },
            successMessage,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return UserLocationEditResult.Fail(result.Message);
        }

        Publish();
        return UserLocationEditResult.Ok(successMessage);
    }

    /// <summary>
    /// Follows a Location rename into the startup preference within the same durable write, so the
    /// startup target is never left pointing at a name that no longer exists (ARCHITECTURE.md —
    /// "Startup Files Location"). A <em>removed</em> Location is deliberately left alone: the
    /// preference survives so the user can repair it, and that launch falls back to Home.
    /// </summary>
    private static StartupLocation Retarget(StartupLocation startup, string? renamedFrom, string? renamedTo)
    {
        if (renamedFrom is null || renamedTo is null ||
            startup.Target != StartupTarget.Location ||
            !string.Equals(startup.Name, renamedFrom, StringComparison.OrdinalIgnoreCase))
        {
            return startup;
        }

        return startup with { Name = renamedTo };
    }

    private void Publish()
    {
        _locations = _settings.Current.Locations
            .Select(static location => new NamedLocation(location.Name, location.Path))
            .ToList();
        _resolver.Replace(_locations);
    }

    private int FindIndex(string name) => _settings.Current.Locations.FindIndex(location =>
        string.Equals(location.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryValidate(
        string name,
        string path,
        out string normalizedName,
        out string normalizedPath,
        out string error)
    {
        if (!FilekinSettingsStore.TryNormalizeLocationName(name, out normalizedName, out var nameError))
        {
            normalizedPath = string.Empty;
            error = $"Location name {nameError}.";
            return false;
        }

        if (!FilekinSettingsStore.TryNormalizeLocationPath(path, out normalizedPath))
        {
            error = "A Location path must be an absolute filesystem path.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
