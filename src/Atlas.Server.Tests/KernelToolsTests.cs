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
}
