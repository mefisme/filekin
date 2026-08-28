namespace Filekin.Infrastructure.Windows.Commands;

/// <summary>One folder in the current user's Windows PATH.</summary>
public sealed class WindowsPathEntry
{
    internal WindowsPathEntry(
        string value,
        string expandedValue,
        bool exists,
        int rawIndex,
        string? snapshotValue)
    {
        Value = value;
        ExpandedValue = expandedValue;
        Exists = exists;
        RawIndex = rawIndex;
        SnapshotValue = snapshotValue;
    }

    public string Value { get; }

    public string ExpandedValue { get; }

    public bool Exists { get; }

    internal int RawIndex { get; }

    internal string? SnapshotValue { get; }
}

/// <summary>
/// An optimistic, immediately undoable user-PATH write. Undo refuses if another process changed the
/// value after Filekin wrote it, so it can never erase a newer external edit.
/// </summary>
public sealed record WindowsUserPathChange
{
    internal WindowsUserPathChange(string? beforeValue, string? afterValue, string message)
    {
        BeforeValue = beforeValue;
        AfterValue = afterValue;
        Message = message;
    }

    internal string? BeforeValue { get; }

    internal string? AfterValue { get; }

    public string Message { get; }
}

/// <summary>The result of one requested user-PATH edit.</summary>
public sealed record WindowsUserPathEditResult
{
    private WindowsUserPathEditResult(bool succeeded, string message, WindowsUserPathChange? change)
    {
        Succeeded = succeeded;
        Message = message;
        Change = change;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public WindowsUserPathChange? Change { get; }

    public static WindowsUserPathEditResult Success(string message, WindowsUserPathChange change) =>
        new(true, message, change);

    public static WindowsUserPathEditResult Fail(string message) => new(false, message, null);
}

/// <summary>
/// Reads and edits the real Windows PATH for the current user. The machine-wide PATH is deliberately
/// out of scope: changing it needs administrator rights, and Filekin never elevates.
/// </summary>
public sealed class WindowsUserPathEditor
{
    private const int MaximumEnvironmentValueLength = 32766;

    private readonly Func<EnvironmentVariableTarget, string?> _read;
    private readonly Action<string?> _writeUser;
    private readonly Func<string, bool> _directoryExists;

    public WindowsUserPathEditor()
        : this(
            target => Environment.GetEnvironmentVariable("Path", target),
            value => Environment.SetEnvironmentVariable("Path", value, EnvironmentVariableTarget.User),
            Directory.Exists)
    {
    }

    internal WindowsUserPathEditor(
        Func<EnvironmentVariableTarget, string?> read,
        Action<string?> writeUser,
        Func<string, bool>? directoryExists = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _writeUser = writeUser ?? throw new ArgumentNullException(nameof(writeUser));
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public IReadOnlyList<WindowsPathEntry> GetSnapshot() => Parse(_read(EnvironmentVariableTarget.User));

    public WindowsUserPathEditResult AddDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var normalized = WindowsEnvironmentPath.NormalizeDirectory(directory);
        if (normalized is null || !Path.IsPathFullyQualified(normalized))
        {
            return WindowsUserPathEditResult.Fail("Choose an absolute folder for Windows user PATH.");
        }

        if (normalized.Contains(Path.PathSeparator, StringComparison.Ordinal))
        {
            return WindowsUserPathEditResult.Fail("Windows PATH cannot safely represent a folder containing ';'.");
        }

        if (!_directoryExists(normalized))
        {
            return WindowsUserPathEditResult.Fail("That folder no longer exists.");
        }

        var before = _read(EnvironmentVariableTarget.User);
        if (WindowsEnvironmentPath.ContainsDirectory(before, normalized))
        {
            return WindowsUserPathEditResult.Fail("That folder is already in Windows user PATH.");
        }

        var after = string.IsNullOrEmpty(before)
            ? normalized
            : before.EndsWith(Path.PathSeparator) ? before + normalized : before + Path.PathSeparator + normalized;

        if (after.Length > MaximumEnvironmentValueLength)
        {
            return WindowsUserPathEditResult.Fail("Windows user PATH is too long to add another folder safely.");
        }

        return Write(before, after, $"Added {normalized} to Windows user PATH.");
    }

    public WindowsUserPathEditResult Remove(WindowsPathEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var before = _read(EnvironmentVariableTarget.User);
        if (!string.Equals(before, entry.SnapshotValue, StringComparison.Ordinal))
        {
            return WindowsUserPathEditResult.Fail("Windows user PATH changed. Refresh Settings and try again.");
        }

        var parts = Split(before);
        if (!IsCurrentEntry(parts, entry))
        {
            return WindowsUserPathEditResult.Fail("That PATH entry is no longer present.");
        }

        parts.RemoveAt(entry.RawIndex);
        var after = string.Join(Path.PathSeparator, parts);
        return Write(before, after, $"Removed {entry.Value} from Windows user PATH.");
    }

    public WindowsUserPathEditResult Undo(WindowsUserPathChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var current = _read(EnvironmentVariableTarget.User);
        if (!string.Equals(current, change.AfterValue, StringComparison.Ordinal))
        {
            return WindowsUserPathEditResult.Fail(
                "Windows user PATH changed after Filekin's edit, so Undo did not overwrite it.");
        }

        try
        {
            _writeUser(change.BeforeValue);
            var inverse = new WindowsUserPathChange(change.AfterValue, change.BeforeValue, "Restored Windows user PATH.");
            return WindowsUserPathEditResult.Success("Restored Windows user PATH.", inverse);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Security.SecurityException)
        {
            return WindowsUserPathEditResult.Fail($"Could not restore Windows user PATH: {ex.Message}");
        }
    }

    private WindowsUserPathEditResult Write(string? before, string? after, string message)
    {
        try
        {
            _writeUser(after);
            return WindowsUserPathEditResult.Success(
                message,
                new WindowsUserPathChange(before, after, message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Security.SecurityException)
        {
            return WindowsUserPathEditResult.Fail($"Could not update Windows user PATH: {ex.Message}");
        }
    }

    private List<WindowsPathEntry> Parse(string? value)
    {
        var parts = Split(value);
        var entries = new List<WindowsPathEntry>();
        for (var index = 0; index < parts.Count; index++)
        {
            var raw = parts[index].Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
            entries.Add(new WindowsPathEntry(
                raw,
                expanded,
                WindowsEnvironmentPath.NormalizeDirectory(raw) is { } normalized && _directoryExists(normalized),
                index,
                value));
        }

        return entries;
    }

    private static List<string> Split(string? value) =>
        value is null ? [] : [.. value.Split(Path.PathSeparator, StringSplitOptions.None)];

    private static bool IsCurrentEntry(List<string> parts, WindowsPathEntry entry) =>
        entry.RawIndex >= 0 && entry.RawIndex < parts.Count &&
        string.Equals(parts[entry.RawIndex].Trim(), entry.Value, StringComparison.Ordinal);
}
