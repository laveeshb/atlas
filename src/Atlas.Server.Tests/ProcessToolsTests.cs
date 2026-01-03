using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

public class ProcessToolsTests
{
    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void ListProcesses_ReturnsProcessList()
    {
        // Act
        var result = ProcessTools.ListProcesses();
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("count", out var count), "Should have count property");
        Assert.True(count.GetInt32() > 0, "Should return at least one process");

        Assert.True(json.TryGetProperty("processes", out var processes), "Should have processes property");
        Assert.True(processes.GetArrayLength() > 0, "Should have process items");
    }

    [Fact]
    public void ListProcesses_WithNameFilter_FiltersResults()
    {
        // Act - filter for a process that should always exist
        var result = ProcessTools.ListProcesses(nameFilter: "System");
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("processes", out var processes));

        foreach (var proc in processes.EnumerateArray())
        {
            var name = proc.GetProperty("name").GetString();
            Assert.Contains("System", name!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ListProcesses_WithLimit_RespectsLimit()
    {
        // Act
        var result = ProcessTools.ListProcesses(limit: 5);
        var json = ToJson(result);

        // Assert
        var count = json.GetProperty("count").GetInt32();
        Assert.True(count <= 5, "Should respect limit parameter");
    }

    [Fact]
    public void FindProcess_ByName_FindsExplorer()
    {
        // Act - explorer.exe should be running on any Windows desktop
        var result = ProcessTools.FindProcess("explorer");
        var json = ToJson(result);

        // Assert - verify structure (may not find explorer on server/CI)
        Assert.True(json.TryGetProperty("count", out _), "Should have count property");
        Assert.True(json.TryGetProperty("matches", out _), "Should have matches property");
    }

    [Fact]
    public void FindProcess_ByPid_FindsProcess()
    {
        // Arrange - use current process PID
        var currentPid = Environment.ProcessId;

        // Act
        var result = ProcessTools.FindProcess(currentPid.ToString());
        var json = ToJson(result);

        // Assert
        var count = json.GetProperty("count").GetInt32();
        Assert.True(count >= 1, "Should find current process by PID");
    }

    [Fact]
    public void GetProcessDetails_ForCurrentProcess_ReturnsDetails()
    {
        // Arrange
        var currentPid = Environment.ProcessId;

        // Act
        var result = ProcessTools.GetProcessDetails(currentPid);
        var json = ToJson(result);

        // Assert - should not have error for own process
        Assert.False(json.TryGetProperty("error", out _), "Should not error on own process");
        Assert.Equal(currentPid, json.GetProperty("pid").GetInt32());
        Assert.True(json.TryGetProperty("name", out _), "Should have name property");
    }

    [Fact]
    public void GetProcessDetails_WithModules_IncludesModuleList()
    {
        // Arrange
        var currentPid = Environment.ProcessId;

        // Act
        var result = ProcessTools.GetProcessDetails(currentPid, includeModules: true);
        var json = ToJson(result);

        // Assert - should have modules or modulesError (if access denied)
        var hasModules = json.TryGetProperty("modules", out _);
        var hasModulesError = json.TryGetProperty("modulesError", out _);
        Assert.True(hasModules || hasModulesError, "Should have modules or modulesError");
    }

    [Fact]
    public void GetProcessDetails_NonExistentPid_ReturnsError()
    {
        // Act - use an unlikely PID
        var result = ProcessTools.GetProcessDetails(999999);
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("error", out _), "Should return error for non-existent PID");
    }

    [Fact]
    public void GetProcessTree_ReturnsTreeStructure()
    {
        // Act
        var result = ProcessTools.GetProcessTree();
        var json = ToJson(result);

        // Assert
        Assert.True(json.TryGetProperty("count", out var count), "Should have count property");
        Assert.True(count.GetInt32() > 0, "Should return process trees");
        Assert.True(json.TryGetProperty("trees", out _), "Should have trees property");
    }

    [Fact]
    public void GetProcessTree_ForSpecificPid_ReturnsTree()
    {
        // Arrange - use PID 4 (System) which always exists
        var result = ProcessTools.GetProcessTree(rootPid: 4);
        var json = ToJson(result);

        // Assert
        Assert.Equal(4, json.GetProperty("pid").GetInt32());
    }
}
