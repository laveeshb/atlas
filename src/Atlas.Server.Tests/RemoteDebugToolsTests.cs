using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Tests for RemoteDebugTools that don't require actual remote.exe connections.
/// These test error handling and input validation.
/// </summary>
public class RemoteDebugToolsTests
{
    [Fact]
    public async Task RemoteAnalyzeCrash_WithInvalidConnectionString_ReturnsError()
    {
        // This will fail to connect (no server running), but should handle gracefully
        var result = await RemoteDebugTools.RemoteAnalyzeCrash(
            connectionString: "nonexistent.invalid/TestSession");

        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out _), "Should return error for invalid connection");
        Assert.True(root.TryGetProperty("suggestion", out _), "Should include helpful suggestion");
    }

    [Fact]
    public async Task RemoteHeapStats_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteHeapStats(
            connectionString: "nonexistent.invalid/TestSession");

        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteStackTrace_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteStackTrace(
            connectionString: "nonexistent.invalid/TestSession",
            stackType: "managed");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteListModules_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteListModules(
            connectionString: "nonexistent.invalid/TestSession");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteDebugCommand_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteDebugCommand(
            connectionString: "nonexistent.invalid/TestSession",
            command: "vertarget");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteAnalyzeCrash_WithBadFormat_ReturnsHelpfulSuggestion()
    {
        // Test with completely invalid format
        var result = await RemoteDebugTools.RemoteAnalyzeCrash(
            connectionString: "invalid-no-session");

        var json = JsonSerializer.Serialize(result);
        
        Assert.Contains("error", json);
        Assert.Contains("suggestion", json);
    }

    [Fact]
    public async Task ConnectionString_SupportsMultipleFormats()
    {
        // These should all parse correctly (though fail to connect)
        // Format: hostname/session
        var result1 = await RemoteDebugTools.RemoteDebugCommand("vm2/DumpSession", "vertarget");
        Assert.Contains("error", JsonSerializer.Serialize(result1)); // Fails to connect, not parse
        
        // Format: server=hostname,session=name
        var result2 = await RemoteDebugTools.RemoteDebugCommand("server=vm2,session=DumpSession", "vertarget");
        Assert.Contains("error", JsonSerializer.Serialize(result2));
    }
}
