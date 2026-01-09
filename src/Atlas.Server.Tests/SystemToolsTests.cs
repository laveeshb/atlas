using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Tests for system information tools.
/// </summary>
public class SystemToolsTests
{
    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void GetSystemInfo_ReturnsExpectedProperties()
    {
        // Act
        var result = SystemTools.GetSystemInfo();
        var json = ToJson(result);

        // Assert - verify all expected properties exist and have reasonable values
        Assert.True(json.TryGetProperty("machineName", out var machineName));
        Assert.False(string.IsNullOrEmpty(machineName.GetString()), "Should have machine name");

        Assert.True(json.TryGetProperty("osVersion", out var osVersion));
        Assert.Contains("Windows", osVersion.GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.True(json.TryGetProperty("processorCount", out var processorCount));
        Assert.True(processorCount.GetInt32() > 0, "Should have at least 1 processor");

        Assert.True(json.TryGetProperty("is64Bit", out var is64Bit));
        Assert.Equal(Environment.Is64BitOperatingSystem, is64Bit.GetBoolean());

        Assert.True(json.TryGetProperty("systemUptime", out var uptime));
        Assert.False(string.IsNullOrEmpty(uptime.GetString()), "Should have uptime");

        Assert.True(json.TryGetProperty("dotnetVersion", out var dotnetVersion));
        Assert.Contains(".NET", dotnetVersion.GetString());

        Assert.True(json.TryGetProperty("userName", out var userName));
        Assert.Equal(Environment.UserName, userName.GetString());
    }
}
