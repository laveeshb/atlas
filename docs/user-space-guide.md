# User-Space Investigation Guide

This guide covers investigating user-mode applications: .NET memory issues, crashes, hangs, and process behavior.

## Table of Contents

- [Getting Started](#getting-started)
- [Memory Leak Investigation](#memory-leak-investigation)
- [Crash Analysis](#crash-analysis)
- [Deadlock and Hang Analysis](#deadlock-and-hang-analysis)
- [Process Investigation](#process-investigation)
- [Tips and Best Practices](#tips-and-best-practices)

---

## Getting Started

### Capturing a Memory Dump

Before analyzing, you need a dump file. Common ways to capture:

**Task Manager (quick)**
1. Open Task Manager → Details tab
2. Right-click process → Create dump file
3. Note the path (usually `%TEMP%\<process>.DMP`)

**procdump (recommended for production)**
```powershell
# Capture immediately
procdump -ma <pid> dump.dmp

# Capture on crash
procdump -ma -e <pid> dump.dmp

# Capture on high memory
procdump -ma -m 1024 <pid> dump.dmp
```

**dotnet-dump (.NET Core/5+)**
```powershell
dotnet-dump collect -p <pid> -o dump.dmp
```

### Finding Dump Files

Ask Atlas to find existing dumps:
```
"Find any crash dumps on this system"
```
Atlas uses `list_dumps` to search common locations like `%LOCALAPPDATA%\CrashDumps`.

---

## Memory Leak Investigation

### Symptoms
- Process memory grows over time
- OutOfMemoryException
- System becoming slow/unresponsive

### Investigation Flow

#### Step 1: Get the Big Picture

```
"What types are using the most memory in this dump?"
```

Atlas uses `dump_heap_stats` to show object counts and sizes by type:

| Type | Count | Total Size |
|------|-------|------------|
| System.String | 1,234,567 | 500 MB |
| MyApp.CacheEntry | 500,000 | 200 MB |
| System.Byte[] | 10,000 | 150 MB |

**What to look for:**
- Unexpectedly high counts of your application types
- Large string counts (often symptoms of caching issues)
- Byte arrays (possible unbounded buffers)

#### Step 2: Investigate Suspicious Types

```
"Find all CacheEntry objects"
```

Atlas uses `find_objects` to list instances. Look for:
- Unexpectedly high counts
- Objects that should have been collected

#### Step 3: Find Why Objects Are Alive

```
"Why is object at 0x1a2b3c4d still alive?"
```

Atlas uses `gc_roots` to trace from GC roots to your object:

```
Thread Stack Root
  → MyApp.EventHandler
    → System.EventHandler
      → MyApp.CacheEntry (your object)
```

**Common leak patterns:**
- **Event handlers** - Objects subscribed to events on long-lived objects
- **Static collections** - Lists/dictionaries on static fields that grow forever
- **Timers** - Timer callbacks holding references
- **Closures** - Lambda captures keeping objects alive

#### Step 4: Compare Two Dumps (Growth Analysis)

Capture dumps at two points in time, then:

```
"Compare dump1.dmp and dump2.dmp - what grew?"
```

Atlas uses `compare_heaps` to show delta:

| Type | Before | After | Delta |
|------|--------|-------|-------|
| MyApp.CacheEntry | 10,000 | 60,000 | +50,000 |
| System.String | 100,000 | 350,000 | +250,000 |

### Specialized Memory Tools

**Large Object Heap issues:**
```
"Show objects on the Large Object Heap"
```
Atlas uses `large_objects` - LOH fragmentation can cause OOM even with available memory.

**Disposal issues:**
```
"What objects are waiting for finalization?"
```
Atlas uses `finalizer_queue` - objects with finalizers that weren't disposed properly.

**String waste:**
```
"Find duplicate strings"
```
Atlas uses `duplicate_strings` - same string content stored multiple times.

---

## Crash Analysis

### Symptoms
- Application terminated unexpectedly
- Unhandled exception
- Windows Error Reporting dialog

### Investigation Flow

#### Step 1: Auto-Analyze

```
"Why did this application crash?"
```

Atlas uses `analyze_crash` to auto-detect:
- Exception type (NullReferenceException, AccessViolationException, etc.)
- Faulting thread
- Crash location

#### Step 2: Get Exception Details

```
"Show the exception details"
```

Atlas uses `dump_exception` to show:
- Exception message
- Full inner exception chain
- Exception properties (HResult, etc.)

#### Step 3: Get Stack Trace

```
"Show the stack trace for the crash"
```

Atlas uses `dump_stack` to show the call stack with:
- Method names with parameters
- Source file and line numbers (if symbols available)
- IL offset

### Common Crash Patterns

**NullReferenceException**
- Check the stack trace for the faulting method
- Often caused by uninitialized variables or unexpected null returns

**AccessViolationException**
- Usually P/Invoke or unsafe code issues
- Check for incorrect marshaling or freed memory access

**StackOverflowException**
- Look for recursive calls in the stack
- No exception object (process killed by CLR)

**OutOfMemoryException**
- Use memory investigation techniques above
- Check for LOH fragmentation with `large_objects`

---

## Deadlock and Hang Analysis

### Symptoms
- Application frozen/not responding
- Specific operations never complete
- CPU at 0% but application stuck

### Investigation Flow

#### Step 1: Detect Deadlocks

```
"Are there any deadlocks?"
```

Atlas uses `detect_deadlocks` to find circular wait patterns:

```
Thread 1: Holds Lock A, Waiting for Lock B
Thread 2: Holds Lock B, Waiting for Lock A
→ DEADLOCK DETECTED
```

#### Step 2: See What Threads Are Waiting On

```
"What are threads waiting for?"
```

Atlas uses `waiting_threads` to show:
- Thread ID and name
- Wait reason (lock, I/O, sleep, etc.)
- Object being waited on

#### Step 3: Get Thread Stacks

```
"Show stack for thread 15"
```

Atlas uses `dump_stack` to see exactly where the thread is stuck.

### Common Hang Patterns

**Lock ordering deadlock**
- Two threads acquiring locks in opposite order
- Fix: Always acquire locks in consistent order

**Async deadlock**
- `.Result` or `.Wait()` on async code in UI/ASP.NET context
- Fix: Use `await` instead, or `ConfigureAwait(false)`

**Database/network timeout**
- Thread waiting on external resource
- Check connection strings, network connectivity

---

## Process Investigation

### Live Process Analysis

```
"What processes are using the most memory?"
```

Atlas uses `list_processes` to show running processes with memory usage.

```
"Show details for process 1234"
```

Atlas uses `get_process_details` to show:
- Command line arguments
- Loaded modules
- Thread count
- Working set, private bytes

```
"Show the process tree for chrome"
```

Atlas uses `get_process_tree` to show parent/child relationships.

### Remote Machine Analysis

All process tools support remote machines:

```
"List processes on SERVER01"
```

Requires:
- WMI service running on target
- Admin rights on target machine
- Firewall allowing WMI (TCP 135 + dynamic ports)

---

## Tips and Best Practices

### Dump Capture Tips

1. **Full dumps for memory analysis** - Use `-ma` flag with procdump
2. **Capture before and after** - For leak analysis, capture at intervals
3. **Include all managed threads** - Ensures complete heap analysis

### Symbol Configuration

For better stack traces, configure symbol servers:
```powershell
# Set symbol path
$env:_NT_SYMBOL_PATH = "srv*C:\Symbols*https://msdl.microsoft.com/download/symbols"
```

### Common Pitfalls

1. **Mini-dumps lack heap data** - Need full dumps for `dump_heap_stats`, `find_objects`, etc.
2. **32-bit vs 64-bit** - Make sure you're analyzing with matching bitness
3. **DAC mismatch** - CLR version in dump must match analysis machine for best results

### Security Considerations

Memory dumps contain sensitive data:
- Credentials, API keys, tokens
- Personal/business data in memory
- Encryption keys

Treat dump files as sensitive and restrict access.
