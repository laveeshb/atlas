using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

namespace Atlas.Server.Tests;

/// <summary>
/// Fixture that builds test apps and provides utilities for capturing dumps.
/// Shared across test classes to avoid rebuilding apps multiple times.
/// </summary>
public class TestAppFixture : IDisposable
{
    private readonly string _testAppsDir;
    private readonly string _dumpsDir;
    private bool _appsBuilt;

    public string MemoryGrowthAppPath { get; }
    public string CrashingAppPath { get; }
    public string DumpsDirectory => _dumpsDir;

    public TestAppFixture()
    {
        // Find the test apps directory relative to the test assembly
        var assemblyDir = Path.GetDirectoryName(typeof(TestAppFixture).Assembly.Location)!;
        
        // Navigate up to find src/Atlas.Server.Tests/TestApps
        var searchDir = assemblyDir;
        while (searchDir != null && !Directory.Exists(Path.Combine(searchDir, "TestApps")))
        {
            var srcDir = Path.Combine(searchDir, "src", "Atlas.Server.Tests", "TestApps");
            if (Directory.Exists(srcDir))
            {
                searchDir = Path.Combine(searchDir, "src", "Atlas.Server.Tests");
                break;
            }
            searchDir = Path.GetDirectoryName(searchDir);
        }

        _testAppsDir = Path.Combine(searchDir ?? assemblyDir, "TestApps");
        
        // Create a temp directory for dumps
        _dumpsDir = Path.Combine(Path.GetTempPath(), "AtlasTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dumpsDir);

        // Set paths to built executables
        MemoryGrowthAppPath = Path.Combine(_testAppsDir, "MemoryGrowthApp", "bin", "Debug", "net8.0-windows", "MemoryGrowthApp.exe");
        CrashingAppPath = Path.Combine(_testAppsDir, "CrashingApp", "bin", "Debug", "net8.0-windows", "CrashingApp.exe");
    }

    public void EnsureAppsBuilt()
    {
        if (_appsBuilt) return;

        BuildApp(Path.Combine(_testAppsDir, "MemoryGrowthApp", "MemoryGrowthApp.csproj"));
        BuildApp(Path.Combine(_testAppsDir, "CrashingApp", "CrashingApp.csproj"));
        
        _appsBuilt = true;
    }

    private void BuildApp(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Debug --nologo -v q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit(60000);

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            var output = process.StandardOutput.ReadToEnd();
            throw new Exception($"Failed to build {projectPath}:\n{error}\n{output}");
        }
    }

    /// <summary>
    /// Starts the memory growth app and returns the process.
    /// Waits for READY signal before returning.
    /// </summary>
    public Process StartMemoryGrowthApp(int totalMB = 20, int intervalMs = 50)
    {
        EnsureAppsBuilt();

        var psi = new ProcessStartInfo
        {
            FileName = MemoryGrowthAppPath,
            Arguments = $"{totalMB} {intervalMs}",
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)!;
        
        // Wait for READY signal
        var line = process.StandardOutput.ReadLine();
        if (line != "READY")
        {
            process.Kill();
            throw new Exception($"Expected READY, got: {line}");
        }

        return process;
    }

    /// <summary>
    /// Waits for the app to allocate a specific amount of memory.
    /// </summary>
    public void WaitForAllocation(Process process, int targetMB, int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var line = process.StandardOutput.ReadLine();
            if (line == null) break;
            
            if (line.StartsWith("ALLOCATED"))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1].TrimEnd('M', 'B'), out var mb))
                {
                    if (mb >= targetMB) return;
                }
            }
            else if (line == "GROWTH_COMPLETE")
            {
                return;
            }
        }
    }

    /// <summary>
    /// Captures a memory dump of the specified process.
    /// </summary>
    public string CaptureDump(Process process, string dumpName)
    {
        var dumpPath = Path.Combine(_dumpsDir, $"{dumpName}_{DateTime.Now:HHmmss}.dmp");
        
        var client = new DiagnosticsClient(process.Id);
        client.WriteDump(DumpType.WithHeap, dumpPath);
        
        return dumpPath;
    }

    /// <summary>
    /// Stops the process gracefully.
    /// </summary>
    public void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("exit");
                if (!process.WaitForExit(2000))
                {
                    process.Kill();
                }
            }
        }
        catch
        {
            try { process.Kill(); } catch { }
        }
    }

    public void Dispose()
    {
        // Clean up dumps directory
        try
        {
            if (Directory.Exists(_dumpsDir))
            {
                Directory.Delete(_dumpsDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}

/// <summary>
/// Collection definition for sharing TestAppFixture across test classes.
/// </summary>
[CollectionDefinition("TestApps")]
public class TestAppsCollection : ICollectionFixture<TestAppFixture>
{
}
