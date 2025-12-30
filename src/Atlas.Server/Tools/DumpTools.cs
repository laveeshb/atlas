using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class DumpTools
{
    // P/Invoke declarations for Rust atlas-dump-core library
    private const string DumpCoreDll = "atlas-dump-core.dll";

    [DllImport(DumpCoreDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr analyze_dump(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath);

    [DllImport(DumpCoreDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void free_result(IntPtr result);

    private static bool _rustLibraryAvailable = false;
    private static bool _rustLibraryChecked = false;

    private static bool IsRustLibraryAvailable()
    {
        if (!_rustLibraryChecked)
        {
            _rustLibraryChecked = true;
            // Check if the DLL exists in common locations
            var searchPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, DumpCoreDll),
                Path.Combine(AppContext.BaseDirectory, "native", DumpCoreDll),
                DumpCoreDll
            };
            _rustLibraryAvailable = searchPaths.Any(File.Exists);
        }
        return _rustLibraryAvailable;
    }

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
        
        // Try Rust library first
        if (IsRustLibraryAvailable())
        {
            try
            {
                var resultPtr = analyze_dump(filePath);
                if (resultPtr != IntPtr.Zero)
                {
                    try
                    {
                        var json = Marshal.PtrToStringUTF8(resultPtr);
                        free_result(resultPtr);
                        
                        if (!string.IsNullOrEmpty(json))
                        {
                            return JsonSerializer.Deserialize<object>(json) 
                                ?? new { error = "Failed to parse Rust response" };
                        }
                    }
                    catch (Exception ex)
                    {
                        return new { error = $"Failed to process Rust response: {ex.Message}" };
                    }
                }
            }
            catch (DllNotFoundException)
            {
                _rustLibraryAvailable = false;
            }
            catch (Exception ex)
            {
                return new { error = $"Rust library error: {ex.Message}" };
            }
        }

        // Fallback: Basic C# analysis (header detection only)
        return AnalyzeDumpBasic(filePath, fileInfo);
    }

    private static object AnalyzeDumpBasic(string filePath, FileInfo fileInfo)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[8];
            var bytesRead = fs.Read(header, 0, 8);

            if (bytesRead < 4)
            {
                return new { error = "File too small to be a valid dump" };
            }

            var signature = Encoding.ASCII.GetString(header, 0, 4);
            var dumpType = DetectDumpType(header, signature);

            return new
            {
                filePath,
                fileName = fileInfo.Name,
                sizeMB = fileInfo.Length / 1024 / 1024,
                sizeBytes = fileInfo.Length,
                created = fileInfo.CreationTime.ToString("o"),
                modified = fileInfo.LastWriteTime.ToString("o"),
                dumpType = dumpType.type,
                dumpTypeDescription = dumpType.description,
                signature = BitConverter.ToString(header[..4]),
                status = "basic_analysis",
                note = "Full analysis requires Rust atlas-dump-core library. Run 'cargo build --release' in rust/atlas-dump-core/"
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Failed to read dump file: {ex.Message}" };
        }
    }

    private static (string type, string description) DetectDumpType(byte[] header, string signature)
    {
        // Check for MDMP (Minidump) - "MDMP" signature
        if (signature == "MDMP")
        {
            return ("minidump", "Windows Minidump - contains limited crash information");
        }

        // Check for full/kernel dump - "PAGE" or "DUMP" signature
        if (signature == "PAGE")
        {
            return ("full_dump", "Windows Full Memory Dump - complete system memory");
        }

        if (signature == "DUMP")
        {
            return ("kernel_dump", "Windows Kernel Dump - kernel memory only");
        }

        // Check for DMP header with different versions
        if (header[0] == 0x4D && header[1] == 0x44) // "MD"
        {
            return ("minidump_variant", "Minidump variant");
        }

        return ("unknown", $"Unknown dump format (signature: {signature})");
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
                        sizeMB = fi.Length / 1024 / 1024,
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
                            sizeMB = fi.Length / 1024 / 1024,
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
            searchedPaths = searchPaths.Where(Directory.Exists).ToList(),
            dumps = dumps.OrderByDescending(d => ((dynamic)d).modified).Take(20).ToList()
        };
    }
}
