using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Minimal newline-delimited JSON-RPC client for the installed local Codex App Server. It exposes
/// account, quota, thread, and turn primitives while leaving policy and persistence to higher layers.
/// </summary>
internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string _executable;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<CodexAppServerNotification> _notifications =
        Channel.CreateUnbounded<CodexAppServerNotification>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });
    private readonly Channel<CodexAppServerRequest> _serverRequests =
        Channel.CreateUnbounded<CodexAppServerRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });
    private readonly CodexAppServerLaunchPlan _launchPlan;
    private Process? _process;
    private Task? _readTask;
    private Task? _errorTask;
    private long _nextRequestId;

    public CodexAppServerClient(string executable = "codex")
        : this(CodexAppServerLaunchPlan.CreateInspection(executable))
    {
    }

    public CodexAppServerClient(
        AgentMcpLaunchConfiguration coordinationIdentity,
        string executable = "codex")
        : this(CodexAppServerLaunchPlan.CreateCoordination(coordinationIdentity, executable))
    {
    }

    internal CodexAppServerClient(CodexAppServerLaunchPlan launchPlan)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);
        _executable = launchPlan.ExecutablePath;
        _launchPlan = launchPlan;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is not null)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in _launchPlan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("The installed Codex App Server did not start.");
            }

            _process = process;
            _readTask = ReadResponsesAsync(process.StandardOutput, _lifetime.Token);
            _errorTask = DrainErrorsAsync(process.StandardError, _lifetime.Token);

            await RequestAsync(
                    "initialize",
                    new
                    {
                        clientInfo = new { name = "filekin", title = "Filekin", version = "0.1.0" },
                        capabilities = new { },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<CodexSubscriptionAccount> ReadAccountAsync(
        CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var result = await RequestAsync(
                "account/read",
                new { refreshToken = false },
                cancellationToken)
            .ConfigureAwait(false);
        return CodexAppServerProtocol.ParseAccount(result);
    }

    public async Task<JsonElement> ReadRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        return await RequestAsync("account/rateLimits/read", new { }, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<CodexAppServerNotification> ReadNotificationsAsync(
        CancellationToken cancellationToken = default) =>
        _notifications.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Surfaces native approval and input requests instead of silently resolving them. The future
    /// app-owned dispatcher can pause and present them; this client never auto-approves one.
    /// </summary>
    public IAsyncEnumerable<CodexAppServerRequest> ReadServerRequestsAsync(
        CancellationToken cancellationToken = default) =>
        _serverRequests.Reader.ReadAllAsync(cancellationToken);

    public async Task<CodexThreadSession> StartThreadAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        EnsureCoordinationFolder(folderPath);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var result = await RequestAsync(
                "thread/start",
                CodexAppServerProtocol.CreateThreadStartParameters(folderPath),
                cancellationToken)
            .ConfigureAwait(false);
        return CodexAppServerProtocol.ParseThread(result);
    }

    public async Task<CodexThreadSession> ResumeThreadAsync(
        string threadId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        EnsureCoordinationFolder(folderPath);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var result = await RequestAsync(
                "thread/resume",
                CodexAppServerProtocol.CreateThreadResumeParameters(threadId, folderPath),
                cancellationToken)
            .ConfigureAwait(false);
        return CodexAppServerProtocol.ParseThread(result);
    }

    /// <param name="trustFolder">
    /// Set only when the owner has said this folder is safe to work in. Otherwise Filekin sends no
    /// approval or sandbox setting and the owner's own Codex configuration stays in charge.
    /// </param>
    public async Task<CodexTurnHandle> StartTurnAsync(
        string threadId,
        string folderPath,
        string prompt,
        bool trustFolder = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        EnsureCoordinationFolder(folderPath);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var result = await RequestAsync(
                "turn/start",
                CodexAppServerProtocol.CreateTurnStartParameters(
                    threadId,
                    folderPath,
                    prompt,
                    trustFolder),
                cancellationToken)
            .ConfigureAwait(false);
        return CodexAppServerProtocol.ParseTurn(result, threadId);
    }

    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await RequestAsync(
                "turn/interrupt",
                new { threadId, turnId },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await RequestAsync(
                "thread/delete",
                new { threadId },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _startGate.Dispose();
        _writeGate.Dispose();
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("A duplicate Codex App Server request id was generated.");
        }

        try
        {
            await WriteAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("The Codex App Server is not running.");
        var json = JsonSerializer.Serialize(message);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadResponsesAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                {
                    if (root.TryGetProperty("method", out var methodElement) &&
                        methodElement.ValueKind == JsonValueKind.String &&
                        root.TryGetProperty("params", out var parameters))
                    {
                        await _notifications.Writer.WriteAsync(
                                new CodexAppServerNotification(methodElement.GetString()!, parameters.Clone()),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    continue;
                }

                if (CodexAppServerProtocol.TryParseServerRequest(root, out var serverRequest))
                {
                    await _serverRequests.Writer.WriteAsync(
                            serverRequest!,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!_pending.TryRemove(id, out var completion))
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    completion.TrySetException(new InvalidOperationException(
                        $"Codex App Server request failed: {error.GetRawText()}"));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(new InvalidOperationException(
                        "Codex App Server returned a response without a result or error."));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            var exception = failure ?? new EndOfStreamException("The Codex App Server output stream closed.");
            _notifications.Writer.TryComplete(cancellationToken.IsCancellationRequested ? null : exception);
            _serverRequests.Writer.TryComplete(cancellationToken.IsCancellationRequested ? null : exception);
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Stderr is drained so the child cannot block. It is intentionally not persisted:
                // provider diagnostics may contain paths or account details.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _lifetime.Cancel();
            if (_readTask is not null)
            {
                await IgnoreFailureAsync(_readTask).ConfigureAwait(false);
            }

            if (_errorTask is not null)
            {
                await IgnoreFailureAsync(_errorTask).ConfigureAwait(false);
            }

            process.Dispose();
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private void EnsureCoordinationFolder(string folderPath)
    {
        var identity = _launchPlan.CoordinationIdentity
            ?? throw new InvalidOperationException(
                "Codex turns require a fixed project/provider Filekin MCP launch identity.");
        var requested = Path.GetFullPath(folderPath);
        if (!string.Equals(
                requested.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                identity.WorkingDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Codex turn folder does not match its fixed Filekin MCP project folder.");
        }
    }
}

internal sealed record CodexAppServerNotification(string Method, JsonElement Parameters);
