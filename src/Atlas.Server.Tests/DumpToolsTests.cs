using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Integration tests for dump management tools.
/// </summary>
public class DumpToolsTests : DumpTestBase
{
    public DumpToolsTests(TestAppFixture fixture) : base(fixture) { }

    [Fact]
    public void DumpTools_AllOperationsWork()
    {
        // Arrange - capture one dump for all operations
        var dumpPath = CaptureTestDump("dump_tools", targetMB: 5, totalMB: 10);
        var dumpDir = Path.GetDirectoryName(dumpPath)!;

        // --- AnalyzeDump ---
        var analyzeResult = DumpTools.AnalyzeDump(dumpPath);
        var analyzeJson = ToJson(analyzeResult);
        
        AssertNoError(analyzeJson, "AnalyzeDump");
        var dumpType = AssertHasProperty(analyzeJson, "dumpType");
        Assert.Equal("minidump", dumpType.GetString());
        var status = AssertHasProperty(analyzeJson, "status");
        Assert.Contains("clrmd", status.GetString(), StringComparison.OrdinalIgnoreCase);
        var clrVersions = AssertHasProperty(analyzeJson, "clrVersions");
        Assert.True(clrVersions.GetArrayLength() > 0, "AnalyzeDump: Should detect CLR");
        AssertHasProperty(analyzeJson, "fileName");
        var size = AssertHasProperty(analyzeJson, "sizeMB");
        Assert.True(size.GetDouble() > 0, "AnalyzeDump: Should have size");
        AssertHasProperty(analyzeJson, "architecture");
        var threadCount = AssertHasProperty(analyzeJson, "threadCount");
        Assert.True(threadCount.GetInt32() > 0, "AnalyzeDump: Should have threads");
        var heap = AssertHasProperty(analyzeJson, "heap");
        AssertHasProperty(heap, "canWalkHeap");
        var modules = AssertHasProperty(analyzeJson, "modules");
        Assert.True(modules.GetArrayLength() > 0, "AnalyzeDump: Should have loaded modules");

        // --- ListDumps ---
        var listResult = DumpTools.ListDumps(dumpDir);
        var listJson = ToJson(listResult);
        
        AssertNoError(listJson, "ListDumps");
        Assert.True(listJson.TryGetProperty("dumps", out var dumps) || listJson.TryGetProperty("files", out dumps),
            "ListDumps: Should have dumps or files property");
        Assert.True(dumps.GetArrayLength() > 0, "ListDumps: Should find dump files");
        
        var found = dumps.EnumerateArray().Any(dump =>
        {
            string? name = null;
            if (dump.TryGetProperty("fileName", out var fn)) name = fn.GetString();
            else if (dump.TryGetProperty("name", out fn)) name = fn.GetString();
            else if (dump.ValueKind == JsonValueKind.String) name = dump.GetString();
            return name?.Contains("dump_tools") == true;
        });
        Assert.True(found, "ListDumps: Should find our created dump file");
    }

    [Fact]
    public void AnalyzeDump_WithNonExistentFile_ReturnsError()
    {
        var result = DumpTools.AnalyzeDump(@"C:\nonexistent\fake.dmp");
        var json = ToJson(result);

        Assert.True(json.TryGetProperty("error", out var error));
        Assert.Contains("not found", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListDumps_InNonExistentDirectory_HandlesGracefully()
    {
        var result = DumpTools.ListDumps(@"C:\definitely\does\not\exist\12345");
        var json = ToJson(result);

        // Should not crash - may return empty or error
        if (!json.TryGetProperty("error", out _))
        {
            if (json.TryGetProperty("dumps", out var dumps) || json.TryGetProperty("files", out dumps))
            {
                Assert.True(dumps.GetArrayLength() == 0);
            }
        }
    }
}
