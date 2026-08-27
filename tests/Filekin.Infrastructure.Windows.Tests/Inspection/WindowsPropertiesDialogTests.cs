using System.Runtime.InteropServices;
using Filekin.Infrastructure.Windows.Inspection;
using Filekin.Infrastructure.Windows.Tests.FileSystem;

namespace Filekin.Infrastructure.Windows.Tests.Inspection;

/// <summary>
/// The Windows Properties escape hatch, checked against a real shell.
///
/// These open and close actual system dialogs, so they carry the same category CI excludes as the
/// real Recycle Bin tests. The regression they guard is specific: `ShellExecuteEx` with the
/// `properties` verb works for files, ordinary folders, `C:\Users`, and `C:\`, and fails with
/// ERROR_CANCELLED for the **user profile folder** — the one target a file manager opens constantly
/// (DECISIONS.md, 2026-08-27). A change back to the verb would pass every other case and break that
/// one, which is exactly what happened the first time.
/// </summary>
[TestClass]
[TestCategory(WindowsRecycleBinTests.RequiresInteractiveShell)]
public sealed class WindowsPropertiesDialogTests
{
    [TestMethod]
    public void TheUserProfileFolderOpensProperties()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        WindowsPropertiesDialog.Show(profile);

        Assert.IsTrue(WaitForDialog(Path.GetFileName(profile)), "No Properties dialog appeared.");
        CloseDialogs();
    }

    [TestMethod]
    public void AnOrdinaryFileOpensProperties()
    {
        var file = Path.Combine(Path.GetTempPath(), $"Filekin-Props-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "probe");

        try
        {
            WindowsPropertiesDialog.Show(file);

            Assert.IsTrue(WaitForDialog(Path.GetFileName(file)), "No Properties dialog appeared.");
            CloseDialogs();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void AMissingTargetIsReportedRatherThanSilentlyIgnored()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"Filekin-Absent-{Guid.NewGuid():N}", "nothing.txt");

        Assert.ThrowsExactly<InvalidOperationException>(() => WindowsPropertiesDialog.Show(missing));
    }

    private static bool WaitForDialog(string leaf)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (EnumerateWindows().Any(window =>
                    window.Title.Contains("Properties", StringComparison.OrdinalIgnoreCase) &&
                    window.Title.Contains(leaf, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return false;
    }

    private static void CloseDialogs()
    {
        foreach (var (handle, title) in EnumerateWindows())
        {
            if (title.Contains("Properties", StringComparison.OrdinalIgnoreCase))
            {
                _ = PostMessageW(handle, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

        Thread.Sleep(500);
    }

    private static List<(IntPtr Handle, string Title)> EnumerateWindows()
    {
        var windows = new List<(IntPtr, string)>();
        _ = EnumWindows(
            (handle, _) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                var buffer = new char[512];
                var length = GetWindowTextW(handle, buffer, buffer.Length);
                if (length > 0)
                {
                    windows.Add((handle, new string(buffer, 0, length)));
                }

                return true;
            },
            IntPtr.Zero);
        return windows;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
