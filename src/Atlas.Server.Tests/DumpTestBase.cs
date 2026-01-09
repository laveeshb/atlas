using System.Diagnostics;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Base class for dump-based integration tests.
/// Provides shared infrastructure for capturing dumps and asserting on JSON results.
/// </summary>
[Collection("TestApps")]
public abstract class DumpTestBase : IDisposable
{
    protected readonly TestAppFixture Fixture;
    private readonly List<string> _dumpFiles = new();

    protected DumpTestBase(TestAppFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Converts a tool result object to JsonElement for assertion.
    /// </summary>
    protected static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Captures a dump from the memory growth app at the specified allocation point.
    /// Handles process lifecycle and tracks dump for cleanup.
    /// </summary>
    /// <param name="dumpName">Name prefix for the dump file</param>
    /// <param name="targetMB">Memory allocation to wait for before capturing</param>
    /// <param name="totalMB">Total memory the app should allocate</param>
    /// <param name="intervalMs">Interval between allocations</param>
    /// <returns>Path to the captured dump file</returns>
    protected string CaptureTestDump(string dumpName, int targetMB = 3, int totalMB = 5, int intervalMs = 20)
    {
        using var process = Fixture.StartMemoryGrowthApp(totalMB: totalMB, intervalMs: intervalMs);
        Fixture.WaitForAllocation(process, targetMB: targetMB);
        
        var dumpPath = Fixture.CaptureDump(process, dumpName);
        _dumpFiles.Add(dumpPath);
        Fixture.StopProcess(process);
        
        return dumpPath;
    }

    /// <summary>
    /// Captures two dumps at different allocation points for comparison tests.
    /// </summary>
    protected (string baseline, string comparison) CaptureComparisonDumps(
        string baseName,
        int baselineMB = 5,
        int comparisonMB = 15,
        int totalMB = 20,
        int intervalMs = 30)
    {
        using var process = Fixture.StartMemoryGrowthApp(totalMB: totalMB, intervalMs: intervalMs);
        
        Fixture.WaitForAllocation(process, targetMB: baselineMB);
        var baselinePath = Fixture.CaptureDump(process, $"{baseName}_baseline");
        _dumpFiles.Add(baselinePath);
        
        Fixture.WaitForAllocation(process, targetMB: comparisonMB);
        var comparisonPath = Fixture.CaptureDump(process, $"{baseName}_comparison");
        _dumpFiles.Add(comparisonPath);
        
        Fixture.StopProcess(process);
        
        return (baselinePath, comparisonPath);
    }

    /// <summary>
    /// Tracks a dump file for cleanup without capturing it.
    /// </summary>
    protected void TrackDumpForCleanup(string dumpPath)
    {
        _dumpFiles.Add(dumpPath);
    }

    /// <summary>
    /// Asserts that the result has no error property.
    /// </summary>
    protected static void AssertNoError(JsonElement json, string context = "")
    {
        Assert.False(json.TryGetProperty("error", out var err), 
            $"Should not error{(context.Length > 0 ? $" ({context})" : "")}: {err}");
    }

    /// <summary>
    /// Asserts that a property exists and returns its value.
    /// </summary>
    protected static JsonElement AssertHasProperty(JsonElement json, string propertyName, string? message = null)
    {
        Assert.True(json.TryGetProperty(propertyName, out var value), 
            message ?? $"Should have '{propertyName}' property");
        return value;
    }

    public void Dispose()
    {
        foreach (var file in _dumpFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }
}
