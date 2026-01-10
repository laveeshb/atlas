using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging;

/// <summary>
/// Manages a connection to a remote debug session via remote.exe.
/// 
/// Server setup (on VM2):
///   remote.exe /s "cdb -z C:\dumps\crash.dmp" DumpSession
/// 
/// This class connects as a client using:
///   remote.exe /c ServerName SessionName
/// </summary>
public sealed class DebugSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _outputBuffer = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly TaskCompletionSource _readyTcs = new();
    private bool _isDisposed;

    private const string PromptPattern = @"\d+:\d+>";  // e.g., "0:000>"
    private const int DefaultTimeoutMs = 30000;

    private DebugSession(Process process)
    {
        _process = process;
    }

    /// <summary>
    /// Connect to a remote debug session via remote.exe using connection string.
    /// Parses connection string to extract server and session.
    /// Format: "hostname/sessionname", "hostname sessionname", or "server=hostname,session=name"
    /// </summary>
    public static async Task<DebugSession> ConnectAsync(
        string connectionString,
        string? remotePath = null,
        CancellationToken cancellationToken = default)
    {
        // Parse connection string
        // Supported formats:
        //   "hostname/sessionname"
        //   "hostname sessionname" 
        //   "server=hostname,session=name"
        
        string serverName;
        string sessionName;

        if (connectionString.Contains('='))
        {
            // Parse key=value format
            var parts = connectionString.Split(',');
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                {
                    dict[kv[0].Trim()] = kv[1].Trim();
                }
            }
            
            serverName = dict.GetValueOrDefault("server") ?? 
                         throw new ArgumentException("Connection string must include 'server=hostname'");
            sessionName = dict.GetValueOrDefault("session") ?? 
                          throw new ArgumentException("Connection string must include 'session=name'");
        }
        else if (connectionString.Contains('/'))
        {
            var parts = connectionString.Split('/', 2);
            serverName = parts[0].Trim();
            sessionName = parts[1].Trim();
        }
        else if (connectionString.Contains(' '))
        {
            var parts = connectionString.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            serverName = parts[0].Trim();
            sessionName = parts.Length > 1 ? parts[1].Trim() : throw new ArgumentException("Session name required");
        }
        else
        {
            throw new ArgumentException(
                "Invalid connection string format. Use 'hostname/session', 'hostname session', or 'server=hostname,session=name'");
        }

        return await ConnectWithParamsAsync(serverName, sessionName, remotePath, cancellationToken);
    }

    /// <summary>
    /// Connect to a remote debug session via remote.exe with explicit server and session.
    /// </summary>
    /// <param name="serverName">Server machine name or IP (e.g., "vm2" or "10.0.0.5")</param>
    /// <param name="sessionName">Session name specified when starting the server (e.g., "DumpSession")</param>
    /// <param name="remotePath">Path to remote.exe (defaults to searching PATH)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task<DebugSession> ConnectWithParamsAsync(
        string serverName,
        string sessionName,
        string? remotePath = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = remotePath ?? "remote.exe",
            Arguments = $"/c {serverName} {sessionName}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo };
        
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start remote.exe. Ensure Debugging Tools for Windows is installed. Error: {ex.Message}", ex);
        }

        var session = new DebugSession(process);

        // Wait for initial prompt
        try
        {
            await session.WaitForPromptAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            throw new InvalidOperationException(
                $"Failed to connect to debug session '{sessionName}' on '{serverName}'. Error: {ex.Message}", ex);
        }

        return session;
    }

    /// <summary>
    /// Execute a debugger command and return the output.
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            _outputBuffer.Clear();

            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();

            return await ReadUntilPromptAsync(cancellationToken);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Execute !analyze -v command.
    /// </summary>
    public Task<string> AnalyzeCrashAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("!analyze -v", cancellationToken);

    /// <summary>
    /// Execute !dumpheap -stat command.
    /// </summary>
    public Task<string> DumpHeapStatsAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("!dumpheap -stat", cancellationToken);

    /// <summary>
    /// Execute !clrstack command for managed stack traces.
    /// </summary>
    public Task<string> GetClrStackAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("!clrstack", cancellationToken);

    /// <summary>
    /// Execute k command for native stack traces.
    /// </summary>
    public Task<string> GetNativeStackAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("k", cancellationToken);

    /// <summary>
    /// Execute lm command to list modules.
    /// </summary>
    public Task<string> ListModulesAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("lm", cancellationToken);

    /// <summary>
    /// Execute !threads command.
    /// </summary>
    public Task<string> ListThreadsAsync(CancellationToken cancellationToken = default)
        => ExecuteCommandAsync("!threads", cancellationToken);

    /// <summary>
    /// Disconnect from the debug session without stopping the server.
    /// For remote.exe, we just kill the client process - the server keeps running.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_isDisposed) return;

        try
        {
            // Just kill the remote.exe client - server stays alive
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Quit the debug session AND stop the server.
    /// Sends 'q' to cdb, which terminates the remote.exe server.
    /// Use DisconnectAsync() if you want to keep the server running.
    /// </summary>
    public async Task QuitAsync()
    {
        if (_isDisposed) return;

        try
        {
            await _process.StandardInput.WriteLineAsync("q");
            await _process.StandardInput.FlushAsync();
            
            // Give it a moment to exit gracefully
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _process.Kill();
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    private async Task WaitForPromptAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeoutMs);

        await ReadUntilPromptAsync(timeoutCts.Token);
    }

    private async Task<string> ReadUntilPromptAsync(CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        var promptRegex = new Regex(PromptPattern);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeoutMs);

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            var readTask = _process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
            
            var completedTask = await Task.WhenAny(
                readTask,
                Task.Delay(DefaultTimeoutMs, timeoutCts.Token));

            if (completedTask != readTask)
            {
                throw new TimeoutException("Timed out waiting for debugger response");
            }

            var charsRead = await readTask;
            if (charsRead == 0)
            {
                throw new InvalidOperationException("Debug server connection closed unexpectedly");
            }

            output.Append(buffer, 0, charsRead);

            var currentOutput = output.ToString();
            if (promptRegex.IsMatch(currentOutput))
            {
                // Remove the prompt from output
                var lines = currentOutput.Split('\n');
                var resultLines = lines
                    .Where(l => !promptRegex.IsMatch(l.Trim()))
                    .ToArray();
                
                return string.Join('\n', resultLines).Trim();
            }
        }

        throw new OperationCanceledException("Operation was cancelled", cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(DebugSession));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Disconnect without stopping the server
        await DisconnectAsync();
        
        _process.Dispose();
        _commandLock.Dispose();
    }
}
