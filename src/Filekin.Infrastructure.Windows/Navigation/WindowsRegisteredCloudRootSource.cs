using System.Security;
using System.Security.Principal;
using Filekin.Infrastructure.Windows.Navigation.Interop;
using Microsoft.Win32;

namespace Filekin.Infrastructure.Windows.Navigation;

/// <summary>Reads cloud sync roots registered with the Windows shell's SyncRootManager.</summary>
public sealed class WindowsRegisteredCloudRootSource : IRegisteredCloudRootSource
{
    private const string SyncRootManagerPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

    public IReadOnlyList<RegisteredCloudRoot> GetCurrentUserRoots()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                return [];
            }

            using var manager = Registry.LocalMachine.OpenSubKey(SyncRootManagerPath);
            if (manager is null)
            {
                return [];
            }

            var roots = new List<RegisteredCloudRoot>();
            foreach (var registrationName in manager.GetSubKeyNames())
            {
                TryAddRoot(manager, registrationName, sid, roots);
            }

            return roots;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static void TryAddRoot(
        RegistryKey manager,
        string registrationName,
        string sid,
        List<RegisteredCloudRoot> roots)
    {
        try
        {
            using var registration = manager.OpenSubKey(registrationName);
            using var userRoots = registration?.OpenSubKey("UserSyncRoots");
            var path = userRoots?.GetValue(sid) as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = Environment.ExpandEnvironmentVariables(path);
            var rawName = registration?.GetValue("DisplayNameResource") as string;
            var displayName = ResolveDisplayName(rawName) ?? ProviderName(registrationName);
            roots.Add(new RegisteredCloudRoot(displayName, path));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // One broken provider registration must not hide the valid siblings.
        }
    }

    private static string? ResolveDisplayName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        if (!rawName.StartsWith('@'))
        {
            return rawName.Trim();
        }

        // An "@dll,-id" indirect string is a resource reference; Windows resolves it to the provider's
        // own localized display name. A failure here is not fatal — the caller falls back to the
        // registration key's provider segment.
        Span<char> output = stackalloc char[512];
        if (CloudStorageInterop.SHLoadIndirectString(rawName, output, (uint)output.Length, IntPtr.Zero) != 0)
        {
            return null;
        }

        var end = output.IndexOf('\0');
        var resolved = (end < 0 ? output : output[..end]).Trim();
        return resolved.IsEmpty ? null : new string(resolved);
    }

    private static string ProviderName(string registrationName)
    {
        var delimiter = registrationName.IndexOf('!');
        return delimiter > 0 ? registrationName[..delimiter] : registrationName;
    }
}
