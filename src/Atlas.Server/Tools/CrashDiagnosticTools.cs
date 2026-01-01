using Microsoft.Diagnostics.Runtime;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class CrashDiagnosticTools
{
    [McpServerTool(Name = "analyze_crash")]
    [Description("Auto-detect crash cause - identifies faulting thread, exception chain, and probable cause (like WinDbg !analyze -v)")]
    public static object AnalyzeCrash(
        [Description("Full path to the .dmp file")] string filePath)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();

            // Find threads with exceptions
            var threadsWithExceptions = runtime.Threads
                .Where(t => t.CurrentException != null)
                .ToList();

            ClrThread? faultingThread = null;
            ClrException? primaryException = null;

            // Find the most likely faulting thread
            foreach (var thread in threadsWithExceptions)
            {
                var ex = thread.CurrentException;
                if (ex != null)
                {
                    // Prefer threads with unhandled/fatal exceptions
                    if (primaryException == null ||
                        ex.Type?.Name?.Contains("StackOverflow") == true ||
                        ex.Type?.Name?.Contains("OutOfMemory") == true ||
                        ex.Type?.Name?.Contains("AccessViolation") == true)
                    {
                        faultingThread = thread;
                        primaryException = ex;
                    }
                }
            }

            // If no exception found, look for threads in problematic states
            if (faultingThread == null)
            {
                faultingThread = runtime.Threads.FirstOrDefault(t =>
                    t.EnumerateStackTrace().Any(f =>
                        f.Method?.Name?.Contains("Throw") == true ||
                        f.Method?.Name?.Contains("FailFast") == true));
            }

            object? crashAnalysis = null;
            if (faultingThread != null && primaryException != null)
            {
                var exceptionChain = GetExceptionChain(primaryException);
                var stackTrace = faultingThread.EnumerateStackTrace()
                    .Take(20)
                    .Select(f => new
                    {
                        method = f.Method?.ToString() ?? "<unknown>",
                        module = f.Method?.Type?.Module?.Name,
                        offset = f.InstructionPointer > 0 ? $"0x{f.InstructionPointer:X}" : null
                    })
                    .ToList();

                crashAnalysis = new
                {
                    faultingThread = new
                    {
                        osThreadId = $"0x{faultingThread.OSThreadId:X}",
                        managedThreadId = faultingThread.ManagedThreadId,
                        isGc = faultingThread.IsGc,
                        isFinalizer = faultingThread.IsFinalizer
                    },
                    exception = new
                    {
                        type = primaryException.Type?.Name,
                        message = primaryException.Message,
                        hresult = $"0x{primaryException.HResult:X8}"
                    },
                    exceptionChain,
                    stackTrace,
                    probableCause = InferProbableCause(primaryException)
                };
            }

            // Summary of all threads
            var threadSummary = runtime.Threads
                .Select(t => new
                {
                    osThreadId = $"0x{t.OSThreadId:X}",
                    managedThreadId = t.ManagedThreadId,
                    hasException = t.CurrentException != null,
                    exceptionType = t.CurrentException?.Type?.Name,
                    isGc = t.IsGc,
                    isFinalizer = t.IsFinalizer,
                    stackDepth = t.EnumerateStackTrace().Count()
                })
                .Where(t => t.hasException || t.stackDepth > 0)
                .Take(20)
                .ToList();

            return new
            {
                status = crashAnalysis != null ? "crash_detected" : "no_crash_detected",
                crashAnalysis,
                threadCount = runtime.Threads.Count(),
                threadsWithExceptions = threadsWithExceptions.Count,
                threadSummary
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "dump_exception")]
    [Description("Print exception details with full inner exception chain (like WinDbg !pe)")]
    public static object DumpException(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Optional: thread OS ID (hex) to get exception from. If not specified, finds first thread with exception")] string? threadId = null)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();

            ClrThread? targetThread = null;

            if (!string.IsNullOrEmpty(threadId))
            {
                var tid = ParseThreadId(threadId);
                targetThread = runtime.Threads.FirstOrDefault(t => t.OSThreadId == tid);
                if (targetThread == null)
                    return new { error = $"Thread {threadId} not found" };
            }
            else
            {
                targetThread = runtime.Threads.FirstOrDefault(t => t.CurrentException != null);
            }

            if (targetThread == null)
                return new { error = "No thread with exception found" };

            var exception = targetThread.CurrentException;
            if (exception == null)
                return new { error = $"Thread 0x{targetThread.OSThreadId:X} has no current exception" };

            var exceptionChain = GetExceptionChain(exception);

            return new
            {
                thread = new
                {
                    osThreadId = $"0x{targetThread.OSThreadId:X}",
                    managedThreadId = targetThread.ManagedThreadId
                },
                exceptionCount = exceptionChain.Count,
                exceptions = exceptionChain
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "dump_stack")]
    [Description("Get detailed stack trace with method signatures (like WinDbg !clrstack)")]
    public static object DumpStack(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Thread OS ID (hex like 0x1A4C). If not specified, dumps all threads")] string? threadId = null,
        [Description("Max frames per thread (default 50)")] int maxFrames = 50)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();

            IEnumerable<ClrThread> threads;
            if (!string.IsNullOrEmpty(threadId))
            {
                var tid = ParseThreadId(threadId);
                var thread = runtime.Threads.FirstOrDefault(t => t.OSThreadId == tid);
                if (thread == null)
                    return new { error = $"Thread {threadId} not found" };
                threads = new[] { thread };
            }
            else
            {
                threads = runtime.Threads.Where(t => t.EnumerateStackTrace().Any()).Take(20);
            }

            var stacks = threads.Select(t =>
            {
                var frames = t.EnumerateStackTrace()
                    .Take(maxFrames)
                    .Select(f =>
                    {
                        var method = f.Method;
                        string signature = method?.Signature ?? method?.ToString() ?? "<unknown>";

                        return new
                        {
                            instructionPointer = $"0x{f.InstructionPointer:X}",
                            stackPointer = $"0x{f.StackPointer:X}",
                            signature,
                            module = method?.Type?.Module?.Name
                        };
                    })
                    .ToList();

                return new
                {
                    osThreadId = $"0x{t.OSThreadId:X}",
                    managedThreadId = t.ManagedThreadId,
                    isGc = t.IsGc,
                    isFinalizer = t.IsFinalizer,
                    hasException = t.CurrentException != null,
                    exceptionType = t.CurrentException?.Type?.Name,
                    frameCount = frames.Count,
                    frames
                };
            }).ToList();

            return new
            {
                threadCount = stacks.Count,
                stacks
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "detect_deadlocks")]
    [Description("Analyze threads for potential deadlock patterns based on lock wait states")]
    public static object DetectDeadlocks(
        [Description("Full path to the .dmp file")] string filePath)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();

            // Analyze threads for lock-related patterns in their stacks
            var lockWaitingThreads = new List<object>();

            foreach (var thread in runtime.Threads)
            {
                var stackFrames = thread.EnumerateStackTrace().ToList();
                var isWaitingOnLock = false;
                string? waitMethod = null;

                foreach (var frame in stackFrames)
                {
                    var methodName = frame.Method?.Name ?? "";
                    var typeName = frame.Method?.Type?.Name ?? "";

                    // Check for common lock wait patterns
                    if (methodName.Contains("Wait") ||
                        methodName.Contains("Enter") ||
                        typeName.Contains("Monitor") ||
                        typeName.Contains("Mutex") ||
                        typeName.Contains("Semaphore") ||
                        typeName.Contains("ReaderWriterLock") ||
                        typeName.Contains("ManualResetEvent") ||
                        typeName.Contains("AutoResetEvent"))
                    {
                        isWaitingOnLock = true;
                        waitMethod = $"{typeName}.{methodName}";
                        break;
                    }
                }

                if (isWaitingOnLock)
                {
                    lockWaitingThreads.Add(new
                    {
                        osThreadId = $"0x{thread.OSThreadId:X}",
                        managedThreadId = thread.ManagedThreadId,
                        waitMethod,
                        topFrames = stackFrames.Take(5).Select(f => f.Method?.ToString() ?? "<unknown>").ToList()
                    });
                }
            }

            return new
            {
                potentialDeadlock = lockWaitingThreads.Count > 1,
                waitingThreadCount = lockWaitingThreads.Count,
                waitingThreads = lockWaitingThreads,
                analysis = lockWaitingThreads.Count > 1
                    ? "Multiple threads waiting on locks detected. Review thread stacks for circular wait patterns."
                    : lockWaitingThreads.Count == 1
                        ? "One thread waiting on a lock. May be waiting for external resource."
                        : "No threads waiting on locks detected.",
                note = "For detailed deadlock analysis, examine the thread stacks for circular dependencies."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "waiting_threads")]
    [Description("Show what each thread is waiting on based on stack analysis")]
    public static object WaitingThreads(
        [Description("Full path to the .dmp file")] string filePath)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();

            var threads = runtime.Threads
                .Select(t =>
                {
                    var stackFrames = t.EnumerateStackTrace().ToList();
                    var topFrame = stackFrames.FirstOrDefault();

                    // Infer wait state from stack
                    string waitState = "Unknown";
                    string? waitingOn = null;

                    foreach (var frame in stackFrames.Take(10))
                    {
                        var methodName = frame.Method?.Name ?? "";
                        var typeName = frame.Method?.Type?.Name ?? "";
                        var fullName = $"{typeName}.{methodName}";

                        if (typeName.Contains("Monitor") || methodName == "Enter" || methodName == "TryEnter")
                        {
                            waitState = "WaitingOnMonitor";
                            waitingOn = fullName;
                            break;
                        }
                        if (typeName.Contains("Mutex"))
                        {
                            waitState = "WaitingOnMutex";
                            waitingOn = fullName;
                            break;
                        }
                        if (typeName.Contains("Semaphore"))
                        {
                            waitState = "WaitingOnSemaphore";
                            waitingOn = fullName;
                            break;
                        }
                        if (typeName.Contains("ManualResetEvent") || typeName.Contains("AutoResetEvent"))
                        {
                            waitState = "WaitingOnEvent";
                            waitingOn = fullName;
                            break;
                        }
                        if (methodName.Contains("Sleep"))
                        {
                            waitState = "Sleeping";
                            waitingOn = fullName;
                            break;
                        }
                        if (methodName.Contains("Wait") && !methodName.Contains("Await"))
                        {
                            waitState = "Waiting";
                            waitingOn = fullName;
                            break;
                        }
                        if (methodName.Contains("Join"))
                        {
                            waitState = "WaitingForThread";
                            waitingOn = fullName;
                            break;
                        }
                        if (typeName.Contains("Socket") || typeName.Contains("Stream"))
                        {
                            if (methodName.Contains("Read") || methodName.Contains("Receive"))
                            {
                                waitState = "WaitingForIO";
                                waitingOn = fullName;
                                break;
                            }
                        }
                    }

                    if (waitState == "Unknown" && stackFrames.Any())
                    {
                        waitState = "Running";
                    }

                    return new
                    {
                        osThreadId = $"0x{t.OSThreadId:X}",
                        managedThreadId = t.ManagedThreadId,
                        isGc = t.IsGc,
                        isFinalizer = t.IsFinalizer,
                        isAlive = t.IsAlive,
                        waitState,
                        waitingOn,
                        topFrame = topFrame?.Method?.ToString()
                    };
                })
                .Where(t => t.isAlive)
                .ToList();

            var stateGroups = threads
                .GroupBy(t => t.waitState)
                .Select(g => new { state = g.Key, count = g.Count() })
                .OrderByDescending(g => g.count)
                .ToList();

            return new
            {
                totalThreads = threads.Count,
                stateBreakdown = stateGroups,
                threads
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    private static List<object> GetExceptionChain(ClrException exception)
    {
        var chain = new List<object>();
        var current = exception;
        int depth = 0;

        while (current != null && depth < 10)
        {
            var stackTrace = current.StackTrace
                .Take(10)
                .Select(f => f.Method?.ToString() ?? "<unknown>")
                .ToList();

            chain.Add(new
            {
                depth,
                type = current.Type?.Name,
                message = current.Message,
                hresult = $"0x{current.HResult:X8}",
                address = $"0x{current.Address:X}",
                stackTrace
            });

            current = current.Inner;
            depth++;
        }

        return chain;
    }

    private static string InferProbableCause(ClrException exception)
    {
        var exType = exception.Type?.Name ?? "";
        var message = exception.Message ?? "";

        if (exType.Contains("NullReferenceException"))
            return "Null reference - an object was accessed before being initialized";

        if (exType.Contains("StackOverflowException"))
            return "Stack overflow - likely infinite recursion or too deep call stack";

        if (exType.Contains("OutOfMemoryException"))
            return "Out of memory - process exceeded available memory or had a memory leak";

        if (exType.Contains("AccessViolationException"))
            return "Access violation - invalid memory access, possibly from native interop";

        if (exType.Contains("InvalidOperationException"))
            return $"Invalid operation - {message}";

        if (exType.Contains("ArgumentException") || exType.Contains("ArgumentNullException"))
            return $"Invalid argument - {message}";

        if (exType.Contains("IOException"))
            return $"I/O error - {message}";

        if (exType.Contains("TimeoutException"))
            return "Operation timed out";

        if (exType.Contains("SqlException") || exType.Contains("DbException"))
            return $"Database error - {message}";

        return $"Exception: {exType} - {message}";
    }

    private static uint ParseThreadId(string threadId)
    {
        var tid = threadId.Trim();
        if (tid.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            tid = tid[2..];

        if (uint.TryParse(tid, System.Globalization.NumberStyles.HexNumber, null, out var result))
            return result;

        if (uint.TryParse(threadId, out result))
            return result;

        return 0;
    }
}
