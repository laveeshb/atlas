using Atlas.Server.Tools;

namespace Atlas.Server.Tests;

/// <summary>
/// Integration tests for memory diagnostic tools.
/// </summary>
public class MemoryDiagnosticToolsTests : DumpTestBase
{
    public MemoryDiagnosticToolsTests(TestAppFixture fixture) : base(fixture) { }

    [Fact]
    public void CompareHeaps_DetectsMemoryGrowth()
    {
        // This test needs two dumps at different allocation points
        var (baselinePath, comparisonPath) = CaptureComparisonDumps("compare", baselineMB: 5, comparisonMB: 15);

        var result = MemoryDiagnosticTools.CompareHeaps(
            baselinePath, 
            comparisonPath, 
            minSizeDelta: 1024 * 100,
            limit: 50);
        var json = ToJson(result);

        AssertNoError(json);
        
        var growth = AssertHasProperty(json, "totalGrowthMB");
        Assert.True(growth.GetDouble() > 0, "Should detect positive memory growth");
        
        var baseline = AssertHasProperty(json, "baseline");
        var comparison = AssertHasProperty(json, "comparison");
        Assert.True(comparison.GetProperty("totalSizeMB").GetDouble() > baseline.GetProperty("totalSizeMB").GetDouble(),
            "Comparison should be larger than baseline");
        
        var deltas = AssertHasProperty(json, "deltas");
        var foundByteGrowth = deltas.EnumerateArray().Any(delta =>
        {
            var typeName = delta.GetProperty("type").GetString();
            return typeName?.Contains("Byte[]") == true && 
                   delta.GetProperty("sizeDeltaBytes").GetInt64() > 0;
        });
        Assert.True(foundByteGrowth, "Should detect Byte[] growth from test app");
    }

    [Fact]
    public void MemoryDiagnosticTools_SingleDumpOperationsWork()
    {
        // Arrange - capture one dump for all single-dump memory operations
        var dumpPath = CaptureTestDump("memory_diag");

        // --- LargeObjects ---
        var lohResult = MemoryDiagnosticTools.LargeObjects(dumpPath, limit: 50);
        var lohJson = ToJson(lohResult);
        
        AssertNoError(lohJson, "LargeObjects");
        var lohObjects = AssertHasProperty(lohJson, "objects");
        Assert.True(lohObjects.GetArrayLength() > 0, "LargeObjects: Should find 1MB byte[] allocations");
        Assert.True(lohObjects[0].GetProperty("sizeBytes").GetUInt64() >= 85000, 
            "LargeObjects: Objects should be >85KB");

        // --- PinnedObjects ---
        var pinnedResult = MemoryDiagnosticTools.PinnedObjects(dumpPath, limit: 20);
        var pinnedJson = ToJson(pinnedResult);
        
        AssertNoError(pinnedJson, "PinnedObjects");
        Assert.True(pinnedJson.TryGetProperty("objects", out _) || pinnedJson.TryGetProperty("count", out _), 
            "PinnedObjects: Should have objects or count property");

        // --- DuplicateStrings ---
        var dupeResult = MemoryDiagnosticTools.DuplicateStrings(dumpPath, minCount: 2, limit: 20);
        var dupeJson = ToJson(dupeResult);
        
        AssertNoError(dupeJson, "DuplicateStrings");
        Assert.True(dupeJson.TryGetProperty("duplicates", out _) || dupeJson.TryGetProperty("count", out _), 
            "DuplicateStrings: Should have duplicates or count property");

        // --- FinalizerQueue ---
        var finalizerResult = MemoryDiagnosticTools.FinalizerQueue(dumpPath, limit: 20);
        var finalizerJson = ToJson(finalizerResult);
        
        AssertNoError(finalizerJson, "FinalizerQueue");
    }
}
