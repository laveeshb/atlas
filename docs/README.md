# Atlas Documentation

Guides for using Atlas to investigate Windows system issues.

## Investigation Guides

| Guide | Description |
|-------|-------------|
| [User-Space Investigations](user-space-guide.md) | Process analysis, .NET memory dumps, crash diagnosis |
| [Kernel-Space Investigations](kernel-space-guide.md) | Driver analysis, kernel pool, system-level diagnostics |
| [Network Investigations](network-guide.md) | TCP connections, port listeners, process network activity |
| [Remote Debugging](remote-debugging-guide.md) | Analyze dumps on remote VMs via remote.exe |

## Quick Reference

### Common Investigation Workflows

**"Why is this process using so much memory?"**
→ See [Memory Leak Investigation](user-space-guide.md#memory-leak-investigation)

**"Why did this application crash?"**
→ See [Crash Analysis](user-space-guide.md#crash-analysis)

**"What driver is causing this BSOD?"**
→ See [Driver Analysis](kernel-space-guide.md#driver-analysis)

**"What's connecting to this IP address?"**
→ See [Connection Tracking](network-guide.md#finding-connections-by-ip)

**"Analyze a crash dump on a remote VM"**
→ See [Remote Debugging Guide](remote-debugging-guide.md)

## Tool Categories

- **Process Tools** - `list_processes`, `get_process_details`, `find_process`, `get_process_tree`
- **Heap Analysis** - `dump_heap_stats`, `find_objects`, `dump_object`, `find_strings`, `gc_roots`
- **Memory Diagnostics** - `compare_heaps`, `large_objects`, `finalizer_queue`, `pinned_objects`, `duplicate_strings`
- **Crash Diagnosis** - `analyze_crash`, `dump_exception`, `dump_stack`, `detect_deadlocks`, `waiting_threads`
- **Network Tools** - `list_network_connections`, `list_tcp_listeners`
- **Kernel Tools** - `list_drivers`, `get_driver_info`, `analyze_pool_usage`, `list_pool_tags`, `find_handle_leaks`
- **Remote Debug Tools** - `remote_analyze_crash`, `remote_heap_stats`, `remote_stack_trace`, `remote_list_modules`, `remote_debug_command`
- **System Tools** - `get_system_info`
- **Dump Management** - `analyze_dump`, `list_dumps`
