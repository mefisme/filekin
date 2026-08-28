using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filekin.Core.Commands;
using Filekin.Core.Commands.App;
using Filekin.Core.Commands.App.External;
using Filekin.Core.Commands.App.Go;
using Filekin.Core.Commands.App.Info;
using Filekin.Core.Commands.App.Run;
using Filekin.Core.Commands.App.Tidy;
using Filekin.Core.Commands.App.Unzip;
using Filekin.Core.Commands.App.Zip;
using Filekin.Core.Commands.References;
using Filekin.Core.Shell;
using Filekin.Core.Terminal;
using Filekin.Infrastructure.Windows.Commands;
using Filekin.Infrastructure.Windows.FileSystem;
using Filekin.Infrastructure.Windows.Shell;
using Filekin.Infrastructure.Windows.Terminal;

namespace Filekin.App.ViewModels;

/// <summary>
/// Runs one Files command-bar line end to end: resolves <c>@</c> references, classifies the input,
/// and dispatches it to the app-command subsystem, the persistent PowerShell runspace, or a hosted
/// terminal tab. It returns a presentation-ready
/// <see cref="CommandExecutionOutcome"/> and never throws for ordinary command failures.
///
/// The runspace backend is created lazily on the first finite command and reused; its location is set
/// to the current Files folder before each command so the command bar always operates from the visible
/// folder (DECISIONS.md, 2026-08-24 — "Files Command Bar Working Directory Follows Files"). Interactive
/// tools and non-filesystem provider locations are launched as independent ConPTY sessions.
/// </summary>
internal sealed class CommandExecutor : IAsyncDisposable
{
    private const int InlineLineLimit = 6;

    private readonly ReferenceResolver _resolver;
    private readonly RunInvocationParser _runParser;
    private readonly GoInvocationParser _goParser;
    private readonly InfoInvocationParser _infoParser;
    private readonly UnzipInvocationParser _unzipParser;
    private readonly TidyInvocationParser _tidyParser;
    private readonly ZipInvocationParser _zipParser;
    private readonly CommandClassifier _classifier;
    private readonly WindowsRunTargetResolver _runTargets;
    private readonly AppCommandDispatcher _appCommands;
    private readonly LocationRebaseCoordinator _locationRebase;
    private readonly WindowsExternalLauncher _externalLauncher;
    private readonly ConPtyTerminalHost _terminalHost;
    private readonly SemaphoreSlim _shellGate = new(1, 1);
    private PowerShellRunspaceBackend? _shell;

