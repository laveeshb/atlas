# Atlas

Windows diagnostics powered by AI.

## What is this?

Atlas is an MCP (Model Context Protocol) server that exposes Windows system diagnostics to AI assistants. Instead of learning WinDbg commands or memorizing Process Explorer, you ask questions in natural language:

- "What's using all the memory?"
- "Why did this process crash?"
- "What's connecting to this IP address?"
- "Compare these two dumps - what grew?"

## Features

- **Process Analysis** - List, search, inspect processes with full command lines and parent/child relationships
- **Memory Dump Analysis** - Analyze .NET crash dumps with heap statistics, object inspection, and leak detection
- **Crash Diagnosis** - Auto-detect crash causes, exception chains, and stack traces
- **Deadlock Detection** - Find threads waiting on locks, identify potential deadlocks
- **Network Connections** - List TCP connections and listeners with owning process info
- **Kernel Diagnostics** - Enumerate loaded kernel drivers with version and signature info
- **Remote Machine Support** - Query processes on remote Windows machines via WMI

## Documentation

- [User-Space Investigation Guide](docs/user-space-guide.md) - Memory leaks, crashes, deadlocks, process analysis
- [Kernel-Space Investigation Guide](docs/kernel-space-guide.md) - Driver analysis, BSODs, security auditing
- [Network Investigation Guide](docs/network-guide.md) - Connections, ports, network troubleshooting

## Prerequisites

- **Windows 10/11 or Windows Server 2016+**
- **.NET 8.0 Runtime** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Administrator privileges** - Required for some operations (process details, remote access)

## Installation

### Building from Source

```powershell
git clone https://github.com/laveeshb/atlas.git
cd atlas

# Install prerequisites (checks for .NET 8 SDK)
.\scripts\install-prereqs.ps1

# Build
.\scripts\build.ps1 -Release
```

The built executable will be at `src/Atlas.Server/bin/Release/net8.0-windows/Atlas.Server.exe`

### MCP Configuration

Add Atlas to your MCP client configuration:

#### GitHub Copilot (VS Code)

Edit your VS Code `settings.json` or `.vscode/mcp.json`:

```json
{
  "mcp": {
    "servers": {
      "atlas": {
        "command": "C:\\path\\to\\Atlas.Server.exe"
      }
    }
  }
}
```

