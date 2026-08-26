using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Commands;
using Filekin.Core.Commands.App;
using Filekin.Core.Commands.App.External;
using Filekin.Core.Commands.References;
using Filekin.Core.Shell;
using Filekin.Infrastructure.Windows.Commands;
using Filekin.Infrastructure.Windows.FileSystem;
using Filekin.Infrastructure.Windows.References;
using Filekin.Infrastructure.Windows.Shell;

namespace Filekin.App.ViewModels;

/// <summary>
/// Runs one Files command-bar line end to end: resolves <c>@</c> references, classifies the input,
/// and dispatches it to the app-command subsystem, the persistent PowerShell runspace, or (once the
/// terminal surface exists) an interactive terminal tab. It returns a presentation-ready
/// <see cref="CommandExecutionOutcome"/> and never throws for ordinary command failures.
///
/// The runspace backend is created lazily on the first finite command and reused; its location is set
/// to the current Files folder before each command so the command bar always operates from the visible
/// folder (DECISIONS.md, 2026-08-24 — "Files Command Bar Working Directory Follows Files"). Interactive
/// tools and non-filesystem provider locations require the terminal renderer, which is the next task,
/// so for now they return an honest notice rather than a hidden or faked session.
/// </summary>
internal sealed class CommandExecutor : IAsyncDisposable
{
    private const int InlineLineLimit = 6;

    private readonly ReferenceResolver _resolver;
    private readonly CommandClassifier _classifier;
    private readonly AppCommandDispatcher _appCommands;
    private readonly IExternalLauncher _externalLauncher;
    private readonly SemaphoreSlim _shellGate = new(1, 1);
    private PowerShellRunspaceBackend? _shell;

    public CommandExecutor()
    {
        _externalLauncher = new WindowsExternalLauncher();
        _resolver = new ReferenceResolver(new WindowsKnownFolderLocations());
        _classifier = new CommandClassifier(new InteractiveCommandRegistry());
        _appCommands = BuiltInAppCommands.CreateDispatcher(new WindowsFileSystemOperations(), _externalLauncher);
    }

    /// <summary>The shared external launcher, so the GUI "open external terminal" action reuses it.</summary>
    public IExternalLauncher ExternalLauncher => _externalLauncher;

    public async Task<CommandExecutionOutcome> ExecuteAsync(
        string rawInput,
        ReferenceContext context,
        string currentFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);

        var resolved = _resolver.ResolveLine(rawInput, context);
        var classification = _classifier.Classify(resolved);

        // /recycle opens the Recycle Bin view; it is an app-owned view command, not a dispatched one.
        if (classification.Route == CommandRoute.AppCommand &&
            AppCommandParser.TryParse(resolved, out var appCommand) &&
            appCommand.Name.Equals("recycle", StringComparison.OrdinalIgnoreCase))
        {
            return CommandExecutionOutcome.RecycleBin();
        }

        return classification.Route switch
        {
            CommandRoute.AppCommand => await RunAppCommandAsync(resolved, currentFolderPath, cancellationToken).ConfigureAwait(true),
            CommandRoute.InteractiveTerminal => Interactive(classification),
            _ => await RunFiniteAsync(resolved, currentFolderPath, cancellationToken).ConfigureAwait(true),
        };
    }

    private async Task<CommandExecutionOutcome> RunAppCommandAsync(
        string resolved,
        string currentFolderPath,
        CancellationToken cancellationToken)
    {
        var location = new ShellLocation(currentFolderPath, "FileSystem", currentFolderPath);
        var result = await _appCommands.DispatchAsync(resolved, location, cancellationToken).ConfigureAwait(true);

        // Only commands that touched the filesystem (they report affected paths) need a re-list;
        // /ext reports none.
        return CommandExecutionOutcome.Inline(
            result.Succeeded ? CommandResultSeverity.Success : CommandResultSeverity.Error,
            result.Message,
            refreshListing: result.AffectedPaths.Count > 0);
    }

    private async Task<CommandExecutionOutcome> RunFiniteAsync(
        string resolved,
        string currentFolderPath,
        CancellationToken cancellationToken)
    {
        var shell = await GetShellAsync(currentFolderPath, cancellationToken).ConfigureAwait(true);
        var result = await shell.ExecuteAsync(resolved, cancellationToken).ConfigureAwait(true);

        var newFolder = result.CurrentLocation.FileSystemPath;

        if (result.TerminalLaunchRequest is not null)
        {
            // A non-filesystem provider (for example `cd HKLM:\`) belongs in a terminal tab, which the
            // renderer task will add. Report it honestly rather than faking a session.
            return CommandExecutionOutcome.Notice(
                "That location isn't a filesystem folder — it will open in a terminal tab once terminal support lands.",
                newFolder);
        }

        var lines = TrimTrailingBlank([.. result.Output, .. result.Errors]);
        var hasErrors = result.Errors.Count > 0;
        var severity = hasErrors ? CommandResultSeverity.Error : CommandResultSeverity.Success;

        if (lines.Count == 0)
        {
            return CommandExecutionOutcome.Inline(
                severity,
                hasErrors ? "Command failed." : "Done.",
                refreshListing: true,
                newFolderPath: newFolder);
        }

        if (lines.Count <= InlineLineLimit)
        {
            return CommandExecutionOutcome.Inline(
                severity,
                string.Join(Environment.NewLine, lines),
                refreshListing: true,
                newFolderPath: newFolder);
        }

        var label = $"{(hasErrors ? "Failed" : "Completed")} · {lines.Count} lines";
        return CommandExecutionOutcome.Summary(
            severity,
            label,
            string.Join(Environment.NewLine, lines),
            refreshListing: true,
            newFolderPath: newFolder);
    }

    private static CommandExecutionOutcome Interactive(CommandClassification classification)
    {
        var tool = string.IsNullOrEmpty(classification.Executable) ? "That tool" : classification.Executable;
        return CommandExecutionOutcome.Notice(
            $"{tool} is interactive — it will open in a terminal tab once terminal support lands.");
    }

    private async Task<PowerShellRunspaceBackend> GetShellAsync(string currentFolderPath, CancellationToken cancellationToken)
    {
        await _shellGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_shell is null)
            {
                _shell = await PowerShellRunspaceBackend.CreateAsync(currentFolderPath, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await _shell.SetFileSystemLocationAsync(currentFolderPath, cancellationToken).ConfigureAwait(true);
            }

            return _shell;
        }
        finally
        {
            _shellGate.Release();
        }
    }

    private static List<string> TrimTrailingBlank(List<string> lines)
    {
        var end = lines.Count;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
        {
            end--;
        }

        return end == lines.Count ? lines : lines.GetRange(0, end);
    }

    public async ValueTask DisposeAsync()
    {
        if (_shell is not null)
        {
            await _shell.DisposeAsync().ConfigureAwait(false);
            _shell = null;
        }

        _shellGate.Dispose();
    }
}
