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

// A companion belongs to exactly one project and cannot be repointed, but the agent session that
// launched it can outlive that project: Filekin resets or removes the project while the session keeps
// running, and the session keeps relaunching this companion against whatever state database is there
// now. Such a companion has nothing left to coordinate, so it refuses to start rather than becoming a
// stale writer on a live database. The check never creates or migrates anything.
if (!await SqliteAgentProjectStore
        .ProjectExistsAsync(options.StateDatabasePath, options.ProjectId)
        .ConfigureAwait(false))
{
    await Console.Error
        .WriteLineAsync(
            $"Filekin has no agent project '{options.ProjectId:D}' in '{options.StateDatabasePath}', so "
            + "this companion refused to start. The agent session that launched it has outlived its "
            + "project; end that session in the agent's own tool.")
        .ConfigureAwait(false);
    return 2;
}

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
