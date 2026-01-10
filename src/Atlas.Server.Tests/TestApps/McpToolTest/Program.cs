using System.Text.Json;
using Atlas.Server.Tools;

// Test the actual MCP tools against a running remote.exe session
// Usage: McpToolTest <connectionString>
// Example: McpToolTest localhost/TestSession

var connectionString = args.Length > 0 ? args[0] : "localhost/TestSession";

Console.WriteLine("=== MCP Tool Integration Test ===");
Console.WriteLine($"Connection: {connectionString}");
Console.WriteLine();

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

// Test 1: remote_debug_command with vertarget
Console.WriteLine("Test 1: remote_debug_command (vertarget)");
Console.WriteLine("----------------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteDebugCommand(connectionString, "vertarget");
    var json = JsonSerializer.Serialize(result, jsonOptions);
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 2: remote_list_modules
Console.WriteLine("Test 2: remote_list_modules");
Console.WriteLine("---------------------------");
try
{
    var result = await RemoteDebugTools.RemoteListModules(connectionString);
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    // Truncate if too long
    if (json.Length > 2000)
        json = json.Substring(0, 2000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 3: remote_analyze_crash
Console.WriteLine("Test 3: remote_analyze_crash");
Console.WriteLine("----------------------------");
try
{
    var result = await RemoteDebugTools.RemoteAnalyzeCrash(connectionString);
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    // Truncate if too long
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 4: remote_heap_stats (!dumpheap -stat)
Console.WriteLine("Test 4: remote_heap_stats (!dumpheap -stat)");
Console.WriteLine("-------------------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteHeapStats(connectionString);
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 5: remote_stack_trace (!clrstack)
Console.WriteLine("Test 5: remote_stack_trace (managed)");
Console.WriteLine("------------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteStackTrace(connectionString, "managed");
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 6: raw command - !eeheap
Console.WriteLine("Test 6: remote_debug_command (!eeheap)");
Console.WriteLine("--------------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteDebugCommand(connectionString, "!eeheap");
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 7: raw command - !threads
Console.WriteLine("Test 7: remote_debug_command (!threads)");
Console.WriteLine("---------------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteDebugCommand(connectionString, "!threads");
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

// Test 8: raw command - k (native stack)
Console.WriteLine("Test 8: remote_debug_command (k)");
Console.WriteLine("--------------------------------");
try
{
    var result = await RemoteDebugTools.RemoteDebugCommand(connectionString, "k");
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    if (json.Length > 3000)
        json = json.Substring(0, 3000) + "\n... (truncated)";
    
    Console.WriteLine(json);
    
    if (json.Contains("error"))
        Console.WriteLine("RESULT: FAILED\n");
    else
        Console.WriteLine("RESULT: PASSED\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine("RESULT: FAILED\n");
}

Console.WriteLine("=== Test Complete ===");
