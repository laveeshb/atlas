using ModelContextProtocol.Server;
using System.ComponentModel;
using Atlas.Server.Debugging;
using Atlas.Server.Debugging.Parsers;

namespace Atlas.Server.Tools;

/// <summary>
/// MCP tools for remote dump analysis via remote.exe.
/// 
/// Server setup (on the remote machine):
///   remote.exe /s "cdb -z C:\dumps\crash.dmp" DumpSession
/// 
/// Connection format: "hostname/session" or "server=hostname,session=name"
/// </summary>
[McpServerToolType]
public static class RemoteDebugTools
{
    [McpServerTool(Name = "remote_analyze_crash")]
    [Description("Analyze a crash dump on a remote debug session. Server must be started with: remote.exe /s \"cdb -z dump.dmp\" SessionName. Returns structured crash analysis including exception info, faulting module, and stack trace.")]
    public static async Task<object> RemoteAnalyzeCrash(
        [Description("Connection string: 'hostname/session' or 'server=hostname,session=name' (e.g., 'vm2/DumpSession')")] 
        string connectionString)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString);
            
            var analyzeOutput = await session.AnalyzeCrashAsync();
            var result = AnalyzeParser.Parse(analyzeOutput);

            return new
            {
                status = "success",
                connectionString,
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
                connectionString,
                suggestion = GetSuggestion(ex)
            };
        }
    }

    [McpServerTool(Name = "remote_heap_stats")]
    [Description("Get heap statistics from a dump on a remote debug session. Shows object counts and sizes by type.")]
    public static async Task<object> RemoteHeapStats(
        [Description("Connection string: 'hostname/session' (e.g., 'vm2/DumpSession')")] 
        string connectionString,
        [Description("Number of top types to return (default: 50)")] 
        int top = 50)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString);

            var heapOutput = await session.DumpHeapStatsAsync();
            var result = HeapStatsParser.Parse(heapOutput);

            return new
            {
                status = "success",
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
    [Description("Get stack trace from a dump on a remote debug session. Supports both managed (.NET) and native stacks.")]
    public static async Task<object> RemoteStackTrace(
        [Description("Connection string: 'hostname/session' (e.g., 'vm2/DumpSession')")] 
        string connectionString,
        [Description("Stack type: 'managed' for .NET (!clrstack) or 'native' for native (k). Default: managed")] 
        string stackType = "managed")
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString);
            
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
    [Description("List loaded modules from a dump on a remote debug session.")]
    public static async Task<object> RemoteListModules(
        [Description("Connection string: 'hostname/session' (e.g., 'vm2/DumpSession')")] 
        string connectionString)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString);
            
            var modulesOutput = await session.ListModulesAsync();
            var result = ModuleParser.Parse(modulesOutput);

            return new
            {
                status = "success",
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
    [Description("Execute an arbitrary WinDbg command on a remote debug session. For advanced users.")]
    public static async Task<object> RemoteDebugCommand(
        [Description("Connection string: 'hostname/session' (e.g., 'vm2/DumpSession')")] 
        string connectionString,
        [Description("WinDbg command to execute (e.g., '!pe', '!threads', 'vertarget')")] 
        string command)
    {
        try
        {
            await using var session = await DebugSession.ConnectAsync(connectionString);
            
            var output = await session.ExecuteCommandAsync(command);

            return new
            {
                status = "success",
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

    private static string GetSuggestion(Exception ex)
    {
        if (ex.Message.Contains("remote.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Ensure Debugging Tools for Windows is installed (part of Windows SDK). Add remote.exe to PATH.";
        }

        if (ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) || 
            ex.Message.Contains("session", StringComparison.OrdinalIgnoreCase))
        {
            return "Could not connect to debug session. Verify: (1) remote.exe /s \"cdb -z dump.dmp\" SessionName is running on the target, (2) connection string format is 'hostname/session', (3) firewall allows SMB (port 445).";
        }

        if (ex is TimeoutException)
        {
            return "Debug session not responding. The server may be busy or the connection may have been lost.";
        }

        if (ex.Message.Contains("format", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "Connection string format: 'hostname/session' (e.g., 'vm2/DumpSession') or 'server=hostname,session=name'.";
        }

        return "Check connection string format (hostname/session) and ensure the remote debug session is accessible.";
    }

    private static string TruncateOutput(string output, int maxLength)
    {
        if (output.Length <= maxLength) return output;
        return output[..maxLength] + $"\n\n... (truncated, {output.Length - maxLength} more characters)";
    }
}
