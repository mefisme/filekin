using Filekin.Infrastructure.Windows.Commands;

namespace Filekin.Infrastructure.Windows.Tests.Commands;

[TestClass]
public sealed class WindowsUserPathEditorTests
{
    [TestMethod]
    public void AddPreservesTheExistingTextAndAppendsOneFolder()
    {
        var state = new FakePathState(@"%TOOLS%;C:\Existing", @"C:\Windows\System32");
        var editor = state.CreateEditor(path => path is @"C:\New Tool" or @"C:\Existing");

        var result = editor.AddDirectory(@"C:\New Tool\");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(@"%TOOLS%;C:\Existing;C:\New Tool", state.User);
        Assert.IsNotNull(result.Change);
    }

    [TestMethod]
    public void EquivalentExistingFolderIsNotDuplicated()
    {
        var state = new FakePathState(@"C:\Tools\", null);
        var editor = state.CreateEditor(_ => true);

        var result = editor.AddDirectory(@"c:\tools");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(@"C:\Tools\", state.User);
    }

    [TestMethod]
    public void SnapshotListsUserFoldersAndMarksMissingOnes()
    {
        var state = new FakePathState(@"C:\UserTools;C:\Missing", @"C:\Windows\System32");
        var editor = state.CreateEditor(path => !path.EndsWith("Missing", StringComparison.Ordinal));

        var entries = editor.GetSnapshot();

        // The machine list is deliberately absent: Filekin never edits it and never elevates.
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries[0].Exists);
        Assert.IsFalse(entries[1].Exists);
    }

    [TestMethod]
    public void RemovePreservesUnrelatedRawSegments()
    {
        var state = new FakePathState(@"C:\One;;%TOOLS%;C:\Three", null);
        var editor = state.CreateEditor(_ => true);

        var one = editor.GetSnapshot().Single(entry => entry.Value == @"C:\One");
        var removed = editor.Remove(one);

        Assert.IsTrue(removed.Succeeded);
        Assert.AreEqual(@";%TOOLS%;C:\Three", state.User);
    }

    [TestMethod]
    public void UndoRefusesToOverwriteANewerExternalEdit()
    {
        var state = new FakePathState(@"C:\One", null);
        var editor = state.CreateEditor(_ => true);
        var added = editor.AddDirectory(@"C:\Two");
        state.User = @"C:\One;C:\Two;C:\External";

        var undone = editor.Undo(added.Change!);

        Assert.IsFalse(undone.Succeeded);
        Assert.AreEqual(@"C:\One;C:\Two;C:\External", state.User);
        StringAssert.Contains(undone.Message, "did not overwrite");
    }

    [TestMethod]
    public void UndoRestoresTheExactPreviousValue()
    {
        var state = new FakePathState(@"%TOOLS%;C:\One;", null);
        var editor = state.CreateEditor(_ => true);
        var added = editor.AddDirectory(@"C:\Two");

        var undone = editor.Undo(added.Change!);

        Assert.IsTrue(undone.Succeeded);
        Assert.AreEqual(@"%TOOLS%;C:\One;", state.User);
    }

    [TestMethod]
    public void AccessDeniedDuringWriteBecomesAFailedResult()
    {
        var state = new FakePathState(@"C:\One", null);
        var editor = state.CreateEditor(
            _ => true,
            _ => throw new UnauthorizedAccessException("Access denied by policy."));

        var result = editor.AddDirectory(@"C:\Two");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "Access denied by policy");
        Assert.AreEqual(@"C:\One", state.User);
    }

    [TestMethod]
    public void RegistryIoFailureDuringWriteBecomesAFailedResult()
    {
        var state = new FakePathState(@"C:\One", null);
        var editor = state.CreateEditor(
            _ => true,
            _ => throw new IOException("Registry value is unavailable."));

        var result = editor.AddDirectory(@"C:\Two");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "Registry value is unavailable");
    }

    private sealed class FakePathState(string? user, string? machine)
    {
        public string? User { get; set; } = user;

        public string? Machine { get; } = machine;

        public WindowsUserPathEditor CreateEditor(
            Func<string, bool> exists,
            Action<string?>? write = null) => new(
            target => target == EnvironmentVariableTarget.User ? User : Machine,
            write ?? (value => User = value),
            exists);
    }
}
