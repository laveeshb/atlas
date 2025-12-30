using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "list_processes")]
    [Description("List all running processes with basic information")]
    public static object ListProcesses()
    {
        var processes = Process.GetProcesses()
            .Select(p => new
            {
                pid = p.Id,
                name = p.ProcessName,
                memoryMB = p.WorkingSet64 / 1024 / 1024,
                threads = p.Threads.Count
            })
            .OrderByDescending(p => p.memoryMB)
            .Take(50)
            .ToList();

        return new { count = processes.Count, processes };
    }

    [McpServerTool(Name = "get_process_details")]
    [Description("Get detailed information about a specific process by PID")]
    public static object GetProcessDetails(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return new
            {
                pid = p.Id,
                name = p.ProcessName,
                memoryMB = p.WorkingSet64 / 1024 / 1024,
                threads = p.Threads.Count,
                startTime = p.StartTime.ToString("o"),
                cpuTime = p.TotalProcessorTime.TotalSeconds,
                handleCount = p.HandleCount,
                mainWindowTitle = p.MainWindowTitle
            };
        }
        catch (ArgumentException)
        {
            return new { error = $"Process with PID {pid} not found" };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
