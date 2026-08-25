namespace Filekin.ShellTerminalSpike;

internal static class TestUi
{
    public static async Task<int> RunAsync(string repositoryRoot)
    {
        using var backend = new PowerShellRunspaceBackend(repositoryRoot);
        var visualFilesLocation = repositoryRoot;

        Console.WriteLine("Filekin disposable location/runspace test UI");
        Console.WriteLine("Commands: files <path> | ps <PowerShell> | terminal | quit");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"FILES LOCATION: {visualFilesLocation}");
            Console.Write("spike> ");
            var input = Console.ReadLine();
            if (input is null || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (input.StartsWith("files ", StringComparison.OrdinalIgnoreCase))
            {
                var requested = input[6..].Trim().Trim('"');
                backend.SetFilesystemLocation(requested);
                visualFilesLocation = backend.GetLocation().ProviderPath ?? requested;
                continue;
            }

            if (input.StartsWith("ps ", StringComparison.OrdinalIgnoreCase))
            {
                var script = input[3..];
                var previousFilesLocation = visualFilesLocation;
                var result = backend.Execute(script);
                foreach (var line in result.StandardOutput)
                {
                    Console.WriteLine(line);
                }
                foreach (var line in result.StandardError)
                {
                    Console.Error.WriteLine(line);
                }

                if (result.Location.IsFilesystem)
                {
                    visualFilesLocation = result.Location.ProviderPath ?? result.Location.CurrentPath;
                }
                else
                {
                    Console.WriteLine($"ROUTE TO TERMINAL: provider={result.Location.ProviderName}; path={result.Location.CurrentPath}");
                    backend.SetFilesystemLocation(previousFilesLocation);
                }

                continue;
            }

            if (input.Equals("terminal", StringComparison.OrdinalIgnoreCase))
            {
                var powerShell = ExecutableLocator.FindOnPath("pwsh.exe")
                    ?? throw new FileNotFoundException("pwsh.exe was not found on PATH.");
                await using var terminal = ConPtySession.StartPowerShell(powerShell, visualFilesLocation, mirrorOutput: true);
                while (!await terminal.WaitForRootExitAsync(TimeSpan.FromMilliseconds(10)))
                {
                    var terminalInput = Console.ReadLine();
                    if (terminalInput is null)
                    {
                        break;
                    }

                    await terminal.WriteAsync(terminalInput + "\r");
                }
                continue;
            }

            Console.WriteLine("Unknown spike command.");
        }
    }
}
