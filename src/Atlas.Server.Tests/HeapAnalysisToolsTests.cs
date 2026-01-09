using Atlas.Server.Tools;

namespace Atlas.Server.Tests;

/// <summary>
/// Integration tests for heap analysis tools.
/// </summary>
public class HeapAnalysisToolsTests : DumpTestBase
{
    public HeapAnalysisToolsTests(TestAppFixture fixture) : base(fixture) { }

    [Fact]
    public void HeapAnalysisTools_AllOperationsWork()
    {
        // Arrange - capture one dump for all heap analysis operations
        var dumpPath = CaptureTestDump("heap_analysis", targetMB: 5, totalMB: 10);

        // --- DumpHeapStats ---
        var heapStatsResult = HeapAnalysisTools.DumpHeapStats(dumpPath, minCount: 1, limit: 100);
        var heapStatsJson = ToJson(heapStatsResult);
        
        AssertNoError(heapStatsJson, "DumpHeapStats");
        var totalObjects = AssertHasProperty(heapStatsJson, "totalObjects");
        Assert.True(totalObjects.GetInt64() > 0, "DumpHeapStats: Should have objects on heap");
        var totalSize = AssertHasProperty(heapStatsJson, "totalSizeMB");
        Assert.True(totalSize.GetDouble() > 0, "DumpHeapStats: Should have heap size");
        var types = AssertHasProperty(heapStatsJson, "types");
        Assert.True(types.GetArrayLength() > 0, "DumpHeapStats: Should have type statistics");
        var hasBytes = types.EnumerateArray().Any(t => t.GetProperty("type").GetString()?.Contains("Byte[]") == true);
        Assert.True(hasBytes, "DumpHeapStats: Should find Byte[] allocations");

        // --- FindObjects ---
        var findResult = HeapAnalysisTools.FindObjects(dumpPath, "Byte[]", limit: 10);
        var findJson = ToJson(findResult);
        
        AssertNoError(findJson, "FindObjects");
        var totalMatches = AssertHasProperty(findJson, "totalMatches");
        Assert.True(totalMatches.GetInt64() > 0, "FindObjects: Should find byte arrays");
        var objects = AssertHasProperty(findJson, "objects");
        Assert.True(objects.GetArrayLength() > 0, "FindObjects: Should return found objects");
        var firstObj = objects[0];
        AssertHasProperty(firstObj, "address");
        AssertHasProperty(firstObj, "type");
        AssertHasProperty(firstObj, "size");

        // --- DumpObject ---
        var address = firstObj.GetProperty("address").GetString()!;
        var dumpObjResult = HeapAnalysisTools.DumpObject(dumpPath, address);
        var dumpObjJson = ToJson(dumpObjResult);
        
        AssertNoError(dumpObjJson, "DumpObject");
        AssertHasProperty(dumpObjJson, "address");
        AssertHasProperty(dumpObjJson, "type");

        // --- FindStrings ---
        var stringsResult = HeapAnalysisTools.FindStrings(dumpPath, containing: "ALLOCATED", limit: 50);
        var stringsJson = ToJson(stringsResult);
        
        AssertNoError(stringsJson, "FindStrings");
        AssertHasProperty(stringsJson, "strings");
    }
}
