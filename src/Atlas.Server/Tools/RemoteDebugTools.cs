using ModelContextProtocol.Server;
using System.ComponentModel;
using Atlas.Server.Debugging;
using Atlas.Server.Debugging.Parsers;

namespace Atlas.Server.Tools;

/// <summary>
/// MCP tools for remote dump analysis via WinDbg debug server (dbgsrv).
/// </summary>
[McpServerToolType]
public static class RemoteDebugTools
{
    [McpServerTool(Name = "remote_analyze_crash")]
    [Description("Analyze a crash dump on a remote WinDbg debug server. Returns structured crash analysis including exception info, faulting module, and stack trace.")]
    public static async Task<object> RemoteAnalyzeCrash(
        [Description("WinDbg connection string (e.g., 'tcp:server=vm2,port=5005' or 'ssl:server=vm2,port=5005')")] 
        string connectionString,
        [Description("Full path to the dump file on the remote machine (e.g., 'C:\\dumps\\app.dmp')")] 
        string dumpPath,
        [Description("Debug server password (if required and not in connection string)")] 
        string? password = null)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString, password);
            
            var openResult = await session.OpenDumpAsync(dumpPath);
            if (openResult.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                openResult.Contains("cannot", StringComparison.OrdinalIgnoreCase))
            {
                return new { error = "Failed to open dump", details = openResult, dumpPath };
            }

            var analyzeOutput = await session.AnalyzeCrashAsync();
            var result = AnalyzeParser.Parse(analyzeOutput);

            return new
            {
                status = "success",
                dumpPath,
                connectionString = SanitizeConnectionString(connectionString),
                crashType = result.CrashType,
                exceptionCode = result.ExceptionCode,
                exceptionDescription = result.ExceptionDescription,
                bugCheckCode = result.BugCheckCode,
                faultingModule = result.FaultingModule,
                faultingSymbol = result.FaultingSymbol,
                faultingImage = result.FaultingImage,
                processName = result.ProcessName,
                failureBucket = result.FailureBucket,
                stackTrace = result.StackFrames.Take(20).Select(f => new
                {
                    frame = f.Index,
                    module = f.Module,
                    function = f.Function,
                    offset = f.Offset
                }),
                note = result.StackFrames.Count > 20 
                    ? $"Showing first 20 of {result.StackFrames.Count} stack frames" 
                    : null
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                error = "Remote analysis failed", 
                message = ex.Message,
                connectionString = SanitizeConnectionString(connectionString),
                suggestion = GetSuggestion(ex)
            };
        }
    }

    [McpServerTool(Name = "remote_heap_stats")]
    [Description("Get heap statistics from a dump on a remote debug server. Shows object counts and sizes by type.")]
    public static async Task<object> RemoteHeapStats(
        [Description("WinDbg connection string (e.g., 'tcp:server=vm2,port=5005')")] 
        string connectionString,
        [Description("Full path to the dump file on the remote machine")] 
        string dumpPath,
        [Description("Debug server password (if required)")] 
        string? password = null,
        [Description("Number of top types to return (default: 50)")] 
        int top = 50)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString, password);
            
            var openResult = await session.OpenDumpAsync(dumpPath);
            if (openResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return new { error = "Failed to open dump", details = openResult };
            }

            var heapOutput = await session.DumpHeapStatsAsync();
            var result = HeapStatsParser.Parse(heapOutput);

            return new
            {
                status = "success",
                dumpPath,
                totalObjects = result.TotalObjects,
                totalBytes = result.TotalBytes,
                totalMB = Math.Round(result.TotalBytes / 1024.0 / 1024.0, 2),
                typeCount = result.TypeStats.Count,
                topTypes = result.TypeStats.Take(top).Select(t => new
                {
                    typeName = t.TypeName,
                    count = t.Count,
                    totalBytes = t.TotalSize,
                    totalMB = Math.Round(t.TotalSize / 1024.0 / 1024.0, 2)
                })
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                error = "Remote heap analysis failed", 
                message = ex.Message,
                suggestion = GetSuggestion(ex)
            };
        }
    }

    [McpServerTool(Name = "remote_stack_trace")]
    [Description("Get stack trace from a dump on a remote debug server. Supports both managed (.NET) and native stacks.")]
    public static async Task<object> RemoteStackTrace(
        [Description("WinDbg connection string (e.g., 'tcp:server=vm2,port=5005')")] 
        string connectionString,
        [Description("Full path to the dump file on the remote machine")] 
        string dumpPath,
        [Description("Stack type: 'managed' for .NET (!clrstack) or 'native' for native (k). Default: managed")] 
        string stackType = "managed",
        [Description("Debug server password (if required)")] 
        string? password = null)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString, password);
            
            var openResult = await session.OpenDumpAsync(dumpPath);
            if (openResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return new { error = "Failed to open dump", details = openResult };
            }

            string stackOutput;
            StackTraceResult result;

            if (stackType.Equals("native", StringComparison.OrdinalIgnoreCase))
            {
                stackOutput = await session.GetNativeStackAsync();
                result = StackParser.ParseNativeStack(stackOutput);
            }
            else
            {
                stackOutput = await session.GetClrStackAsync();
                result = StackParser.ParseClrStack(stackOutput);
            }

            return new
            {
                status = "success",
                dumpPath,
                stackType = result.StackType,
                threadId = result.ThreadId,
                frameCount = result.Frames.Count,
                frames = result.Frames.Select(f => new
                {
                    frame = f.Index,
                    module = f.Module,
                    type = f.TypeName,
                    method = f.MethodName,
                    offset = f.Offset,
                    fullName = f.FullName
                })
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                error = "Remote stack trace failed", 
                message = ex.Message,
                suggestion = GetSuggestion(ex)
            };
        }
    }

    [McpServerTool(Name = "remote_list_modules")]
    [Description("List loaded modules from a dump on a remote debug server.")]
    public static async Task<object> RemoteListModules(
        [Description("WinDbg connection string (e.g., 'tcp:server=vm2,port=5005')")] 
        string connectionString,
        [Description("Full path to the dump file on the remote machine")] 
        string dumpPath,
        [Description("Debug server password (if required)")] 
        string? password = null)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString, password);
            
            var openResult = await session.OpenDumpAsync(dumpPath);
            if (openResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return new { error = "Failed to open dump", details = openResult };
            }

            var modulesOutput = await session.ListModulesAsync();
            var result = ModuleParser.Parse(modulesOutput);

            return new
            {
                status = "success",
                dumpPath,
                moduleCount = result.Modules.Count,
                modules = result.Modules.Select(m => new
                {
                    name = m.Name,
                    startAddress = m.StartAddress,
                    size = m.Size,
                    sizeMB = Math.Round(m.Size / 1024.0 / 1024.0, 2),
                    status = m.Status
                })
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                error = "Remote module list failed", 
                message = ex.Message,
                suggestion = GetSuggestion(ex)
            };
        }
    }

    [McpServerTool(Name = "remote_debug_command")]
    [Description("Execute an arbitrary WinDbg command on a remote debug server. For advanced users.")]
    public static async Task<object> RemoteDebugCommand(
        [Description("WinDbg connection string (e.g., 'tcp:server=vm2,port=5005')")] 
        string connectionString,
        [Description("Full path to the dump file on the remote machine")] 
        string dumpPath,
        [Description("WinDbg command to execute (e.g., '!pe', '!threads', 'vertarget')")] 
        string command,
        [Description("Debug server password (if required)")] 
        string? password = null)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString, password);
            
            var openResult = await session.OpenDumpAsync(dumpPath);
            if (openResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return new { error = "Failed to open dump", details = openResult };
            }

            var output = await session.ExecuteCommandAsync(command);

            return new
            {
                status = "success",
                dumpPath,
                command,
                output = TruncateOutput(output, 10000)
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                error = "Remote command failed", 
                message = ex.Message,
                command,
                suggestion = GetSuggestion(ex)
            };
        }
    }

    private static string SanitizeConnectionString(string connectionString)
    {
        // Remove password from connection string for logging
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString, 
            @"password=[^,\s]+", 
            "password=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string GetSuggestion(Exception ex)
    {
        if (ex.Message.Contains("cdb.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Ensure Debugging Tools for Windows is installed (part of Windows SDK). Add cdb.exe to PATH or specify full path.";
        }

        if (ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) || 
            ex.Message.Contains("remote", StringComparison.OrdinalIgnoreCase))
        {
            return "Could not connect to debug server. Verify: (1) dbgsrv is running on the target, (2) connection string is correct, (3) firewall allows the port, (4) password is correct.";
        }

        if (ex is TimeoutException)
        {
            return "Debug server not responding. The server may be busy or the connection may have been lost.";
        }

        return "Check connection string format and ensure the remote debug server is accessible.";
    }

    private static string TruncateOutput(string output, int maxLength)
    {
        if (output.Length <= maxLength) return output;
        return output[..maxLength] + $"\n\n... (truncated, {output.Length - maxLength} more characters)";
    }
}
