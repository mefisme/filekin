namespace Filekin.ShellTerminalSpike;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("native-probe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("__NATIVE_STDOUT__");
            Console.Error.WriteLine("__NATIVE_STDERR__");
            return 7;
        }

        if (args.Length > 0 && args[0].Equals("unexpected-child", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"__UNEXPECTED_READY__ pid={Environment.ProcessId} redirected={Console.IsInputRedirected}");
            Console.Out.Flush();
            Console.Error.WriteLine("__UNEXPECTED_STDERR__");
            Console.Error.Flush();

            var readTask = Task.Run(Console.ReadLine);
            if (!readTask.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine("__UNEXPECTED_WAIT_TIMEOUT__");
                return 24;
            }

            var input = readTask.Result;
            Console.WriteLine(input is null ? "__UNEXPECTED_STDIN_EOF__" : $"__UNEXPECTED_INPUT__{input}");
            return input is null ? 23 : 0;
        }

        var repositoryRoot = RepositoryLocator.FindRoot(AppContext.BaseDirectory);

        if (args.Length > 0 && args[0].Equals("interactive", StringComparison.OrdinalIgnoreCase))
        {
            return await TestUi.RunAsync(repositoryRoot);
        }

        return await SpikeRunner.RunAsync(repositoryRoot);
    }
}

internal static class RepositoryLocator
{
    public static string FindRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PROJECT-SETUP.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Environment.CurrentDirectory;
    }
}

internal static class ExecutableLocator
{
    public static string? FindOnPath(string executable)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception) when (directory.Length > 0)
            {
                // Ignore malformed PATH entries in this disposable machine probe.
            }
        }

        return null;
    }
}
