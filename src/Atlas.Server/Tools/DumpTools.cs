using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class DumpTools
{
    [McpServerTool(Name = "analyze_dump")]
    [Description("Analyze a Windows memory dump file (.dmp) - detects dump type and extracts key information")]
    public static object AnalyzeDump(
        [Description("Full path to the .dmp file")] string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new { error = $"File not found: {filePath}" };
        }

        var fileInfo = new FileInfo(filePath);
        var dumpType = DetectDumpType(filePath);

        // For minidumps, try ClrMD analysis (works for .NET process dumps)
        if (dumpType.type == "minidump")
        {
            return AnalyzeWithClrMD(filePath, fileInfo, dumpType);
        }

        // For kernel/full dumps, provide basic info (ClrMD doesn't support these)
        return new
        {
            filePath,
            fileName = fileInfo.Name,
            sizeMB = fileInfo.Length / 1024.0 / 1024.0,
            sizeBytes = fileInfo.Length,
            created = fileInfo.CreationTime.ToString("o"),
            modified = fileInfo.LastWriteTime.ToString("o"),
            dumpType = dumpType.type,
            dumpTypeDescription = dumpType.description,
            status = "kernel_dump_detected",
            note = "Kernel/full memory dumps require WinDbg for detailed analysis. ClrMD only supports user-mode minidumps."
        };
    }

    private static object AnalyzeWithClrMD(string filePath, FileInfo fileInfo, (string type, string description) dumpType)
    {
        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            
            var clrVersions = dataTarget.ClrVersions.ToList();
            
            if (clrVersions.Count == 0)
            {
                // Native dump (no CLR) - still provide what we can
                return new
                {
                    filePath,
                    fileName = fileInfo.Name,
                    sizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                    sizeBytes = fileInfo.Length,
                    created = fileInfo.CreationTime.ToString("o"),
                    modified = fileInfo.LastWriteTime.ToString("o"),
                    dumpType = dumpType.type,
                    dumpTypeDescription = dumpType.description,
                    architecture = dataTarget.DataReader.Architecture.ToString(),
                    moduleCount = dataTarget.DataReader.EnumerateModules().Count(),
                    modules = dataTarget.DataReader.EnumerateModules()
                        .Take(50)
                        .Select(m => new
                        {
                            name = Path.GetFileName(m.FileName ?? "unknown"),
                            baseAddress = $"0x{m.ImageBase:X}",
                            size = m.IndexFileSize
                        })
                        .ToList(),
                    status = "native_minidump",
                    note = "Native dump (no .NET CLR detected). Module list provided. For full stack analysis use WinDbg."
                };
            }

            // .NET process dump - full ClrMD analysis
            var clrInfo = clrVersions[0];
            
            ClrRuntime? runtime = null;
            List<object>? threads = null;
            List<object>? exceptions = null;
            object? heapInfo = null;
            string? runtimeError = null;

            try
            {
                runtime = clrInfo.CreateRuntime();
                
                threads = runtime.Threads
                    .Where(t => t.IsAlive)
                    .Take(50)
                    .Select(t => (object)new
                    {
                        osThreadId = $"0x{t.OSThreadId:X}",
                        managedThreadId = t.ManagedThreadId,
                        isGC = t.IsGc,
                        isFinalizer = t.IsFinalizer,
                        stackFrameCount = t.EnumerateStackTrace().Count(),
                        stackTrace = t.EnumerateStackTrace()
                            .Take(10)
                            .Select(f => f.ToString())
                            .ToList(),
                        currentException = t.CurrentException?.Type?.Name
                    })
                    .ToList();

                exceptions = runtime.Threads
                    .Where(t => t.CurrentException != null)
                    .Select(t => (object)new
                    {
                        threadId = $"0x{t.OSThreadId:X}",
                        exceptionType = t.CurrentException!.Type?.Name,
                        message = t.CurrentException.Message,
                        hresult = t.CurrentException.HResult
                    })
                    .ToList();

                if (runtime.Heap.CanWalkHeap)
                {
                    var segments = runtime.Heap.Segments.ToList();
                    heapInfo = new
                    {
                        canWalkHeap = true,
                        segmentCount = segments.Count,
                        totalHeapSize = segments.Sum(s => (long)s.Length),
                        ephemeralSegments = segments.Count(s => s.Kind == GCSegmentKind.Ephemeral),
                        gen2Segments = segments.Count(s => s.Kind == GCSegmentKind.Generation2),
                        largeObjectSegments = segments.Count(s => s.Kind == GCSegmentKind.Large),
                        pinnedObjectSegments = segments.Count(s => s.Kind == GCSegmentKind.Pinned)
                    };
                }
                else
                {
                    heapInfo = new { canWalkHeap = false, note = "Heap was in an inconsistent state during dump" };
                }
            }
            catch (Exception ex)
            {
                runtimeError = $"Could not create CLR runtime: {ex.Message}";
            }
            finally
            {
                runtime?.Dispose();
            }

            var modules = dataTarget.DataReader.EnumerateModules()
                .Take(100)
                .Select(m => new
                {
                    name = Path.GetFileName(m.FileName ?? "unknown"),
                    baseAddress = $"0x{m.ImageBase:X}",
                    size = m.IndexFileSize
                })
                .ToList();

            return new
            {
                filePath,
                fileName = fileInfo.Name,
                sizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                sizeBytes = fileInfo.Length,
                created = fileInfo.CreationTime.ToString("o"),
                modified = fileInfo.LastWriteTime.ToString("o"),
                dumpType = dumpType.type,
                dumpTypeDescription = dumpType.description,
                architecture = dataTarget.DataReader.Architecture.ToString(),
                clrVersions = clrVersions.Select(v => new
                {
                    version = v.Version.ToString(),
                    flavor = v.Flavor.ToString()
                }).ToList(),
                threadCount = threads?.Count ?? 0,
                threads,
                exceptionCount = exceptions?.Count ?? 0,
                exceptions,
                heap = heapInfo,
                moduleCount = modules.Count,
                modules,
                runtimeError,
                status = "clrmd_analysis_complete",
                analyzedWith = "Microsoft.Diagnostics.Runtime (ClrMD)"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                filePath,
                fileName = fileInfo.Name,
                sizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                dumpType = dumpType.type,
                error = $"ClrMD analysis failed: {ex.Message}",
                suggestion = "This dump may be corrupted, from an unsupported architecture, or require the matching DAC file."
            };
        }
    }

    private static (string type, string description) DetectDumpType(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[8];
            var bytesRead = fs.Read(header, 0, 8);

            if (bytesRead < 4)
            {
                return ("unknown", "File too small to be a valid dump");
            }

            var signature = Encoding.ASCII.GetString(header, 0, 4);

            // MDMP = Minidump
            if (signature == "MDMP")
            {
                return ("minidump", "Windows Minidump - user mode crash dump");
            }

            // PAGE = Full memory dump
            if (signature == "PAGE")
            {
                return ("full_dump", "Windows Full Memory Dump - complete system memory");
            }

            // DUMP = Kernel dump
            if (signature == "DUMP")
            {
                return ("kernel_dump", "Windows Kernel Dump - kernel memory only");
            }

            return ("unknown", $"Unknown dump format (signature: {signature})");
        }
        catch (Exception ex)
        {
            return ("error", $"Could not read dump header: {ex.Message}");
        }
    }

    [McpServerTool(Name = "list_dumps")]
    [Description("Find and list all .dmp files in a directory")]
    public static object ListDumps(
        [Description("Directory to search (default: common crash dump locations)")] string? directory = null)
    {
        var searchPaths = new List<string>();

        if (!string.IsNullOrEmpty(directory))
        {
            searchPaths.Add(directory);
        }
        else
        {
            // Common Windows dump locations
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            
            searchPaths.AddRange(new[]
            {
                Path.Combine(localAppData, "CrashDumps"),
                Path.Combine(windows, "Minidump"),
                @"C:\Windows\MEMORY.DMP",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            });
        }

        var dumps = new List<object>();

        foreach (var searchPath in searchPaths)
        {
            try
            {
                if (File.Exists(searchPath) && searchPath.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FileInfo(searchPath);
                    dumps.Add(new
                    {
                        path = searchPath,
                        name = fi.Name,
                        sizeMB = Math.Round(fi.Length / 1024.0 / 1024.0, 2),
                        modified = fi.LastWriteTime.ToString("o")
                    });
                }
                else if (Directory.Exists(searchPath))
                {
                    foreach (var file in Directory.EnumerateFiles(searchPath, "*.dmp", SearchOption.TopDirectoryOnly))
                    {
                        var fi = new FileInfo(file);
                        dumps.Add(new
                        {
                            path = file,
                            name = fi.Name,
                            sizeMB = Math.Round(fi.Length / 1024.0 / 1024.0, 2),
                            modified = fi.LastWriteTime.ToString("o")
                        });
                    }
                }
            }
            catch { /* Skip inaccessible paths */ }
        }

        return new
        {
            count = dumps.Count,
            searchedPaths = searchPaths.Where(p => Directory.Exists(p) || File.Exists(p)).ToList(),
            dumps = dumps.OrderByDescending(d => ((dynamic)d).modified).Take(20).ToList()
        };
    }

    [McpServerTool(Name = "collect_dump")]
    [Description("Collect a memory dump from a running .NET process")]
    public static object CollectDump(
        [Description("Process ID to dump")] int processId,
        [Description("Output path for the .dmp file (optional - defaults to temp folder)")] string? outputPath = null,
        [Description("Dump type: 'mini' (smaller, heap only) or 'full' (larger, complete memory). Default: mini")] string dumpType = "mini")
    {
        try
        {
            // Validate process exists
            System.Diagnostics.Process process;
            try
            {
                process = System.Diagnostics.Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return new
                {
                    error = $"Process with ID {processId} not found",
                    suggestion = "Use list_processes to find valid process IDs"
                };
            }

            // Determine output path
            var fileName = $"{process.ProcessName}_{processId}_{DateTime.Now:yyyyMMdd_HHmmss}.dmp";
            var finalPath = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(Path.GetTempPath(), fileName)
                : outputPath.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)
                    ? outputPath
                    : Path.Combine(outputPath, fileName);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Determine dump type
            var writeDumpType = dumpType.ToLowerInvariant() switch
            {
                "full" => DumpType.Full,
                "mini" => DumpType.WithHeap,
                "heap" => DumpType.WithHeap,
                _ => DumpType.WithHeap
            };

            // Collect the dump using DiagnosticsClient
            var client = new DiagnosticsClient(processId);
            client.WriteDump(writeDumpType, finalPath, logDumpGeneration: false);

            var fileInfo = new FileInfo(finalPath);
            return new
            {
                success = true,
                processId,
                processName = process.ProcessName,
                dumpPath = finalPath,
                dumpType = writeDumpType.ToString(),
                sizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                sizeBytes = fileInfo.Length,
                created = fileInfo.CreationTime.ToString("o"),
                suggestion = $"Use analyze_dump with path '{finalPath}' to analyze this dump"
            };
        }
        catch (UnsupportedCommandException)
        {
            return new
            {
                error = "Process does not support dump collection",
                details = "The target process may not be a .NET process or may not have the diagnostic server enabled",
                suggestion = "For native processes, use Task Manager or procdump.exe to collect dumps"
            };
        }
        catch (ServerNotAvailableException)
        {
            return new
            {
                error = "Diagnostic server not available",
                details = "The .NET diagnostic server is not running in the target process",
                suggestion = "Ensure the process is a .NET 5+ application or .NET Core 3.0+ with diagnostics enabled"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                error = "Failed to collect dump",
                details = ex.Message,
                errorType = ex.GetType().Name,
                suggestion = "Ensure you have sufficient permissions and the process is accessible"
            };
        }
    }
}
