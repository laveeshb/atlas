using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

public class NetworkToolsTests
{
    private static JsonElement ToJson(object result)
    {
        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void ListNetworkConnections_ReturnsConnectionList()
    {
        // Act
        var result = NetworkTools.ListNetworkConnections();
        var json = ToJson(result);

        // Assert - should have count and connections fields
        Assert.True(json.TryGetProperty("count", out _), "Should have count property");
        Assert.True(json.TryGetProperty("connections", out _), "Should have connections property");
    }

    [Fact]
    public void ListNetworkConnections_ConnectionsHaveExpectedFields()
    {
        // Act
        var result = NetworkTools.ListNetworkConnections();
        var json = ToJson(result);

        // Assert
        var connections = json.GetProperty("connections");

        // Skip if no connections (valid on isolated systems)
        if (connections.GetArrayLength() == 0)
            return;

        var first = connections[0];

        // Verify expected fields exist
        Assert.True(first.TryGetProperty("localAddress", out _), "Should have localAddress");
        Assert.True(first.TryGetProperty("localPort", out _), "Should have localPort");
        Assert.True(first.TryGetProperty("remoteAddress", out _), "Should have remoteAddress");
        Assert.True(first.TryGetProperty("remotePort", out _), "Should have remotePort");
        Assert.True(first.TryGetProperty("state", out _), "Should have state");
        Assert.True(first.TryGetProperty("owningPid", out _), "Should have owningPid");
    }

    [Fact]
    public void ListTcpListeners_ReturnsListenerList()
    {
        // Act
        var result = NetworkTools.ListTcpListeners();
        var json = ToJson(result);

        // Assert - should have count and listeners fields
        Assert.True(json.TryGetProperty("count", out _), "Should have count property");
        Assert.True(json.TryGetProperty("listeners", out _), "Should have listeners property");
    }

    [Fact]
    public void ListTcpListeners_ListenersHaveExpectedFields()
    {
        // Act
        var result = NetworkTools.ListTcpListeners();
        var json = ToJson(result);

        // Assert
        var listeners = json.GetProperty("listeners");

        // Should have at least some listeners on a Windows system
        Assert.True(listeners.GetArrayLength() > 0, "Should have at least one listener");

        var first = listeners[0];

        // Verify expected fields exist
        Assert.True(first.TryGetProperty("address", out _), "Should have address");
        Assert.True(first.TryGetProperty("port", out _), "Should have port");
        Assert.True(first.TryGetProperty("owningPid", out _), "Should have owningPid");
    }

    [Fact]
    public void ListTcpListeners_ContainsListeners()
    {
        // Act
        var result = NetworkTools.ListTcpListeners();
        var json = ToJson(result);

        // Assert - Windows typically has some listeners
        var listeners = json.GetProperty("listeners");
        Assert.True(listeners.GetArrayLength() > 0, "Should have at least one listener");
    }

    [Fact]
    public void ListTcpListeners_ResultsAreSortedByPort()
    {
        // Act
        var result = NetworkTools.ListTcpListeners();
        var json = ToJson(result);

        // Assert
        var listeners = json.GetProperty("listeners");
        var ports = new List<int>();

        foreach (var listener in listeners.EnumerateArray())
        {
            ports.Add(listener.GetProperty("port").GetInt32());
        }

        // Verify sorted
        var sorted = ports.OrderBy(p => p).ToList();
        Assert.Equal(sorted, ports);
    }
}
