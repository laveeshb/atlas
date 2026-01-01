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
        [Description("Max results to return (default 50)")] int limit = 50,
        [Description("Optional: remote machine name (e.g., 'SERVER01' or '192.168.1.100')")] string? hostname = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only supported on Windows" };
        }

        try
        {
            var scope = GetManagementScope(hostname);
            var processes = new List<object>();

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT ProcessId, ParentProcessId, Name, CommandLine, WorkingSetSize, ThreadCount FROM Win32_Process"));

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(nameFilter) &&
                    !name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                processes.Add(new
                {
                    pid = Convert.ToInt32(obj["ProcessId"]),
                    parentPid = Convert.ToInt32(obj["ParentProcessId"] ?? 0),
                    name,
                    commandLine = TruncateString(obj["CommandLine"]?.ToString(), 200),
                    memoryMB = Convert.ToInt64(obj["WorkingSetSize"] ?? 0) / 1024 / 1024,
                    threads = Convert.ToInt32(obj["ThreadCount"] ?? 0)
                });
            }

            var result = processes
                .OrderByDescending(p => ((dynamic)p).memoryMB)
                .Take(limit)
                .ToList();

            return new
            {
                count = result.Count,
                hostname = hostname ?? Environment.MachineName,
                isRemote = !string.IsNullOrEmpty(hostname),
                processes = result
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to list processes: {ex.Message}", hostname };
        }
    }

    [McpServerTool(Name = "get_process_details")]
    [Description("Get detailed information about a specific process including modules, threads, and environment")]
    public static object GetProcessDetails(
        [Description("Process ID to inspect")] int pid,
        [Description("Include loaded modules list")] bool includeModules = false,
        [Description("Optional: remote machine name (e.g., 'SERVER01' or '192.168.1.100')")] string? hostname = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only supported on Windows" };
        }

        try
        {
            var scope = GetManagementScope(hostname);
            var isRemote = !string.IsNullOrEmpty(hostname);

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM Win32_Process WHERE ProcessId = {pid}"));

            ManagementObject? processObj = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                processObj = obj;
                break;
            }

            if (processObj == null)
                return new { error = $"Process with PID {pid} not found", hostname };

            var result = new Dictionary<string, object>
            {
                ["pid"] = pid,
                ["parentPid"] = Convert.ToInt32(processObj["ParentProcessId"] ?? 0),
                ["name"] = processObj["Name"]?.ToString() ?? "",
                ["commandLine"] = processObj["CommandLine"]?.ToString() ?? "",
                ["executablePath"] = processObj["ExecutablePath"]?.ToString() ?? "",
                ["memoryMB"] = Convert.ToInt64(processObj["WorkingSetSize"] ?? 0) / 1024 / 1024,
                ["virtualMemoryMB"] = Convert.ToInt64(processObj["VirtualSize"] ?? 0) / 1024 / 1024,
                ["threads"] = Convert.ToInt32(processObj["ThreadCount"] ?? 0),
                ["handleCount"] = Convert.ToInt32(processObj["HandleCount"] ?? 0),
                ["priority"] = Convert.ToInt32(processObj["Priority"] ?? 0),
                ["hostname"] = hostname ?? Environment.MachineName,
                ["isRemote"] = isRemote
            };

            var creationDate = processObj["CreationDate"]?.ToString();
            if (!string.IsNullOrEmpty(creationDate))
            {
                result["startTime"] = ManagementDateTimeConverter.ToDateTime(creationDate).ToString("o");
            }

            // For local processes, we can get additional info via System.Diagnostics
            if (!isRemote)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    result["mainWindowTitle"] = p.MainWindowTitle;
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
                }
                catch { }
            }

            return result;
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to get process details: {ex.Message}", hostname };
        }
    }

    [McpServerTool(Name = "get_process_tree")]
    [Description("Get process tree showing parent-child relationships")]
    public static object GetProcessTree(
        [Description("Optional: root PID to start from (default: show all top-level)")] int? rootPid = null,
        [Description("Optional: remote machine name (e.g., 'SERVER01' or '192.168.1.100')")] string? hostname = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "Process tree is only supported on Windows" };
        }

        var allProcesses = new Dictionary<int, (string name, int parentPid, long memoryMB)>();
        var children = new Dictionary<int, List<int>>();

        try
        {
            var scope = GetManagementScope(hostname);
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT ProcessId, ParentProcessId, Name, WorkingSetSize FROM Win32_Process"));

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
            return new { error = $"Failed to enumerate processes: {ex.Message}", hostname };
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

        return new
        {
            count = topLevel.Count,
            hostname = hostname ?? Environment.MachineName,
            isRemote = !string.IsNullOrEmpty(hostname),
            trees = topLevel
        };
    }

    [McpServerTool(Name = "find_process")]
    [Description("Search for processes by name, command line, or PID")]
    public static object FindProcess(
        [Description("Search query - matches name, command line, or PID")] string query,
        [Description("Optional: remote machine name (e.g., 'SERVER01' or '192.168.1.100')")] string? hostname = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only supported on Windows" };
        }

        var results = new List<object>();
        var isNumeric = int.TryParse(query, out var pidQuery);

        try
        {
            var scope = GetManagementScope(hostname);
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT ProcessId, Name, CommandLine, WorkingSetSize FROM Win32_Process"));

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

            return new
            {
                count = results.Count,
                hostname = hostname ?? Environment.MachineName,
                isRemote = !string.IsNullOrEmpty(hostname),
                matches = results
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Search failed: {ex.Message}", hostname };
        }
    }

    private static ManagementScope GetManagementScope(string? hostname)
    {
        if (string.IsNullOrEmpty(hostname))
        {
            return new ManagementScope(@"\\.\root\cimv2");
        }

        var path = $@"\\{hostname}\root\cimv2";
        var scope = new ManagementScope(path);
        scope.Connect();
        return scope;
    }

    private static string TruncateString(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
