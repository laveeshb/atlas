using Atlas.Server.Tools;
using System.Text.Json;

namespace Atlas.Server.Tests;

/// <summary>
/// Tests for RemoteDebugTools that don't require actual cdb.exe/dbgsrv.
/// These test error handling and input validation.
/// </summary>
public class RemoteDebugToolsTests
{
    [Fact]
    public async Task RemoteAnalyzeCrash_WithInvalidConnectionString_ReturnsError()
    {
        // This will fail to connect (no server running), but should handle gracefully
        var result = await RemoteDebugTools.RemoteAnalyzeCrash(
            connectionString: "tcp:server=nonexistent.invalid,port=99999",
            dumpPath: @"C:\fake\path.dmp");

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
            connectionString: "tcp:server=nonexistent.invalid,port=99999",
            dumpPath: @"C:\fake\path.dmp");

        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteStackTrace_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteStackTrace(
            connectionString: "tcp:server=nonexistent.invalid,port=99999",
            dumpPath: @"C:\fake\path.dmp",
            stackType: "managed");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteListModules_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteListModules(
            connectionString: "tcp:server=nonexistent.invalid,port=99999",
            dumpPath: @"C:\fake\path.dmp");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteDebugCommand_WithInvalidConnectionString_ReturnsError()
    {
        var result = await RemoteDebugTools.RemoteDebugCommand(
            connectionString: "tcp:server=nonexistent.invalid,port=99999",
            dumpPath: @"C:\fake\path.dmp",
            command: "vertarget");

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RemoteAnalyzeCrash_PasswordIsSanitizedInResponse()
    {
        var result = await RemoteDebugTools.RemoteAnalyzeCrash(
            connectionString: "tcp:server=test,port=5005,password=supersecret",
            dumpPath: @"C:\fake\path.dmp");

        var json = JsonSerializer.Serialize(result);
        
        // Password should be masked in any returned connection string
        Assert.DoesNotContain("supersecret", json);
    }

    [Fact]
    public async Task ConnectionStringSanitization_RemovesPassword()
    {
        // Test via the error response which includes sanitized connection string
        var result = await RemoteDebugTools.RemoteAnalyzeCrash(
            connectionString: "ssl:server=myvm,port=5005,password=MySecretPass123",
            dumpPath: @"C:\test.dmp");
        
        var json = JsonSerializer.Serialize(result);
        
        Assert.DoesNotContain("MySecretPass123", json);
        Assert.Contains("password=***", json);
    }
}
