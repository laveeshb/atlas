using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Tests for kernel diagnostic tools.
/// </summary>
public class KernelToolsTests
{
    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void ListDrivers_ReturnsLoadedDrivers()
    {
        // Act
        var result = KernelTools.ListDrivers();
        var json = ToJson(result);

        // Assert - should have drivers loaded
        Assert.True(json.TryGetProperty("totalLoaded", out var totalLoaded));
        Assert.True(totalLoaded.GetInt32() > 0, "Should have drivers loaded");

        Assert.True(json.TryGetProperty("drivers", out var drivers));
        Assert.True(drivers.GetArrayLength() > 0, "Should return driver list");

        // Check first driver has expected properties
        var firstDriver = drivers[0];
        Assert.True(firstDriver.TryGetProperty("name", out _));
        Assert.True(firstDriver.TryGetProperty("path", out _));
        Assert.True(firstDriver.TryGetProperty("baseAddress", out _));
        Assert.True(firstDriver.TryGetProperty("size", out _));
        Assert.True(firstDriver.TryGetProperty("loadOrder", out _));
    }

    [Fact]
    public void ListDrivers_WithNameFilter_FiltersResults()
    {
        // Act - filter for ntoskrnl (always present)
        var result = KernelTools.ListDrivers(nameFilter: "ntoskrnl");
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("drivers", out var drivers));
        Assert.True(drivers.GetArrayLength() >= 1, "Should find ntoskrnl");

