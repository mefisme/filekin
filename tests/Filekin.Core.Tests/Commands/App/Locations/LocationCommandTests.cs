using Filekin.Core.Commands.App;
using Filekin.Core.Commands.App.Locations;
using Filekin.Core.Commands.References;
using Filekin.Core.Shell;

namespace Filekin.Core.Tests.Commands.App.Locations;

[TestClass]
public sealed class LocationCommandTests
{
    private static readonly ShellLocation Work = new(@"D:\Work", "FileSystem", @"D:\Work");

    [TestMethod]
    public async Task AddResolvesARelativePathFromTheFilesLocation()
    {
        var editor = new FakeLocationEditor();
        var command = new LocationCommand(editor);

        var result = await command.ExecuteAsync(Context("add", "projects", "."));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("add", editor.LastOperation);
        Assert.AreEqual("projects", editor.LastName);
        Assert.AreEqual(@"D:\Work", editor.LastPath);
    }

    [TestMethod]
    public async Task SetChangesOnlyTheNamedLocationsPath()
    {
        var editor = new FakeLocationEditor();
        var command = new LocationCommand(editor);

        var result = await command.ExecuteAsync(Context("set", "projects", @"D:\New Work"));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("set", editor.LastOperation);
        Assert.AreEqual("projects", editor.LastName);
        Assert.AreEqual(@"D:\New Work", editor.LastPath);
    }

    [TestMethod]
    public async Task RenameAndRemoveUseTheirDistinctOperations()
    {
        var editor = new FakeLocationEditor();
        var command = new LocationCommand(editor);

        var renamed = await command.ExecuteAsync(Context("rename", "projects", "client-work"));
        Assert.IsTrue(renamed.Succeeded);
        Assert.AreEqual("rename", editor.LastOperation);
        Assert.AreEqual("client-work", editor.LastNewName);

        var removed = await command.ExecuteAsync(Context("remove", "client-work"));
        Assert.IsTrue(removed.Succeeded);
        Assert.AreEqual("remove", editor.LastOperation);
        Assert.AreEqual("client-work", editor.LastName);
    }

    [TestMethod]
    public async Task InvalidGrammarReturnsTheCompleteUsage()
    {
        var command = new LocationCommand(new FakeLocationEditor());

        var result = await command.ExecuteAsync(Context("set", "projects"));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "/location add");
        StringAssert.Contains(result.Message, "set <name> <path>");
    }

    private static AppCommandContext Context(params string[] arguments) =>
        new(Work, new ParsedAppCommand("location", arguments));

    private sealed class FakeLocationEditor : IUserLocationEditor
    {
        public IReadOnlyList<NamedLocation> Locations => [];

        public string? LastOperation { get; private set; }

        public string? LastName { get; private set; }

        public string? LastNewName { get; private set; }

        public string? LastPath { get; private set; }

        public Task<UserLocationEditResult> AddAsync(
            string name,
            string path,
            CancellationToken cancellationToken = default) =>
            Record("add", name, path: path);

        public Task<UserLocationEditResult> SetPathAsync(
            string name,
            string path,
            CancellationToken cancellationToken = default) =>
            Record("set", name, path: path);

        public Task<UserLocationEditResult> RenameAsync(
            string name,
            string newName,
            CancellationToken cancellationToken = default) =>
            Record("rename", name, newName);

        public Task<UserLocationEditResult> UpdateAsync(
            string name,
            string newName,
            string path,
            CancellationToken cancellationToken = default) =>
            Record("update", name, newName, path);

        public Task<UserLocationEditResult> RemoveAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Record("remove", name);

        private Task<UserLocationEditResult> Record(
            string operation,
            string name,
            string? newName = null,
            string? path = null)
        {
            LastOperation = operation;
            LastName = name;
            LastNewName = newName;
            LastPath = path;
            return Task.FromResult(UserLocationEditResult.Ok("Done."));
        }
    }
}
