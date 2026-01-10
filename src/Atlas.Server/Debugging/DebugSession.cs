using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging;

/// <summary>
/// Manages a connection to a WinDbg debug server via cdb.exe.
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
    /// Connect to a remote debug server.
    /// </summary>
    /// <param name="connectionString">WinDbg connection string (e.g., "tcp:server=vm2,port=5005")</param>
    /// <param name="password">Optional password (appended to connection string if provided)</param>
    /// <param name="cdbPath">Path to cdb.exe (defaults to searching PATH)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task<DebugSession> ConnectAsync(
        string connectionString,
        string? password = null,
        string? cdbPath = null,
        CancellationToken cancellationToken = default)
    {
        var fullConnectionString = string.IsNullOrEmpty(password)
            ? connectionString
            : $"{connectionString},password={password}";

        var startInfo = new ProcessStartInfo
        {
            FileName = cdbPath ?? "cdb.exe",
            Arguments = $"-remote {fullConnectionString}",
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
                $"Failed to start cdb.exe. Ensure Debugging Tools for Windows is installed. Error: {ex.Message}", ex);
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
                $"Failed to connect to debug server at '{connectionString}'. Error: {ex.Message}", ex);
        }

        return session;
    }

    /// <summary>
    /// Open a dump file on the remote machine.
    /// </summary>
    public async Task<string> OpenDumpAsync(string dumpPath, CancellationToken cancellationToken = default)
    {
        return await ExecuteCommandAsync($".opendump {dumpPath}", cancellationToken);
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
    /// Quit the debug session.
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

        await QuitAsync();
        
        _process.Dispose();
        _commandLock.Dispose();
    }
}