        foreach (var driver in drivers.EnumerateArray())
        {
            var name = driver.GetProperty("name").GetString()!;
            Assert.Contains("ntoskrnl", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ListDrivers_WithLimit_RespectsLimit()
    {
        // Act
        var result = KernelTools.ListDrivers(limit: 5);
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("drivers", out var drivers));
        Assert.True(drivers.GetArrayLength() <= 5);
    }

    [Fact]
    public void GetDriverInfo_ForNtoskrnl_ReturnsDetails()
    {
        // Act
        var result = KernelTools.GetDriverInfo("ntoskrnl");
        var json = ToJson(result);

        // Assert - should not be an error
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");

        // Check basic properties
        Assert.True(json.TryGetProperty("name", out var name));
        Assert.Contains("ntoskrnl", name.GetString()!, StringComparison.OrdinalIgnoreCase);

        Assert.True(json.TryGetProperty("path", out _));
        Assert.True(json.TryGetProperty("baseAddress", out _));
        Assert.True(json.TryGetProperty("size", out _));

        // Check version info
        Assert.True(json.TryGetProperty("version", out var version));
        Assert.True(version.TryGetProperty("available", out var available));
        Assert.True(available.GetBoolean(), "ntoskrnl should have version info");

        // Check signature info
        Assert.True(json.TryGetProperty("signature", out var signature));
        Assert.True(signature.TryGetProperty("signed", out var signed));
        Assert.True(signed.GetBoolean(), "ntoskrnl should be signed");
    }

    [Fact]
    public void GetDriverInfo_ForNonexistentDriver_ReturnsError()
    {
        // Act
        var result = KernelTools.GetDriverInfo("nonexistent_driver_xyz");
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("error", out var error));
        Assert.Contains("not found", error.GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDriverInfo_WithEmptyName_ReturnsError()
    {
        // Act
        var result = KernelTools.GetDriverInfo("");
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("error", out var error));
        Assert.Contains("required", error.GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListDrivers_WithNoMatchFilter_ReturnsEmptyList()
    {
        // Act
        var result = KernelTools.ListDrivers(nameFilter: "zzz_no_driver_matches_this_zzz");
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("drivers", out var drivers));
        Assert.Equal(0, drivers.GetArrayLength());
        Assert.True(json.TryGetProperty("returned", out var returned));
        Assert.Equal(0, returned.GetInt32());
    }

    [Fact]
    public void ListDrivers_ReturnsSortedByLoadOrder()
    {
        // Act
        var result = KernelTools.ListDrivers(limit: 20);
        var json = ToJson(result);

        // Assert - verify sorted by loadOrder
        var drivers = json.GetProperty("drivers");
        int previousLoadOrder = -1;
        foreach (var driver in drivers.EnumerateArray())
        {
            int loadOrder = driver.GetProperty("loadOrder").GetInt32();
            Assert.True(loadOrder >= previousLoadOrder, 
                $"Drivers should be sorted by loadOrder, but {loadOrder} came after {previousLoadOrder}");
            previousLoadOrder = loadOrder;
        }
    }

    [Fact]
    public void GetDriverInfo_WithAndWithoutSysExtension_BothWork()
    {
        // Act - try "ntfs" without extension
        var result1 = KernelTools.GetDriverInfo("ntfs");
        var json1 = ToJson(result1);

        // Act - try "ntfs.sys" with extension
        var result2 = KernelTools.GetDriverInfo("ntfs.sys");
        var json2 = ToJson(result2);

        // Assert - both should succeed (not return error)
        Assert.False(json1.TryGetProperty("error", out _), "ntfs should be found");
        Assert.False(json2.TryGetProperty("error", out _), "ntfs.sys should be found");

        // Both should return same driver
        Assert.Equal(
            json1.GetProperty("path").GetString(),
            json2.GetProperty("path").GetString());
    }

    #region Pool Memory Analysis Tests (#63)

    [Fact]
    public void AnalyzePoolUsage_ReturnsKernelMemoryStats()
    {
        // Act
        var result = KernelTools.AnalyzePoolUsage();
        var json = ToJson(result);

        // Assert - should not be an error
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");

        // Check kernel memory info
        Assert.True(json.TryGetProperty("kernel", out var kernel));
        Assert.True(kernel.TryGetProperty("totalBytes", out _));
        Assert.True(kernel.TryGetProperty("pagedBytes", out _));
        Assert.True(kernel.TryGetProperty("nonPagedBytes", out _));

        // Check system counts
        Assert.True(json.TryGetProperty("system", out var system));
        Assert.True(system.GetProperty("handleCount").GetUInt32() > 0);
        Assert.True(system.GetProperty("processCount").GetUInt32() > 0);
        Assert.True(system.GetProperty("threadCount").GetUInt32() > 0);
    }

    [Fact]
    public void ListPoolTags_ReturnsPoolTags()
    {
        // Act
        var result = KernelTools.ListPoolTags(limit: 10);
        var json = ToJson(result);

        // Assert - should have tags (may be empty if not available)
        Assert.True(json.TryGetProperty("tags", out var tags));
        Assert.True(json.TryGetProperty("totalTags", out _));

        // If we have tags, check structure
        if (tags.GetArrayLength() > 0)
        {
            var firstTag = tags[0];
            Assert.True(firstTag.TryGetProperty("tag", out _));
            Assert.True(firstTag.TryGetProperty("paged", out _));
            Assert.True(firstTag.TryGetProperty("nonPaged", out _));
            Assert.True(firstTag.TryGetProperty("total", out _));
        }
    }

    [Fact]
    public void ListPoolTags_WithMinBytes_FiltersResults()
    {
        // Act - high threshold should return fewer results
        var result = KernelTools.ListPoolTags(minBytes: 1_000_000, limit: 100);
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("tags", out var tags));
        
        // Each returned tag should have >= 1MB total
        foreach (var tag in tags.EnumerateArray())
        {
            var totalBytes = tag.GetProperty("total").GetProperty("bytes").GetInt64();
            Assert.True(totalBytes >= 1_000_000, $"Tag should have >= 1MB, but has {totalBytes}");
        }
    }

    #endregion

    #region Handle/Object Analysis Tests (#64)

    [Fact]
    public void FindHandleLeaks_ReturnsProcessesAboveThreshold()
    {
        // Act - use a low threshold to ensure we get results
        var result = KernelTools.FindHandleLeaks(threshold: 10, limit: 10);
        var json = ToJson(result);

        // Assert
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");
        Assert.True(json.TryGetProperty("processes", out var processes));
        Assert.True(json.TryGetProperty("threshold", out var threshold));
        Assert.Equal(10, threshold.GetInt32());

        // All returned processes should be above threshold
        foreach (var proc in processes.EnumerateArray())
        {
            Assert.True(proc.GetProperty("handleCount").GetInt32() >= 10);
            Assert.True(proc.TryGetProperty("pid", out _));
            Assert.True(proc.TryGetProperty("name", out _));
            Assert.True(proc.TryGetProperty("severity", out _));
        }
    }

    [Fact]
    public void FindHandleLeaks_WithHighThreshold_MayReturnEmpty()
    {
        // Act - very high threshold
        var result = KernelTools.FindHandleLeaks(threshold: 1_000_000);
        var json = ToJson(result);

        // Assert - should succeed but likely be empty
        Assert.False(json.TryGetProperty("error", out _));
        Assert.True(json.TryGetProperty("processes", out var processes));
        Assert.Equal(0, processes.GetArrayLength());
    }

    [Fact]
    public void ListHandleTypes_ReturnsSystemHandleStats()
    {
        // Act
        var result = KernelTools.ListHandleTypes();
        var json = ToJson(result);

        // Assert
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");
        Assert.True(json.TryGetProperty("totalHandles", out var totalHandles));
        Assert.True(totalHandles.GetInt32() > 0, "Should have handles");
        Assert.True(json.TryGetProperty("processCount", out var processCount));
        Assert.True(processCount.GetInt32() > 0);
        Assert.True(json.TryGetProperty("topProcessesByHandles", out var topProcesses));
        Assert.True(topProcesses.GetArrayLength() > 0);
    }

    #endregion

    #region Kernel Thread Analysis Tests (#65)

    [Fact]
    public void AnalyzeThreadStats_ForSystemProcess_ReturnsThreadInfo()
    {
        // Act - analyze a system process that's always accessible (like the idle process or explorer)
        // We'll use Process.GetCurrentProcess which is always accessible
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var result = KernelTools.AnalyzeThreadStats(currentProcess.Id);
        var json = ToJson(result);

        // If we get an error, it might be access denied - that's OK for some processes
        if (json.TryGetProperty("error", out var error))
        {
            // Access denied is acceptable for some system processes
            Assert.Contains("Access", error.GetString()!, StringComparison.OrdinalIgnoreCase);
            return;
        }

        // If no error, check structure
        Assert.True(json.TryGetProperty("pid", out _));
        Assert.True(json.TryGetProperty("threadCount", out var threadCount));
        Assert.True(threadCount.GetInt32() > 0);
    }

    [Fact]
    public void AnalyzeThreadStats_ForNonexistentProcess_ReturnsError()
    {
        // Act
        var result = KernelTools.AnalyzeThreadStats(999999);
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("error", out var error));
        Assert.Contains("not found", error.GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetInterruptStats_ReturnsProcessorInfo()
    {
        // Act
        var result = KernelTools.GetInterruptStats();
        var json = ToJson(result);

        // Assert
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");
        Assert.True(json.TryGetProperty("processorCount", out var processorCount));
        Assert.Equal(Environment.ProcessorCount, processorCount.GetInt32());
    }

    #endregion

    #region System Resources Tests (#66)

    [Fact]
    public void GetPhysicalMemory_ReturnsMemoryStats()
    {
        // Act
        var result = KernelTools.GetPhysicalMemory();
        var json = ToJson(result);

        // Assert
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");
        
        // Check physical memory
        Assert.True(json.TryGetProperty("physical", out var physical));
        Assert.True(physical.GetProperty("totalBytes").GetUInt64() > 0);
        Assert.True(physical.GetProperty("availableBytes").GetUInt64() > 0);

        // Check page file
        Assert.True(json.TryGetProperty("pageFile", out var pageFile));
        Assert.True(pageFile.GetProperty("totalBytes").GetUInt64() > 0);

        // Check kernel memory
        Assert.True(json.TryGetProperty("kernel", out var kernel));
        Assert.True(kernel.TryGetProperty("pagedBytes", out _));
        Assert.True(kernel.TryGetProperty("nonPagedBytes", out _));
    }

    [Fact]
    public void GetSystemResources_ReturnsComprehensiveStats()
    {
        // Act
        var result = KernelTools.GetSystemResources();
        var json = ToJson(result);

        // Assert
        Assert.False(json.TryGetProperty("error", out _), "Should not return error");

        // Check system info
        Assert.True(json.TryGetProperty("system", out var system));
        Assert.Equal(Environment.MachineName, system.GetProperty("machineName").GetString());
        Assert.Equal(Environment.ProcessorCount, system.GetProperty("processorCount").GetInt32());

        // Check counts
        Assert.True(json.TryGetProperty("counts", out var counts));
        Assert.True(counts.GetProperty("processes").GetUInt32() > 0);
        Assert.True(counts.GetProperty("threads").GetUInt32() > 0);
        Assert.True(counts.GetProperty("handles").GetUInt32() > 0);

        // Check memory
        Assert.True(json.TryGetProperty("memory", out var memory));
        Assert.True(memory.GetProperty("physicalTotalBytes").GetUInt64() > 0);

        // Check kernel memory
        Assert.True(json.TryGetProperty("kernelMemory", out var kernelMemory));
        Assert.True(kernelMemory.TryGetProperty("pagedBytes", out _));
    }

    #endregion
}
