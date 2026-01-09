using Atlas.Server.Tools;

namespace Atlas.Server.Tests;

/// <summary>
/// Integration tests for crash diagnostic tools.
/// </summary>
public class CrashDiagnosticToolsTests : DumpTestBase
{
    public CrashDiagnosticToolsTests(TestAppFixture fixture) : base(fixture) { }

    [Fact]
    public void CrashDiagnosticTools_AllOperationsWork()
    {
        // Arrange - capture one dump for all crash diagnostic operations
        var dumpPath = CaptureTestDump("crash_diag");

        // --- AnalyzeCrash ---
        var crashResult = CrashDiagnosticTools.AnalyzeCrash(dumpPath);
        var crashJson = ToJson(crashResult);
        
        AssertNoError(crashJson, "AnalyzeCrash");
        AssertHasProperty(crashJson, "status");
        var threadCount = AssertHasProperty(crashJson, "threadCount");
        Assert.True(threadCount.GetInt32() > 0, "AnalyzeCrash: Should have threads");
        AssertHasProperty(crashJson, "threadSummary");

        // --- DumpStack (all threads) ---
        var stackResult = CrashDiagnosticTools.DumpStack(dumpPath, maxFrames: 20);
        var stackJson = ToJson(stackResult);
        
        AssertNoError(stackJson, "DumpStack");
        var stackThreadCount = AssertHasProperty(stackJson, "threadCount");
        Assert.True(stackThreadCount.GetInt32() > 0, "DumpStack: Should have threads with stacks");
        var stacks = AssertHasProperty(stackJson, "stacks");
        Assert.True(stacks.GetArrayLength() > 0, "DumpStack: Should have stack traces");
        var firstStack = stacks[0];
        AssertHasProperty(firstStack, "osThreadId");
        var frames = AssertHasProperty(firstStack, "frames");
        if (frames.GetArrayLength() > 0)
        {
            AssertHasProperty(frames[0], "signature");
        }

        // --- DumpStack (specific thread) ---
        var threadId = firstStack.GetProperty("osThreadId").GetString()!;
        var singleStackResult = CrashDiagnosticTools.DumpStack(dumpPath, threadId: threadId, maxFrames: 20);
        var singleStackJson = ToJson(singleStackResult);
        
        AssertNoError(singleStackJson, "DumpStack (specific thread)");
        Assert.Equal(1, singleStackJson.GetProperty("threadCount").GetInt32());

        // --- DetectDeadlocks ---
        var deadlockResult = CrashDiagnosticTools.DetectDeadlocks(dumpPath);
        var deadlockJson = ToJson(deadlockResult);
        
        AssertNoError(deadlockJson, "DetectDeadlocks");
        AssertHasProperty(deadlockJson, "potentialDeadlock");
        AssertHasProperty(deadlockJson, "waitingThreadCount");
        AssertHasProperty(deadlockJson, "analysis");

        // --- WaitingThreads ---
        var waitingResult = CrashDiagnosticTools.WaitingThreads(dumpPath);
        var waitingJson = ToJson(waitingResult);
        
        AssertNoError(waitingJson, "WaitingThreads");

        // --- DumpException ---
        var exResult = CrashDiagnosticTools.DumpException(dumpPath);
        var exJson = ToJson(exResult);
        
        // Healthy process should report no exception (error is expected)
        if (exJson.TryGetProperty("error", out var error))
        {
            Assert.Contains("exception", error.GetString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