See [VS Code MCP documentation](https://code.visualstudio.com/docs/copilot/customization/mcp-servers) for details.

#### Claude Desktop

Edit `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "atlas": {
      "command": "C:\\path\\to\\Atlas.Server.exe"
    }
  }
}
```

#### Claude Code (CLI)

Edit `~/.claude.json` or use the `/mcp` command to add servers.

## Available Tools

### Process Tools

| Tool | Description |
|------|-------------|
| `list_processes` | List running processes with memory, threads, command line |
| `get_process_details` | Detailed info for a specific process (modules, handles) |
| `get_process_tree` | Parent/child process relationships |
| `find_process` | Search processes by name, command line, or PID |

All process tools support an optional `hostname` parameter to query remote Windows machines (e.g., `hostname: "SERVER01"`). Uses WMI over DCOM - no agent needed on the target, but requires:
- Target machine has WMI service running (default on Windows)
- Firewall allows TCP 135 + dynamic RPC ports
- Your account has admin rights on the target machine

### Network Tools

| Tool | Description |
|------|-------------|
| `list_network_connections` | Active TCP connections with owning process PID |
| `list_tcp_listeners` | TCP ports being listened on with owning process |

### Heap Analysis Tools

| Tool | Description |
|------|-------------|
| `dump_heap_stats` | Object count/size by type (like `!dumpheap -stat`) |
| `find_objects` | Find objects by type name (like `!dumpheap -type`) |
| `dump_object` | Inspect object fields at address (like `!do`) |
| `find_strings` | Find strings, optionally containing specific text |
| `gc_roots` | Find what's keeping an object alive (like `!gcroot`) |

### Memory Diagnostic Tools

| Tool | Description |
|------|-------------|
| `compare_heaps` | Diff two dumps to identify memory growth |
| `large_objects` | List objects on Large Object Heap (>85KB) |
| `finalizer_queue` | Objects with finalizers (potential disposal issues) |
| `pinned_objects` | Pinned objects preventing GC compaction |
| `duplicate_strings` | Find duplicate string content (memory waste) |

### Crash & Hang Diagnosis Tools

| Tool | Description |
|------|-------------|
| `analyze_crash` | Auto-detect crash cause (like `!analyze -v`) |
| `dump_exception` | Exception details with inner exception chain |
| `dump_stack` | Stack traces with method signatures (like `!clrstack`) |
| `detect_deadlocks` | Find threads waiting on locks |
| `waiting_threads` | Show what each thread is blocked on |

### Dump Management Tools

| Tool | Description |
|------|-------------|
| `analyze_dump` | Basic dump analysis - type detection, CLR info, threads |
| `list_dumps` | Find .dmp files in common crash dump locations |

### System Tools

| Tool | Description |
|------|-------------|
| `get_system_info` | OS version, processor count, memory, uptime, .NET version |

### Kernel Tools

| Tool | Description |
|------|-------------|
| `list_drivers` | List loaded kernel drivers with name, path, size, base address |
| `get_driver_info` | Detailed driver info including version and digital signature status |

## Usage Examples

### Investigating High Memory Usage

```
User: "What's using memory in this dump?"

Atlas uses: dump_heap_stats → Shows System.String using 500MB
Atlas uses: find_strings containing:"cache" → Finds cached data
Atlas uses: duplicate_strings → Shows 50MB wasted on duplicates
```

### Diagnosing a Crash

```
User: "Why did this process crash?"

Atlas uses: analyze_crash → Detects NullReferenceException
Atlas uses: dump_exception → Shows full exception chain
Atlas uses: dump_stack → Shows code path leading to crash
```

### Finding a Memory Leak

```
User: "Memory keeps growing, what's leaking?"

Atlas uses: compare_heaps dump1.dmp dump2.dmp → MyApp.CacheEntry grew by 50,000 objects
Atlas uses: find_objects "CacheEntry" → Lists instances
Atlas uses: gc_roots 0x1234... → Shows event handler preventing GC
```

### Investigating Network Connections

```
User: "What process is connecting to 10.0.0.50?"

Atlas uses: list_network_connections → Shows PID 1234 connected to that IP
Atlas uses: get_process_details 1234 → Shows it's MyApp.exe
```

## Security Considerations

**Atlas is a powerful diagnostic tool. Understand these implications before use:**

### Access & Privileges

- **Process inspection** requires access to process memory and may need Administrator privileges
- **Remote machine queries** use WMI and require appropriate network permissions and credentials
- **Dump file analysis** can access any dump file the user has read permissions for

### Sensitive Data Exposure

Memory dumps and process inspection can expose:
- **Credentials** - Passwords, API keys, tokens in memory
- **PII** - Personal data being processed by applications
- **Business data** - Database contents, cached records
- **Encryption keys** - Keys held in memory

**Recommendations:**
- Only analyze dumps from systems you own or have authorization to debug
- Be cautious sharing Atlas output - it may contain sensitive data
- Consider dump file contents as sensitive as the original system
- Use remote hostname feature only on networks you trust and manage

### Network Security

The `hostname` parameter for process tools:
- Connects to remote machines via WMI (DCOM)
- Uses current user credentials by default
- Requires firewall rules allowing WMI traffic (TCP 135 + dynamic ports)

## Roadmap

- **Kernel pool analysis** - Memory pool usage and pool tag tracking
- **Handle/object analysis** - Detect handle leaks and enumerate kernel objects
- **Kernel dump analysis** - Full kernel dump support (currently only .NET user-mode dumps for heap analysis)
- **Linux support** - Process and dump analysis for Linux systems
- **Performance counters** - Real-time CPU, memory, disk metrics
- **ETW tracing** - Event Tracing for Windows integration

## License

MIT
