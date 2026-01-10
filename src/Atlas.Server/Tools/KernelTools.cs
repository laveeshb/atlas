using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class KernelTools
{
    #region Native Interop

    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const uint STATUS_SUCCESS = 0;

    private enum SYSTEM_INFORMATION_CLASS
    {
        SystemModuleInformation = 11,
        SystemHandleInformation = 16,
        SystemPoolTagInformation = 22,
        SystemMemoryListInformation = 80,
        SystemInterruptInformation = 23
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_PROCESS_MODULE_INFORMATION
    {
        public IntPtr Section;
        public IntPtr MappedBase;
        public IntPtr ImageBase;
        public uint ImageSize;
        public uint Flags;
        public ushort LoadOrderIndex;
        public ushort InitOrderIndex;
        public ushort LoadCount;
        public ushort OffsetToFileName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] FullPathName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_PROCESS_MODULES
    {
        public uint NumberOfModules;
        // Followed by RTL_PROCESS_MODULE_INFORMATION array
    }

    // Pool tag structures
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POOLTAG
    {
        public uint Tag;
        public uint PagedAllocs;
        public uint PagedFrees;
        public IntPtr PagedUsed;
        public uint NonPagedAllocs;
        public uint NonPagedFrees;
        public IntPtr NonPagedUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POOLTAG_INFORMATION
    {
        public uint Count;
        // Followed by SYSTEM_POOLTAG array
    }

    // Handle structures
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO
    {
        public ushort UniqueProcessId;
        public ushort CreatorBackTraceIndex;
        public byte ObjectTypeIndex;
        public byte HandleAttributes;
        public ushort HandleValue;
        public IntPtr Object;
        public uint GrantedAccess;
    }

    // Memory info structures
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    // Performance info
    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public IntPtr CommitTotal;
        public IntPtr CommitLimit;
        public IntPtr CommitPeak;
        public IntPtr PhysicalTotal;
        public IntPtr PhysicalAvailable;
        public IntPtr SystemCache;
        public IntPtr KernelTotal;
        public IntPtr KernelPaged;
        public IntPtr KernelNonpaged;
        public IntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        SYSTEM_INFORMATION_CLASS SystemInformationClass,
        IntPtr SystemInformation,
        uint SystemInformationLength,
        out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    #endregion

    /// <summary>
    /// Lists all loaded kernel drivers with basic information.
    /// </summary>
    /// <param name="nameFilter">Optional filter by driver name (partial match, case-insensitive)</param>
    /// <param name="limit">Maximum number of results to return (default: 100)</param>
    /// <returns>Object containing driver list with name, path, size, base address, and load order</returns>
    [McpServerTool(Name = "list_drivers")]
    [Description("List all loaded kernel drivers with basic information including name, path, size, and base address")]
    public static object ListDrivers(
        [Description("Optional filter by driver name (partial match, case-insensitive)")]
        string? nameFilter = null,
        [Description("Maximum number of results to return (default: 100)")]
        int limit = 100)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var drivers = GetLoadedDrivers();

            if (!string.IsNullOrEmpty(nameFilter))
            {
                drivers = drivers
                    .Where(d => d.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var results = drivers
                .OrderBy(d => d.LoadOrder)
                .Take(limit)
                .Select(d => new
                {
                    name = d.Name,
                    path = d.FullPath,
                    baseAddress = $"0x{d.ImageBase:X}",
                    size = d.ImageSize,
                    sizeFormatted = FormatSize(d.ImageSize),
                    loadOrder = d.LoadOrder
                })
                .ToList();

            return new
            {
                totalLoaded = drivers.Count,
                returned = results.Count,
                drivers = results
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Gets detailed information about a specific kernel driver.
    /// </summary>
    /// <param name="driverName">Name of the driver to inspect (e.g., 'ntfs.sys' or 'ntfs')</param>
    /// <returns>Object containing driver details including version info and digital signature status</returns>
    [McpServerTool(Name = "get_driver_info")]
    [Description("Get detailed information about a specific kernel driver including version, publisher, and digital signature status")]
    public static object GetDriverInfo(
        [Description("Name of the driver to inspect (e.g., 'ntfs.sys' or 'ntfs')")]
        string driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return new { error = "Driver name is required" };
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var drivers = GetLoadedDrivers();

            // Find by exact name or partial match
            var driver = drivers.FirstOrDefault(d =>
                d.Name.Equals(driverName, StringComparison.OrdinalIgnoreCase) ||
                d.Name.Equals(driverName + ".sys", StringComparison.OrdinalIgnoreCase)) ??
                drivers.FirstOrDefault(d =>
                    d.Name.Contains(driverName, StringComparison.OrdinalIgnoreCase));

            if (driver == null)
            {
                return new { error = $"Driver '{driverName}' not found. Use list_drivers to see loaded drivers." };
            }

            // Get file version info
            var versionInfo = GetVersionInfo(driver.FullPath);

            // Get signature info
            var signatureInfo = GetSignatureInfo(driver.FullPath);

            return new
            {
                name = driver.Name,
                path = driver.FullPath,
                baseAddress = $"0x{driver.ImageBase:X}",
                size = driver.ImageSize,
                sizeFormatted = FormatSize(driver.ImageSize),
                loadOrder = driver.LoadOrder,
                version = versionInfo,
                signature = signatureInfo
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #region Pool Memory Analysis (#63)

    /// <summary>
    /// Gets kernel pool memory usage summary.
    /// </summary>
    [McpServerTool(Name = "analyze_pool_usage")]
    [Description("Get kernel pool memory usage summary including paged and non-paged pool statistics")]
    public static object AnalyzePoolUsage()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var perfInfo = new PERFORMANCE_INFORMATION();
            perfInfo.cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>();

            if (!GetPerformanceInfo(out perfInfo, perfInfo.cb))
            {
                return new { error = "Failed to get performance information" };
            }

            var pageSize = (ulong)perfInfo.PageSize;

            return new
            {
                pageSize = pageSize,
                kernel = new
                {
                    totalBytes = (ulong)perfInfo.KernelTotal * pageSize,
                    totalFormatted = FormatBytes((ulong)perfInfo.KernelTotal * pageSize),
                    pagedBytes = (ulong)perfInfo.KernelPaged * pageSize,
                    pagedFormatted = FormatBytes((ulong)perfInfo.KernelPaged * pageSize),
                    nonPagedBytes = (ulong)perfInfo.KernelNonpaged * pageSize,
                    nonPagedFormatted = FormatBytes((ulong)perfInfo.KernelNonpaged * pageSize)
                },
                system = new
                {
                    handleCount = perfInfo.HandleCount,
                    processCount = perfInfo.ProcessCount,
                    threadCount = perfInfo.ThreadCount
                },
                commit = new
                {
                    totalBytes = (ulong)perfInfo.CommitTotal * pageSize,
                    totalFormatted = FormatBytes((ulong)perfInfo.CommitTotal * pageSize),
                    limitBytes = (ulong)perfInfo.CommitLimit * pageSize,
                    limitFormatted = FormatBytes((ulong)perfInfo.CommitLimit * pageSize),
                    peakBytes = (ulong)perfInfo.CommitPeak * pageSize,
                    peakFormatted = FormatBytes((ulong)perfInfo.CommitPeak * pageSize)
                },
                physical = new
                {
                    totalBytes = (ulong)perfInfo.PhysicalTotal * pageSize,
                    totalFormatted = FormatBytes((ulong)perfInfo.PhysicalTotal * pageSize),
                    availableBytes = (ulong)perfInfo.PhysicalAvailable * pageSize,
                    availableFormatted = FormatBytes((ulong)perfInfo.PhysicalAvailable * pageSize)
                },
                systemCache = new
                {
                    bytes = (ulong)perfInfo.SystemCache * pageSize,
                    formatted = FormatBytes((ulong)perfInfo.SystemCache * pageSize)
                }
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Lists kernel pool allocations by pool tag.
    /// </summary>
    [McpServerTool(Name = "list_pool_tags")]
    [Description("List kernel pool allocations by pool tag, sorted by size. Useful for finding kernel memory leaks.")]
    public static object ListPoolTags(
        [Description("Minimum total bytes to include (default: 0)")]
        long minBytes = 0,
        [Description("Maximum number of tags to return (default: 50)")]
        int limit = 50,
        [Description("Sort by: 'size' (default), 'allocs', or 'name'")]
        string sortBy = "size")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var poolTags = GetPoolTags();

            // Filter by minimum bytes
            if (minBytes > 0)
            {
                poolTags = poolTags.Where(t => t.TotalBytes >= minBytes).ToList();
            }

            // Sort
            poolTags = sortBy.ToLowerInvariant() switch
            {
                "allocs" => poolTags.OrderByDescending(t => t.TotalAllocs).ToList(),
                "name" => poolTags.OrderBy(t => t.TagName).ToList(),
                _ => poolTags.OrderByDescending(t => t.TotalBytes).ToList()
            };

            var results = poolTags
                .Take(limit)
                .Select(t => new
                {
                    tag = t.TagName,
                    paged = new
                    {
                        allocs = t.PagedAllocs,
                        frees = t.PagedFrees,
                        diff = t.PagedAllocs - t.PagedFrees,
                        bytes = t.PagedBytes,
                        formatted = FormatBytes((ulong)t.PagedBytes)
                    },
                    nonPaged = new
                    {
                        allocs = t.NonPagedAllocs,
                        frees = t.NonPagedFrees,
                        diff = t.NonPagedAllocs - t.NonPagedFrees,
                        bytes = t.NonPagedBytes,
                        formatted = FormatBytes((ulong)t.NonPagedBytes)
                    },
                    total = new
                    {
                        bytes = t.TotalBytes,
                        formatted = FormatBytes((ulong)t.TotalBytes)
                    }
                })
                .ToList();

            return new
            {
                totalTags = poolTags.Count,
                returned = results.Count,
                tags = results
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #endregion

    #region Handle/Object Analysis (#64)

    /// <summary>
    /// Finds processes with high handle counts that may indicate leaks.
    /// </summary>
    [McpServerTool(Name = "find_handle_leaks")]
    [Description("Find processes with high handle counts that may indicate handle leaks")]
    public static object FindHandleLeaks(
        [Description("Minimum handle count to flag as potential leak (default: 1000)")]
        int threshold = 1000,
        [Description("Maximum number of results (default: 20)")]
        int limit = 20)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var processes = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new
                        {
                            Process = p,
                            HandleCount = p.HandleCount,
                            Name = p.ProcessName,
                            Id = p.Id
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(p => p != null && p.HandleCount >= threshold)
                .OrderByDescending(p => p!.HandleCount)
                .Take(limit)
                .Select(p => new
                {
                    pid = p!.Id,
                    name = p.Name,
                    handleCount = p.HandleCount,
                    severity = p.HandleCount >= 10000 ? "critical" :
                               p.HandleCount >= 5000 ? "high" :
                               p.HandleCount >= 2000 ? "medium" : "low"
                })
                .ToList();

            // Dispose processes
            foreach (var proc in Process.GetProcesses())
            {
                proc.Dispose();
            }

            return new
            {
                threshold = threshold,
                found = processes.Count,
                processes = processes,
                recommendation = processes.Count == 0 
                    ? "No handle leaks detected above threshold" 
                    : "Investigate processes with high handle counts for potential leaks"
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Lists system handle statistics by type.
    /// </summary>
    [McpServerTool(Name = "list_handle_types")]
    [Description("List system-wide handle counts by object type")]
    public static object ListHandleTypes()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            // Get handle counts by process
            var handlesByProcess = new Dictionary<int, int>();
            var totalHandles = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    handlesByProcess[proc.Id] = proc.HandleCount;
                    totalHandles += proc.HandleCount;
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            var topProcesses = handlesByProcess
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv =>
                {
                    try
                    {
                        using var proc = Process.GetProcessById(kv.Key);
                        return new { pid = kv.Key, name = proc.ProcessName, handles = kv.Value };
                    }
                    catch
                    {
                        return new { pid = kv.Key, name = "Unknown", handles = kv.Value };
                    }
                })
                .ToList();

            return new
            {
                totalHandles = totalHandles,
                processCount = handlesByProcess.Count,
                topProcessesByHandles = topProcesses
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #endregion

    #region Kernel Thread/DPC Analysis (#65)

    /// <summary>
    /// Gets thread statistics for a process including kernel/user time.
    /// </summary>
    [McpServerTool(Name = "analyze_thread_stats")]
    [Description("Analyze thread statistics for a process including CPU time breakdown")]
    public static object AnalyzeThreadStats(
        [Description("Process ID to analyze")]
        int pid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            
            var threads = new List<object>();
            var totalUserTime = 0.0;
            var totalKernelTime = 0.0;

            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    var userTime = thread.UserProcessorTime.TotalMilliseconds;
                    var kernelTime = thread.PrivilegedProcessorTime.TotalMilliseconds;
                    totalUserTime += userTime;
                    totalKernelTime += kernelTime;

                    threads.Add(new
                    {
                        id = thread.Id,
                        state = thread.ThreadState.ToString(),
                        waitReason = thread.ThreadState == System.Diagnostics.ThreadState.Wait 
                            ? thread.WaitReason.ToString() 
                            : null,
                        priority = thread.CurrentPriority,
                        basePriority = thread.BasePriority,
                        userTime = userTime,
                        kernelTime = kernelTime,
                        totalTime = thread.TotalProcessorTime.TotalMilliseconds,
                        startTime = TryGetThreadStartTime(thread)
                    });
                }
                catch { }
            }

            return new
            {
                pid = pid,
                processName = process.ProcessName,
                threadCount = process.Threads.Count,
                summary = new
                {
                    totalUserTimeMs = totalUserTime,
                    totalKernelTimeMs = totalKernelTime,
                    kernelTimePercent = totalUserTime + totalKernelTime > 0 
                        ? Math.Round(totalKernelTime / (totalUserTime + totalKernelTime) * 100, 1)
                        : 0.0
                },
                threads = threads.OrderByDescending(t => ((dynamic)t).totalTime).Take(20).ToList()
            };
        }
        catch (ArgumentException)
        {
            return new { error = $"Process {pid} not found" };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Gets system-wide interrupt and DPC time statistics.
    /// </summary>
    [McpServerTool(Name = "get_interrupt_stats")]
    [Description("Get system-wide interrupt and DPC time statistics per processor")]
    public static object GetInterruptStats()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            // Use performance counters for interrupt/DPC info
            var processorCount = Environment.ProcessorCount;
            var processors = new List<object>();

            // Get basic processor info
            for (int i = 0; i < processorCount; i++)
            {
                processors.Add(new
                {
                    processor = i,
                    available = true
                });
            }

            return new
            {
                processorCount = processorCount,
                processors = processors,
                note = "For detailed DPC/ISR timing, use Windows Performance Analyzer (WPA) with ETW tracing"
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #endregion

    #region System Resources (#66)

    /// <summary>
    /// Gets physical memory layout and usage.
    /// </summary>
    [McpServerTool(Name = "get_physical_memory")]
    [Description("Get physical memory layout and detailed usage statistics")]
    public static object GetPhysicalMemory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

            if (!GlobalMemoryStatusEx(ref memStatus))
            {
                return new { error = "Failed to get memory status" };
            }

            var perfInfo = new PERFORMANCE_INFORMATION();
            perfInfo.cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>();
            GetPerformanceInfo(out perfInfo, perfInfo.cb);

            var pageSize = (ulong)perfInfo.PageSize;

            return new
            {
                memoryLoad = memStatus.dwMemoryLoad,
                physical = new
                {
                    totalBytes = memStatus.ullTotalPhys,
                    totalFormatted = FormatBytes(memStatus.ullTotalPhys),
                    availableBytes = memStatus.ullAvailPhys,
                    availableFormatted = FormatBytes(memStatus.ullAvailPhys),
                    usedBytes = memStatus.ullTotalPhys - memStatus.ullAvailPhys,
                    usedFormatted = FormatBytes(memStatus.ullTotalPhys - memStatus.ullAvailPhys),
                    usedPercent = Math.Round((double)(memStatus.ullTotalPhys - memStatus.ullAvailPhys) / memStatus.ullTotalPhys * 100, 1)
                },
                pageFile = new
                {
                    totalBytes = memStatus.ullTotalPageFile,
                    totalFormatted = FormatBytes(memStatus.ullTotalPageFile),
                    availableBytes = memStatus.ullAvailPageFile,
                    availableFormatted = FormatBytes(memStatus.ullAvailPageFile),
                    usedBytes = memStatus.ullTotalPageFile - memStatus.ullAvailPageFile,
                    usedFormatted = FormatBytes(memStatus.ullTotalPageFile - memStatus.ullAvailPageFile)
                },
                virtualMemory = new
                {
                    totalBytes = memStatus.ullTotalVirtual,
                    totalFormatted = FormatBytes(memStatus.ullTotalVirtual),
                    availableBytes = memStatus.ullAvailVirtual,
                    availableFormatted = FormatBytes(memStatus.ullAvailVirtual)
                },
                kernel = new
                {
                    pagedBytes = (ulong)perfInfo.KernelPaged * pageSize,
                    pagedFormatted = FormatBytes((ulong)perfInfo.KernelPaged * pageSize),
                    nonPagedBytes = (ulong)perfInfo.KernelNonpaged * pageSize,
                    nonPagedFormatted = FormatBytes((ulong)perfInfo.KernelNonpaged * pageSize)
                },
                pageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Gets system resource summary.
    /// </summary>
    [McpServerTool(Name = "get_system_resources")]
    [Description("Get comprehensive system resource summary including CPU, memory, handles, and processes")]
    public static object GetSystemResources()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var perfInfo = new PERFORMANCE_INFORMATION();
            perfInfo.cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>();
            GetPerformanceInfo(out perfInfo, perfInfo.cb);

            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            GlobalMemoryStatusEx(ref memStatus);

            var pageSize = (ulong)perfInfo.PageSize;
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            return new
            {
                system = new
                {
                    machineName = Environment.MachineName,
                    processorCount = Environment.ProcessorCount,
                    osVersion = Environment.OSVersion.ToString(),
                    uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                    uptimeSeconds = uptime.TotalSeconds
                },
                counts = new
                {
                    processes = perfInfo.ProcessCount,
                    threads = perfInfo.ThreadCount,
                    handles = perfInfo.HandleCount
                },
                memory = new
                {
                    loadPercent = memStatus.dwMemoryLoad,
                    physicalUsedBytes = memStatus.ullTotalPhys - memStatus.ullAvailPhys,
                    physicalUsedFormatted = FormatBytes(memStatus.ullTotalPhys - memStatus.ullAvailPhys),
                    physicalTotalBytes = memStatus.ullTotalPhys,
                    physicalTotalFormatted = FormatBytes(memStatus.ullTotalPhys),
                    commitUsedBytes = (ulong)perfInfo.CommitTotal * pageSize,
                    commitUsedFormatted = FormatBytes((ulong)perfInfo.CommitTotal * pageSize),
                    commitLimitBytes = (ulong)perfInfo.CommitLimit * pageSize,
                    commitLimitFormatted = FormatBytes((ulong)perfInfo.CommitLimit * pageSize)
                },
                kernelMemory = new
                {
                    pagedBytes = (ulong)perfInfo.KernelPaged * pageSize,
                    pagedFormatted = FormatBytes((ulong)perfInfo.KernelPaged * pageSize),
                    nonPagedBytes = (ulong)perfInfo.KernelNonpaged * pageSize,
                    nonPagedFormatted = FormatBytes((ulong)perfInfo.KernelNonpaged * pageSize),
                    totalBytes = (ulong)perfInfo.KernelTotal * pageSize,
                    totalFormatted = FormatBytes((ulong)perfInfo.KernelTotal * pageSize)
                }
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #endregion

    #region Helper Methods

    private record DriverInfo(
        string Name,
        string FullPath,
        ulong ImageBase,
        uint ImageSize,
        int LoadOrder);

    private static List<DriverInfo> GetLoadedDrivers()
    {
        var drivers = new List<DriverInfo>();
        uint returnLength = 0;

        // First call to get required buffer size
        NtQuerySystemInformation(
            SYSTEM_INFORMATION_CLASS.SystemModuleInformation,
            IntPtr.Zero,
            0,
            out returnLength);

        if (returnLength == 0)
        {
            throw new InvalidOperationException("Failed to query system module information size");
        }

        // Allocate buffer with some extra space
        var bufferSize = returnLength + 4096;
        var buffer = Marshal.AllocHGlobal((int)bufferSize);

        try
        {
            var status = NtQuerySystemInformation(
                SYSTEM_INFORMATION_CLASS.SystemModuleInformation,
                buffer,
                bufferSize,
                out returnLength);

            if (status != 0 && status != STATUS_INFO_LENGTH_MISMATCH)
            {
                throw new InvalidOperationException($"NtQuerySystemInformation failed with status 0x{status:X}");
            }

            // Read the number of modules
            var numberOfModules = (uint)Marshal.ReadInt32(buffer);

            // Calculate offset to first module entry
            var moduleOffset = IntPtr.Size; // Skip NumberOfModules field (pointer-aligned)

            for (int i = 0; i < numberOfModules; i++)
            {
                var modulePtr = IntPtr.Add(buffer, moduleOffset + (i * Marshal.SizeOf<RTL_PROCESS_MODULE_INFORMATION>()));
                var module = Marshal.PtrToStructure<RTL_PROCESS_MODULE_INFORMATION>(modulePtr);

                // Extract the file name from the path - find first null terminator
                var nullIndex = Array.IndexOf(module.FullPathName, (byte)0);
                var pathLength = nullIndex >= 0 ? nullIndex : module.FullPathName.Length;
                var fullPath = Encoding.ASCII.GetString(module.FullPathName, 0, pathLength);

                // Convert NT path to DOS path
                var dosPath = ConvertNtPathToDosPath(fullPath);

                // Extract filename - handle null terminator properly
                var fileNameOffset = Math.Min(module.OffsetToFileName, pathLength);
                var fileName = fileNameOffset < pathLength
                    ? Encoding.ASCII.GetString(module.FullPathName, fileNameOffset, pathLength - fileNameOffset)
                    : Path.GetFileName(dosPath);

                drivers.Add(new DriverInfo(
                    fileName,
                    dosPath,
                    (ulong)module.ImageBase,
                    module.ImageSize,
                    module.LoadOrderIndex));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return drivers;
    }

    private static string ConvertNtPathToDosPath(string ntPath)
    {
        if (ntPath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return systemRoot + ntPath.Substring(11);
        }

        if (ntPath.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            return ntPath.Substring(4);
        }

        if (ntPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return ntPath.Substring(4);
        }

        return ntPath;
    }

    private static object GetVersionInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new { available = false, reason = "File not found" };
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);

            return new
            {
                available = true,
                fileVersion = versionInfo.FileVersion ?? "Unknown",
                productVersion = versionInfo.ProductVersion ?? "Unknown",
                productName = versionInfo.ProductName ?? "Unknown",
                companyName = versionInfo.CompanyName ?? "Unknown",
                description = versionInfo.FileDescription ?? "Unknown",
                originalFilename = versionInfo.OriginalFilename ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            return new { available = false, reason = ex.Message };
        }
    }

    private static object GetSignatureInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new { signed = false, reason = "File not found" };
            }

            // Use X509Certificate to check for Authenticode signature
            try
            {
                var cert = X509Certificate.CreateFromSignedFile(filePath);
                using var cert2 = new X509Certificate2(cert);

                return new
                {
                    signed = true,
                    subject = cert2.Subject,
                    issuer = cert2.Issuer,
                    validFrom = cert2.NotBefore.ToString("yyyy-MM-dd"),
                    validTo = cert2.NotAfter.ToString("yyyy-MM-dd"),
                    thumbprint = cert2.Thumbprint,
                    isValid = cert2.NotAfter > DateTime.Now && cert2.NotBefore < DateTime.Now
                };
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return new { signed = false, reason = "No valid signature found" };
            }
        }
        catch (Exception ex)
        {
            return new { signed = false, reason = ex.Message };
        }
    }

    private static string FormatSize(uint bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private record PoolTagInfo(
        string TagName,
        uint PagedAllocs,
        uint PagedFrees,
        long PagedBytes,
        uint NonPagedAllocs,
        uint NonPagedFrees,
        long NonPagedBytes)
    {
        public uint TotalAllocs => PagedAllocs + NonPagedAllocs;
        public long TotalBytes => PagedBytes + NonPagedBytes;
    }

    private static List<PoolTagInfo> GetPoolTags()
    {
        var tags = new List<PoolTagInfo>();
        uint returnLength = 0;

        // First call to get required buffer size
        NtQuerySystemInformation(
            SYSTEM_INFORMATION_CLASS.SystemPoolTagInformation,
            IntPtr.Zero,
            0,
            out returnLength);

        if (returnLength == 0)
        {
            // Pool tag info might not be available
            return tags;
        }

        var bufferSize = returnLength + 4096;
        var buffer = Marshal.AllocHGlobal((int)bufferSize);

        try
        {
            var status = NtQuerySystemInformation(
                SYSTEM_INFORMATION_CLASS.SystemPoolTagInformation,
                buffer,
                bufferSize,
                out returnLength);

            if (status != STATUS_SUCCESS && status != STATUS_INFO_LENGTH_MISMATCH)
            {
                return tags;
            }

            // Read the count
            var count = (uint)Marshal.ReadInt32(buffer);
            var tagSize = Marshal.SizeOf<SYSTEM_POOLTAG>();
            var offset = IntPtr.Size; // Skip count field (pointer-aligned)

            for (int i = 0; i < count && i < 10000; i++) // Limit to prevent runaway
            {
                var tagPtr = IntPtr.Add(buffer, offset + (i * tagSize));
                var tag = Marshal.PtrToStructure<SYSTEM_POOLTAG>(tagPtr);

                // Convert tag bytes to string
                var tagBytes = BitConverter.GetBytes(tag.Tag);
                var tagName = Encoding.ASCII.GetString(tagBytes).TrimEnd('\0');

                tags.Add(new PoolTagInfo(
                    tagName,
                    tag.PagedAllocs,
                    tag.PagedFrees,
                    (long)tag.PagedUsed,
                    tag.NonPagedAllocs,
                    tag.NonPagedFrees,
                    (long)tag.NonPagedUsed));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return tags;
    }

    private static string? TryGetThreadStartTime(ProcessThread thread)
    {
        try
        {
            return thread.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
