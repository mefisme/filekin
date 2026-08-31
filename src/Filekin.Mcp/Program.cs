using Filekin.Core.Agents;
using Filekin.Infrastructure.Windows.Agents;
using Filekin.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length > 0 && args[0] == ClaudeStatusLineCommand.ModeArgument)
{
    if (!ClaudeStatusLineCommand.TryParseArguments(args, out var statusLineRequest))
    {
        await Console.Error
            .WriteLineAsync("The Filekin status-line helper accepts only its fixed project-scoped arguments.")
            .ConfigureAwait(false);
        return 1;
    }

    return await ClaudeStatusLineMode
        .RunAsync(statusLineRequest, Console.In, Console.Error)
        .ConfigureAwait(false);
}

var options = McpServerOptions.Parse(args);
var builder = Host.CreateApplicationBuilder([]);

// Standard output is reserved for the MCP protocol.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new AgentToolIdentity(options.ProjectId, options.Provider));
builder.Services.AddSingleton<IAgentProjectStore>(
    _ => new SqliteAgentProjectStore(options.StateDatabasePath));
builder.Services.AddSingleton<AgentCoordinationToolService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FilekinAgentTools>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
