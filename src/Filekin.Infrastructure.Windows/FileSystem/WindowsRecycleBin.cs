using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Filekin.Core.FileSystem;

namespace Filekin.Infrastructure.Windows.FileSystem;

/// <summary>
/// The Windows <see cref="IRecycleBin"/>, over the shell automation object (<c>Shell.Application</c>,
/// Recycle Bin namespace). It reads each item's name, original location, deletion time, and size, and
/// restores an item by invoking its shell "Restore" verb — the supported way to put a deleted item back
/// without presenting the raw <c>$Recycle.Bin</c> store. Each listed row retains its backing shell path
/// as an opaque internal identity so entries with the same original path cannot be confused. Shell
/// automation is apartment-threaded, so every call runs on a dedicated STA thread.
/// </summary>
public sealed partial class WindowsRecycleBin : IRecycleBin
{
    private const int RecycleBinFolderId = 10; // ssfBITBUCKET
    private const uint SherbNoConfirmation = 0x00000001;
    private const uint SherbNoProgressUi = 0x00000002;
    private const uint SherbNoSound = 0x00000004;
    private const int RecycleBinEmptyHResult = unchecked((int)0x8000FFFF); // returned when the bin is already empty

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public IReadOnlyList<RecycledItem> List() => RunSta(ListCore);

    public bool Restore(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RunSta(() => RestoreCore(item));
    }

    public bool DeleteForever(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RunSta(() => DeleteForeverCore(item));
    }

    public void Empty()
    {
        // SHEmptyRecycleBin over all drives (null root). We do our own confirm, so suppress the
        // shell's dialog, progress UI, and sound. A non-zero HRESULT means nothing was emptied
        // (already empty returns S_OK on modern Windows), so treat only real failures as errors.
        var hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
        if (hr is not 0 and not RecycleBinEmptyHResult)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static IReadOnlyList<RecycledItem> ListCore()
    {
        var results = new List<RecycledItem>();
        RunOnBin(bin =>
        {
            (int Location, int Date) columns = FindColumns(bin);
            dynamic items = bin.Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic entry = items.Item(i);
                var name = (string)entry.Name;
                var location = CleanText((string)bin.GetDetailsOf(entry, columns.Location));
                var deletedText = CleanText((string)bin.GetDetailsOf(entry, columns.Date));
                var isFolder = (bool)entry.IsFolder;
                var originalPath = string.IsNullOrEmpty(location) ? name : Path.Combine(location, name);
                var when = DateTime.TryParse(deletedText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)
                    ? d
                    : (DateTime?)null;

                results.Add(new RecycledItem(
                    name,
                    originalPath,
                    when,
                    isFolder ? null : SafeSize(entry),
                    isFolder,
                    (string)entry.Path));
            }
        });
        return results;
    }

    private static bool RestoreCore(RecycledItem target)
    {
        var restored = false;
        RunOnBin(bin =>
        {
            (int Location, int Date) columns = FindColumns(bin);
            dynamic items = bin.Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic entry = items.Item(i);
                var name = (string)entry.Name;
                var location = CleanText((string)bin.GetDetailsOf(entry, columns.Location));
                var originalPath = string.IsNullOrEmpty(location) ? name : Path.Combine(location, name);

                if (MatchesTarget((string)entry.Path, originalPath, target) &&
                    InvokeVerb(entry, "Restore", "Undelete", "Put Back"))
                {
                    restored = true;
                    break;
                }
            }
        });
        return restored;
    }

    private static bool DeleteForeverCore(RecycledItem target)
    {
        var deleted = false;
        RunOnBin(bin =>
        {
            (int Location, int Date) columns = FindColumns(bin);
            dynamic items = bin.Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic entry = items.Item(i);
                var name = (string)entry.Name;
                var location = CleanText((string)bin.GetDetailsOf(entry, columns.Location));
                var originalPath = string.IsNullOrEmpty(location) ? name : Path.Combine(location, name);

                if (MatchesTarget((string)entry.Path, originalPath, target))
                {
                    // The shell "Delete" verb pops Windows' own confirmation dialog, so instead remove the
                    // Recycle Bin's backing store directly: entry.Path is the "$R…" data file (or folder),
                    // and its "$I…" sibling holds the metadata. Deleting both drops the item silently.
                    deleted = DeleteBackingStore((string)entry.Path, (bool)entry.IsFolder);
                    break;
                }
            }
        });
        return deleted;
    }

    private static bool DeleteBackingStore(string dataPath, bool isFolder)
    {
        if (string.IsNullOrEmpty(dataPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(dataPath);
        var fileName = Path.GetFileName(dataPath);
        var metadataPath = directory is not null && fileName.StartsWith("$R", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(directory, "$I" + fileName[2..])
            : null;

        try
        {
            if (isFolder && Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
            else if (File.Exists(dataPath))
            {
                File.Delete(dataPath);
            }

            if (metadataPath is not null && File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool MatchesTarget(string recycleBinIdentity, string originalPath, RecycledItem target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.IsNullOrWhiteSpace(target.RecycleBinIdentity)
            ? string.Equals(originalPath, target.OriginalPath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(recycleBinIdentity, target.RecycleBinIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InvokeVerb(dynamic entry, params string[] names)
    {
        dynamic verbs = entry.Verbs();
        int count = verbs.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic verb = verbs.Item(i);
            var verbName = ((string)verb.Name).Replace("&", string.Empty, StringComparison.Ordinal);
            foreach (var wanted in names)
            {
                if (verbName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
        }

        return false;
    }

    private static (int Location, int Date) FindColumns(dynamic bin)
    {
        // Defaults for current Windows; corrected by header names when available.
        var location = 1;
        var date = 2;
        for (var i = 0; i < 12; i++)
        {
            var header = (string)bin.GetDetailsOf(null, i) ?? string.Empty;
            if (header.Contains("Original", StringComparison.OrdinalIgnoreCase))
            {
                location = i;
            }
            else if (header.Contains("Deleted", StringComparison.OrdinalIgnoreCase))
            {
                date = i;
            }
        }

        return (location, date);
    }

    private static long? SafeSize(dynamic entry)
    {
        try
        {
            return Convert.ToInt64((object)entry.Size, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string CleanText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // GetDetailsOf can wrap values (dates especially) in Unicode direction/format marks; drop them.
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is not UnicodeCategory.Control and not UnicodeCategory.Format)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    private static void RunOnBin(Action<dynamic> action)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return;
        }

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic? bin = shell?.Namespace(RecycleBinFolderId);
            if (bin is not null)
            {
                action(bin);
            }
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        return result;
    }
}