    public CommandExecutor(
        INamedLocationResolver namedLocations,
        IUserLocationEditor userLocations,
        IUserLocationPathRebaser locationRebaser,
        IInteractiveCommandRegistry interactiveCommands)
    {
        ArgumentNullException.ThrowIfNull(namedLocations);
        ArgumentNullException.ThrowIfNull(userLocations);
        ArgumentNullException.ThrowIfNull(locationRebaser);
        ArgumentNullException.ThrowIfNull(interactiveCommands);
        _externalLauncher = new WindowsExternalLauncher();
        _resolver = new ReferenceResolver(namedLocations);
        _runParser = new RunInvocationParser(_resolver);
        _goParser = new GoInvocationParser(_resolver);
        _infoParser = new InfoInvocationParser(_resolver);
        _unzipParser = new UnzipInvocationParser(_resolver);
        _tidyParser = new TidyInvocationParser(_resolver);
        _zipParser = new ZipInvocationParser(_resolver);

        // The registry is supplied rather than created here so the Settings surface can add the
        // user's own interactive programs to the live classifier without a restart.
        _classifier = new CommandClassifier(interactiveCommands);
        _runTargets = new WindowsRunTargetResolver(interactiveCommands);
        var fileOperations = new WindowsFileSystemOperations();
        _appCommands = BuiltInAppCommands.CreateDispatcher(
            fileOperations,
            _externalLauncher,
            userLocations);
        _locationRebase = new LocationRebaseCoordinator(fileOperations, locationRebaser);
        _terminalHost = new ConPtyTerminalHost();
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

        // /go, /run, /info, /unzip, /zip, and /tidy own their own argument grammar and must be parsed before
        // the ordinary reference rewrite, so a multi-item @selection survives as several targets.
        if (AppCommandParser.TryParse(rawInput, out var rawAppCommand))
        {
            if (rawAppCommand.Name.Equals("go", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _goParser.Parse(rawInput, context);
                return parsed.Succeeded
                    ? await ExecuteGoAsync(parsed.Invocation!, cancellationToken).ConfigureAwait(true)
                    : CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
            }

            if (rawAppCommand.Name.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteRunAsync(rawInput, context, currentFolderPath).ConfigureAwait(true);
            }

            if (rawAppCommand.Name.Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _infoParser.Parse(rawInput, context);
                return parsed.Succeeded
                    ? CommandExecutionOutcome.Info(parsed.Invocation!.Targets)
                    : CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
            }

            if (rawAppCommand.Name.Equals("unzip", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _unzipParser.Parse(rawInput, context);
                return parsed.Succeeded
                    ? CommandExecutionOutcome.Unzip(parsed.Invocation!)
                    : CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
            }

            if (rawAppCommand.Name.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _zipParser.Parse(rawInput, context);
                return parsed.Succeeded
                    ? CommandExecutionOutcome.Zip(parsed.Invocation!)
                    : CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
            }

            if (rawAppCommand.Name.Equals("tidy", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _tidyParser.Parse(rawInput, context);
                return parsed.Succeeded
                    ? CommandExecutionOutcome.Tidy(parsed.Invocation!)
                    : CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
            }
        }

        var resolved = _resolver.ResolveLine(rawInput, context);
        var classification = _classifier.Classify(resolved);

        // Rich-view commands are app-owned surface navigation, not dispatched file operations.
        if (classification.Route == CommandRoute.AppCommand &&
            AppCommandParser.TryParse(resolved, out var appCommand))
        {
            if (appCommand.Name.Equals("recycle", StringComparison.OrdinalIgnoreCase))
            {
                return CommandExecutionOutcome.RecycleBin();
            }

            if (appCommand.Name.Equals("places", StringComparison.OrdinalIgnoreCase))
            {
                return CommandExecutionOutcome.Places();
            }

            if (appCommand.Name.Equals("drives", StringComparison.OrdinalIgnoreCase))
            {
                return CommandExecutionOutcome.Drives();
            }

            if (appCommand.Name.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                return CommandExecutionOutcome.Settings();
            }
        }

        return classification.Route switch
        {
            CommandRoute.AppCommand => await RunAppCommandAsync(resolved, currentFolderPath, cancellationToken).ConfigureAwait(true),
            CommandRoute.InteractiveTerminal => StartInteractive(resolved, classification, currentFolderPath),
            _ => await RunFiniteAsync(resolved, currentFolderPath, cancellationToken).ConfigureAwait(true),
        };
    }

    /// <summary>
    /// Whether a finite raw-shell invocation is a concrete Windows console target that should receive
    /// the delayed one-time terminal-relaunch offer if it remains active.
    /// </summary>
    public bool ShouldOfferTerminalFallback(
        string rawInput,
        ReferenceContext context,
        string currentFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);

        var resolved = _resolver.ResolveLine(rawInput, context);
        var classification = _classifier.Classify(resolved);
        return classification.Route == CommandRoute.FiniteShell &&
               classification.Executable is { Length: > 0 } executable &&
               _runTargets.IsTerminalCommand(executable, currentFolderPath);
    }

    /// <summary>Starts a fresh hosted terminal with the same resolved raw-shell command.</summary>
    public CommandExecutionOutcome StartInTerminal(
        string rawInput,
        ReferenceContext context,
        string currentFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);

        var resolved = _resolver.ResolveLine(rawInput, context);
        var classification = _classifier.Classify(resolved);
        return StartInteractive(resolved, classification, currentFolderPath);
    }

    private async Task<CommandExecutionOutcome> ExecuteRunAsync(
        string rawInput,
        ReferenceContext context,
        string currentFolderPath)
    {
        var parsed = _runParser.Parse(rawInput, context);
        if (!parsed.Succeeded)
        {
            return CommandExecutionOutcome.Inline(CommandResultSeverity.Error, parsed.Error!);
        }

        // Resolving a target reads the filesystem and PE headers, and shell execution creates a
        // process, so the whole launch stays off the UI thread (ENGINEERING-GUARDRAILS.md —
        // Performance). A hosted session buffers its output until the tab subscribes, so starting
        // one here loses nothing.
        var invocation = parsed.Invocation!;
        return await Task.Run(() => LaunchRunTargets(invocation, currentFolderPath)).ConfigureAwait(true);
    }

    private static Task<CommandExecutionOutcome> ExecuteGoAsync(
        GoInvocation invocation,
        CancellationToken cancellationToken) =>
        Task.Run(() => ResolveGoTarget(invocation), cancellationToken);

    private static CommandExecutionOutcome ResolveGoTarget(GoInvocation invocation)
    {
        if (Directory.Exists(invocation.FolderPath))
        {
            return CommandExecutionOutcome.Navigate(invocation.FolderPath);
        }

        var message = File.Exists(invocation.FolderPath)
            ? $"{invocation.FolderPath} is a file, not a folder."
            : $"Folder not found: {invocation.FolderPath}";
        return CommandExecutionOutcome.Inline(CommandResultSeverity.Error, message);
    }

    private CommandExecutionOutcome LaunchRunTargets(RunInvocation invocation, string currentFolderPath)
    {
        var terminals = new List<TerminalLaunchOutcome>();
        var errors = new List<string>();
        var launched = 0;

        foreach (var target in invocation.Targets)
        {
            RunTargetResolution? resolution = null;
            try
            {
                resolution = _runTargets.Resolve(target, invocation.Arguments, currentFolderPath);
                if (resolution.Kind == RunTargetKind.Directory)
                {
                    errors.Add($"{resolution.DisplayName}: folders are navigated in Files, not run.");
                    continue;
                }

                if (resolution.Kind == RunTargetKind.Terminal)
                {
                    var command = BuildPowerShellInvocation(resolution.LaunchTarget, invocation.Arguments);
                    var title = BuildTerminalTitle(resolution.DisplayName, currentFolderPath);
                    var location = new ShellLocation(currentFolderPath, "FileSystem", currentFolderPath);
                    var launch = new ShellTerminalLaunchRequest(location, command);
                    var session = _terminalHost.Start(new TerminalSessionRequest(launch, title));
                    terminals.Add(new TerminalLaunchOutcome(session, title));
                }
                else
                {
                    _externalLauncher.OpenExternal(
                        currentFolderPath,
                        resolution.LaunchTarget,
                        invocation.Arguments);
                }

                launched++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                // A name that never resolved reads better as "not found" than as the Windows
                // process-start wording, which repeats the name twice and names the working directory.
                errors.Add(resolution is { FoundOnDisk: false }
                    ? $"{target}: not found in this folder or on PATH."
                    : $"{resolution?.DisplayName ?? target}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            // One target speaks for itself; a batch needs to say how it split.
            var failureText = invocation.Targets.Count == 1
                ? errors[0]
                : $"Launched {launched}; {errors.Count} failed. {string.Join(" ", errors)}";
            return CommandExecutionOutcome.RunResult(CommandResultSeverity.Error, failureText, terminals);
        }

        var text = terminals.Count > 0
            ? string.Empty
            : launched == 1
                ? $"Launched {Path.GetFileName(invocation.Targets[0])}."
                : $"Launched {launched} items.";
        return CommandExecutionOutcome.RunResult(CommandResultSeverity.Success, text, terminals);
    }

    private async Task<CommandExecutionOutcome> RunAppCommandAsync(
        string resolved,
        string currentFolderPath,
        CancellationToken cancellationToken)
    {
        var location = new ShellLocation(currentFolderPath, "FileSystem", currentFolderPath);
        // File operations can recurse, cross volumes, or enter the shell Recycle Bin. The command
        // abstractions are synchronous today, so dispatch them away from WPF's dispatcher thread.
        var result = await Task.Run(
            () => _appCommands.DispatchAsync(resolved, location, cancellationToken),
            cancellationToken).ConfigureAwait(true);

        var message = result.Message;
        // A partial move still carries the exact relocations that completed. Saved Locations must
        // follow those items even though unrelated targets failed.
        if (result.Relocations.Count > 0)
        {
            var rebase = await Task.Run(
                () => _locationRebase.RebaseOrRollbackAsync(result.Relocations),
                CancellationToken.None).ConfigureAwait(true);
            if (!rebase.Succeeded)
            {
                return CommandExecutionOutcome.Inline(
                    CommandResultSeverity.Error,
                    rebase.Message,
                    refreshListing: true);
            }

            if (rebase.UpdatedCount > 0)
            {
                message += $" · Updated {rebase.UpdatedCount} saved {(rebase.UpdatedCount == 1 ? "Location" : "Locations")}.";
            }
        }

        // Any command that may have written needs a re-list, including one that failed part way
        // through a batch. /ext never touches the filesystem and reports none.
        var severity = result.Outcome switch
        {
            AppCommandOutcome.Success => CommandResultSeverity.Success,
            AppCommandOutcome.PartialSuccess => CommandResultSeverity.Warning,
            _ => CommandResultSeverity.Error,
        };
        return CommandExecutionOutcome.Inline(
            severity,
            message,
            refreshListing: result.TouchedFileSystem);
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
            var session = _terminalHost.Start(new TerminalSessionRequest(
                result.TerminalLaunchRequest,
                title: "PowerShell"));
            return CommandExecutionOutcome.Terminal(session, "PowerShell");
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

    private CommandExecutionOutcome StartInteractive(
        string command,
        CommandClassification classification,
        string currentFolderPath)
    {
        var executable = string.IsNullOrEmpty(classification.Executable) ? "PowerShell" : classification.Executable;
        var title = BuildTerminalTitle(executable, currentFolderPath);
        var location = new ShellLocation(currentFolderPath, "FileSystem", currentFolderPath);
        var launch = new ShellTerminalLaunchRequest(location, command.Trim());
        var session = _terminalHost.Start(new TerminalSessionRequest(launch, title));
        return CommandExecutionOutcome.Terminal(session, title);
    }

    public CommandExecutionOutcome StartPowerShell(string currentFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);
        var location = new ShellLocation(currentFolderPath, "FileSystem", currentFolderPath);
        var title = BuildTerminalTitle("PowerShell", currentFolderPath);
        var session = _terminalHost.Start(new TerminalSessionRequest(new ShellTerminalLaunchRequest(location), title));
        return CommandExecutionOutcome.Terminal(session, title);
    }

    private static string BuildTerminalTitle(string executable, string currentFolderPath)
    {
        var toolName = Path.GetFileNameWithoutExtension(executable);
        if (string.IsNullOrWhiteSpace(toolName))
        {
            toolName = executable;
        }

        var tool = char.ToUpperInvariant(toolName[0]) + toolName[1..];
        var folder = Path.GetFileName(currentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(folder) ? tool : $"{tool} · {folder}";
    }

    private static string BuildPowerShellInvocation(string target, IReadOnlyList<string> arguments)
    {
        var tokens = new List<string>(arguments.Count + 2)
        {
            "&",
            QuoteForPowerShell(target),
        };
        tokens.AddRange(arguments.Select(QuoteForPowerShell));
        return string.Join(' ', tokens);
    }

    private static string QuoteForPowerShell(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

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
