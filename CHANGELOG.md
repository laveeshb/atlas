# Changelog

## [Unreleased]

### Added

- **Remote Debug Tools**: Analyze crash dumps on remote VMs via remote.exe
  - `remote_analyze_crash` - Crash analysis on remote session
  - `remote_heap_stats` - Heap statistics from remote dump
  - `remote_stack_trace` - Managed/native stack traces
  - `remote_list_modules` - List loaded modules
  - `remote_debug_command` - Run arbitrary WinDbg commands
  - Connection via `remote.exe` for persistent, multi-client sessions
  - See [Remote Debugging Guide](docs/remote-debugging-guide.md)

## [0.1.0] - 2026-01-03

### Added

- **Process Tools**: List, search, and inspect Windows processes
  - `list_processes` - List all running processes with memory, threads, command line
  - `get_process_details` - Detailed info including modules and handles
  - `get_process_tree` - Parent/child process relationships
  - `find_process` - Search by name, command line, or PID
  - Remote machine support via WMI

- **Network Tools**: TCP connection and listener analysis
  - `list_network_connections` - Active TCP connections with owning process
  - `list_tcp_listeners` - TCP ports being listened on

- **Heap Analysis Tools**: .NET memory inspection
  - `dump_heap_stats` - Object count/size by type
  - `find_objects` - Find objects by type name
  - `dump_object` - Inspect object fields
  - `find_strings` - Find string objects
  - `gc_roots` - Find what's keeping objects alive

- **Memory Diagnostic Tools**: Memory leak detection
  - `compare_heaps` - Diff two dumps to identify memory growth
  - `large_objects` - List Large Object Heap contents
  - `finalizer_queue` - Objects with finalizers
  - `pinned_objects` - Pinned objects preventing compaction
  - `duplicate_strings` - Find duplicate string content

- **Crash Diagnostic Tools**: Exception and hang analysis
  - `analyze_crash` - Auto-detect crash cause
  - `dump_exception` - Exception details with inner chain
  - `dump_stack` - Stack traces with method signatures
  - `detect_deadlocks` - Find threads waiting on locks
  - `waiting_threads` - Show thread wait states

- **Dump Management Tools**: Memory dump handling
  - `analyze_dump` - Detect dump type and extract info
  - `list_dumps` - Find .dmp files in common locations

- **System Tools**: Basic system information
  - `get_system_info` - OS version, processor count, uptime

- **Build System**
  - PowerShell build scripts (`scripts/build.ps1`, `scripts/install-prereqs.ps1`)
  - Pinned .NET 8 SDK version via `global.json`

- **Error Handling**
  - Actionable error messages for access denied, timeout, and connectivity issues
  - Graceful handling of 32-bit vs 64-bit process inspection

- **Testing**
  - Integration tests for ProcessTools and NetworkTools

[Unreleased]: https://github.com/laveeshb/atlas/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/laveeshb/atlas/releases/tag/v0.1.0
