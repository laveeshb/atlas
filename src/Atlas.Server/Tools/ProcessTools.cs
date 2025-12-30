using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "list_processes")]
    [Description("List all running processes with basic information including parent PID and command line")]
    public static object ListProcesses(
        [Description("Optional: filter by process name (partial match)")] string? nameFilter = null,
        [Description("Max results to return (default 50)")] int limit = 50)
    {
        var wmiProcesses = new Dictionary<int, (int parentPid, string commandLine)>();
        
        // Use WMI to get parent PID and command line (not available via System.Diagnostics)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    var ppid = Convert.ToInt32(obj["ParentProcessId"] ?? 0);
                    var cmdLine = obj["CommandLine"]?.ToString() ?? "";
                    wmiProcesses[pid] = (ppid, cmdLine);
                }
            }
            catch { /* WMI may fail for some processes */ }
        }

        var processes = Process.GetProcesses()
            .Where(p => string.IsNullOrEmpty(nameFilter) || 
                        p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                wmiProcesses.TryGetValue(p.Id, out var wmiInfo);
                return new
                {
                    pid = p.Id,
                    parentPid = wmiInfo.parentPid,
                    name = p.ProcessName,
                    commandLine = TruncateString(wmiInfo.commandLine, 200),
                    memoryMB = p.WorkingSet64 / 1024 / 1024,
                    threads = p.Threads.Count
                };
            })
            .OrderByDescending(p => p.memoryMB)
            .Take(limit)
            .ToList();

        return new { count = processes.Count, processes };
    }

    [McpServerTool(Name = "get_process_details")]
    [Description("Get detailed information about a specific process including modules, threads, and environment")]
    public static object GetProcessDetails(
        [Description("Process ID to inspect")] int pid,
        [Description("Include loaded modules list")] bool includeModules = false)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            
            // Get WMI info for command line and parent
            int parentPid = 0;
            string commandLine = "";
            string executablePath = "";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT ParentProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE ProcessId = {pid}");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        parentPid = Convert.ToInt32(obj["ParentProcessId"] ?? 0);
                        commandLine = obj["CommandLine"]?.ToString() ?? "";
                        executablePath = obj["ExecutablePath"]?.ToString() ?? "";
                    }
                }
                catch { }
            }

            var result = new Dictionary<string, object>
            {
                ["pid"] = p.Id,
                ["parentPid"] = parentPid,
                ["name"] = p.ProcessName,
                ["commandLine"] = commandLine,
                ["executablePath"] = executablePath,
                ["memoryMB"] = p.WorkingSet64 / 1024 / 1024,
                ["virtualMemoryMB"] = p.VirtualMemorySize64 / 1024 / 1024,
                ["threads"] = p.Threads.Count,
                ["handleCount"] = p.HandleCount,
                ["priorityClass"] = p.PriorityClass.ToString(),
                ["mainWindowTitle"] = p.MainWindowTitle
            };

            try { result["startTime"] = p.StartTime.ToString("o"); } catch { }
            try { result["cpuTimeSeconds"] = p.TotalProcessorTime.TotalSeconds; } catch { }

            if (includeModules)
            {
                try
                {
                    var modules = p.Modules.Cast<ProcessModule>()
                        .Select(m => new { name = m.ModuleName, path = m.FileName })
                        .Take(50)
                        .ToList();
                    result["modules"] = modules;
                    result["moduleCount"] = p.Modules.Count;
                }
                catch (Exception ex)
                {
                    result["modulesError"] = ex.Message;
                }
            }

            return result;
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

    [McpServerTool(Name = "get_process_tree")]
    [Description("Get process tree showing parent-child relationships")]
    public static object GetProcessTree(
        [Description("Optional: root PID to start from (default: show all top-level)")] int? rootPid = null)
    {
        var allProcesses = new Dictionary<int, (string name, int parentPid, long memoryMB)>();
        var children = new Dictionary<int, List<int>>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "Process tree is only supported on Windows" };
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, WorkingSetSize FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var ppid = Convert.ToInt32(obj["ParentProcessId"] ?? 0);
                var name = obj["Name"]?.ToString() ?? "Unknown";
                var memory = Convert.ToInt64(obj["WorkingSetSize"] ?? 0) / 1024 / 1024;
                
                allProcesses[pid] = (name, ppid, memory);
                
                if (!children.ContainsKey(ppid))
                    children[ppid] = new List<int>();
                children[ppid].Add(pid);
            }
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to enumerate processes: {ex.Message}" };
        }

        object BuildTree(int pid, int depth = 0)
        {
            if (!allProcesses.TryGetValue(pid, out var info))
                return new { pid, name = "Unknown" };

            var node = new Dictionary<string, object>
            {
                ["pid"] = pid,
                ["name"] = info.name,
                ["memoryMB"] = info.memoryMB
            };

            if (children.TryGetValue(pid, out var childPids) && childPids.Count > 0 && depth < 5)
            {
                node["children"] = childPids
                    .Where(cpid => allProcesses.ContainsKey(cpid))
                    .Select(cpid => BuildTree(cpid, depth + 1))
                    .ToList();
            }

            return node;
        }

        if (rootPid.HasValue)
        {
            return BuildTree(rootPid.Value);
        }

        // Find processes whose parent doesn't exist (top-level)
        var topLevel = allProcesses
            .Where(kvp => !allProcesses.ContainsKey(kvp.Value.parentPid) || kvp.Value.parentPid == 0)
            .OrderByDescending(kvp => kvp.Value.memoryMB)
            .Take(20)
            .Select(kvp => BuildTree(kvp.Key))
            .ToList();

        return new { count = topLevel.Count, trees = topLevel };
    }

    [McpServerTool(Name = "find_process")]
    [Description("Search for processes by name, command line, or PID")]
    public static object FindProcess(
        [Description("Search query - matches name, command line, or PID")] string query)
    {
        var results = new List<object>();
        var isNumeric = int.TryParse(query, out var pidQuery);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Fallback for non-Windows
            var processes = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           (isNumeric && p.Id == pidQuery))
                .Take(20)
                .Select(p => new { pid = p.Id, name = p.ProcessName, memoryMB = p.WorkingSet64 / 1024 / 1024 })
                .ToList();
            return new { count = processes.Count, matches = processes };
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, WorkingSetSize FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var name = obj["Name"]?.ToString() ?? "";
                var cmdLine = obj["CommandLine"]?.ToString() ?? "";
                var memory = Convert.ToInt64(obj["WorkingSetSize"] ?? 0) / 1024 / 1024;

                var matches = name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             cmdLine.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             (isNumeric && pid == pidQuery);

                if (matches)
                {
                    results.Add(new
                    {
                        pid,
                        name,
                        commandLine = TruncateString(cmdLine, 200),
                        memoryMB = memory
                    });
                }

                if (results.Count >= 20) break;
            }
        }
        catch (Exception ex)
        {
            return new { error = $"Search failed: {ex.Message}" };
        }

        return new { count = results.Count, matches = results };
    }

    private static string TruncateString(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
