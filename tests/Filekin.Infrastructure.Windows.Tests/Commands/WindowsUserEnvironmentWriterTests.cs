using Filekin.Infrastructure.Windows.Commands;
using Microsoft.Win32;

namespace Filekin.Infrastructure.Windows.Tests.Commands;

/// <summary>
/// These run against the real <c>HKCU\Environment</c> key, because the defect being guarded is a
/// registry value kind and nothing else would prove it. Each test uses its own throwaway value name
/// and removes it afterwards; PATH is never touched.
/// </summary>
[TestClass]
public sealed class WindowsUserEnvironmentWriterTests
{
    private const string Key = "Environment";

    private readonly string _name = "FilekinTestValue" + Guid.NewGuid().ToString("N")[..8];

    [TestCleanup]
    public void RemoveProbe()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        key?.DeleteValue(_name, throwOnMissingValue: false);
    }

    [TestMethod]
    public void AnExpandableValueStaysExpandable()
    {
        // Environment.SetEnvironmentVariable rewrites this as REG_SZ, after which %USERPROFILE%
        // stops expanding and every variable-based PATH entry silently stops resolving.
        Write(@"%USERPROFILE%\bin", RegistryValueKind.ExpandString);

        WindowsUserEnvironmentWriter.Write(_name, @"%USERPROFILE%\bin;C:\extra");

        Assert.AreEqual(RegistryValueKind.ExpandString, KindOf());
        Assert.AreEqual(@"%USERPROFILE%\bin;C:\extra", RawValue());
        Assert.AreNotEqual(RawValue(), ExpandedValue(), "REG_EXPAND_SZ must still expand.");
    }

    [TestMethod]
    public void APlainValueStaysPlain()
    {
        Write(@"C:\one", RegistryValueKind.String);

        WindowsUserEnvironmentWriter.Write(_name, @"C:\one;C:\two");

        Assert.AreEqual(RegistryValueKind.String, KindOf());
        Assert.AreEqual(@"C:\one;C:\two", RawValue());
    }

    [TestMethod]
    public void ANewValueCarryingAVariableIsStoredExpandable()
    {
        WindowsUserEnvironmentWriter.Write(_name, @"%LOCALAPPDATA%\tools");

        Assert.AreEqual(RegistryValueKind.ExpandString, KindOf());
    }

    [TestMethod]
    public void ANewPlainValueIsStoredAsAString()
    {
        WindowsUserEnvironmentWriter.Write(_name, @"C:\tools");

        Assert.AreEqual(RegistryValueKind.String, KindOf());
    }

    private void Write(string value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true)!;
        key.SetValue(_name, value, kind);
    }

    private RegistryValueKind KindOf()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key)!;
        return key.GetValueKind(_name);
    }

    private string RawValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key)!;
        return (string)key.GetValue(_name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)!;
    }

    private string ExpandedValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key)!;
        return (string)key.GetValue(_name, string.Empty)!;
    }
}
