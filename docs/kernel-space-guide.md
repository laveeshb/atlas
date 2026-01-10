# Kernel-Space Investigation Guide

This guide covers investigating kernel-level issues: driver problems, BSODs, system stability, and kernel resource usage.

## Table of Contents

- [Getting Started](#getting-started)
- [Driver Analysis](#driver-analysis)
- [BSOD Investigation](#bsod-investigation)
- [Security Auditing](#security-auditing)
- [Tips and Best Practices](#tips-and-best-practices)
- [Pool Memory Analysis](#pool-memory-analysis)
- [Handle Leak Detection](#handle-leak-detection)
- [Thread Analysis](#thread-analysis)
- [System Resources](#system-resources)
- [Coming Soon](#coming-soon)

---

## Getting Started

### Understanding Kernel vs User Mode

| Aspect | User Mode | Kernel Mode |
|--------|-----------|-------------|
| Access | Limited, sandboxed | Full system access |
| Crashes | Process terminates | System BSOD |
| Tools | ClrMD, debuggers | DbgEng, WinDbg |
| Dumps | .dmp from process | Memory.dmp from system |

### When to Use Kernel Tools

- System is unstable or crashing (BSODs)
- Investigating driver conflicts
- Security audit (unsigned drivers, rootkits)
- Performance issues at system level
- Hardware-related problems

---

## Driver Analysis

### Listing Loaded Drivers

```
"List all loaded kernel drivers"
```

Atlas uses `list_drivers` to enumerate all kernel modules:

| Name | Path | Size | Load Order |
|------|------|------|------------|
| ntoskrnl.exe | C:\Windows\system32\ntoskrnl.exe | 11.3 MB | 0 |
| hal.dll | C:\Windows\system32\hal.dll | 788 KB | 1 |
| nvlddmkm.sys | C:\Windows\System32\DriverStore\... | 45.2 MB | 87 |

**What to look for:**
- Unusual driver names or paths
- Drivers loaded from unexpected locations
- Very recently added drivers (check file dates)

### Filtering Drivers

```
"Show drivers with 'nvidia' in the name"
```

```
"List the first 20 drivers by load order"
```

Atlas filters using `nameFilter` and `limit` parameters.

### Getting Driver Details

```
"Get details for the ntfs driver"
```

Atlas uses `get_driver_info` to show:

```json
{
  "name": "ntfs.sys",
  "path": "C:\\Windows\\system32\\drivers\\ntfs.sys",
  "baseAddress": "0xFFFFF80014A00000",
  "size": 2306048,
  "version": {
    "fileVersion": "10.0.22621.2506",
    "companyName": "Microsoft Corporation",
    "description": "NT File System Driver"
  },
  "signature": {
    "signed": true,
    "subject": "CN=Microsoft Windows, O=Microsoft Corporation...",
    "issuer": "CN=Microsoft Windows Production PCA 2011",
    "isValid": true
  }
}
```

---

## BSOD Investigation

### Common BSOD Causes

| Bug Check | Common Cause |
|-----------|--------------|
| DRIVER_IRQL_NOT_LESS_OR_EQUAL | Driver accessing paged memory at high IRQL |
| SYSTEM_SERVICE_EXCEPTION | Driver or system code exception |
| PAGE_FAULT_IN_NONPAGED_AREA | Driver referencing invalid memory |
| KERNEL_DATA_INPAGE_ERROR | Disk/storage issues |
| CRITICAL_PROCESS_DIED | Critical system process crashed |

### Investigation Flow

#### Step 1: Check Recently Loaded Drivers

After a BSOD, check what drivers are loaded:

```
"List all loaded drivers"
```

Look for:
- Third-party drivers (non-Microsoft)
- Recently updated drivers
- Drivers with unusually large sizes

#### Step 2: Verify Driver Signatures

```
"Get details for suspicious_driver.sys"
```

Check:
- Is the driver signed?
- Is the signature valid (not expired)?
- Who is the publisher?

**Red flags:**
- Unsigned drivers
- Self-signed certificates
- Expired signatures
- Unknown publishers

#### Step 3: Identify the Faulting Driver

If you have the bug check parameter (from Event Viewer or BlueScreenView):

1. The address often points to the faulting module
2. Match the address to driver base addresses from `list_drivers`
3. Get details on that driver

### Collecting BSOD Information

**Event Viewer:**
1. Open Event Viewer → Windows Logs → System
2. Filter for "BugCheck" source
3. Note the bug check code and parameters

**Memory.dmp location:**
- Default: `C:\Windows\MEMORY.DMP`
- Minidumps: `C:\Windows\Minidump\`

---

## Security Auditing

### Finding Unsigned Drivers

Unsigned drivers are potential security risks:

```
"List all drivers and check which are unsigned"
```

For each driver, use `get_driver_info` to check signature status.

**Legitimate reasons for unsigned drivers:**
- Development/test signing during driver development
- Very old legacy hardware

**Security concerns:**
- Rootkits often use unsigned drivers
- Malware may install unsigned drivers to bypass security

### Checking for Suspicious Drivers

**Suspicious patterns:**
- Random or obfuscated names (e.g., `a3x7d2.sys`)
- Loaded from user directories instead of System32
- No version information
- Self-signed or expired certificates
- Unusually small size for claimed functionality

```
"Get details for each third-party driver"
```

### Known Driver Locations

Normal driver paths:
- `C:\Windows\System32\drivers\`
- `C:\Windows\System32\DriverStore\FileRepository\`

Suspicious paths:
- User directories (`C:\Users\...`)
- Temp directories
- Random paths with GUIDs

---

## Tips and Best Practices

### Driver Investigation Checklist

1. **Inventory all drivers** - Know what's loaded
2. **Check signatures** - All production drivers should be signed
3. **Verify publishers** - Confirm legitimate vendors
4. **Compare to baseline** - Know what "normal" looks like
5. **Check load order** - Early-loading drivers have more power

### Correlating with System Events

Use Event Viewer alongside Atlas:
- System log for driver load/unload events
- Application log for related app issues
- Security log for audit events

### Safe Mode Investigation

If system won't boot normally:
1. Boot to Safe Mode (minimal drivers)
2. Compare drivers loaded in Safe Mode vs normal
3. Identify which driver causes the problem

### Creating a Driver Baseline

For production systems, document:
- Expected drivers and versions
- Expected signature status
- Known third-party drivers

Compare periodically to detect changes.

---

## Pool Memory Analysis

### Analyzing Kernel Pool Usage

```
"Show kernel pool memory usage"
```

Atlas uses `analyze_pool_usage` to show paged and non-paged pool statistics:

```json
{
  "kernel": {
    "totalBytes": 523190272,
    "pagedBytes": 412876800,
    "nonPagedBytes": 110313472
  },
  "system": {
    "handleCount": 125000,
    "processCount": 234,
    "threadCount": 3456
  }
}
```

### Finding Memory Leaks by Pool Tag

```
"List top pool tags by memory usage"
```

Atlas uses `list_pool_tags` to identify kernel memory consumers:

| Tag | Paged | Non-Paged | Total |
|-----|-------|-----------|-------|
| CM31 | 125 MB | 0 B | 125 MB |
| MmSt | 45 MB | 12 MB | 57 MB |
| Ntfs | 38 MB | 2 MB | 40 MB |

**Common pool tags:**
- `CM31` - Registry cache
- `MmSt` - Memory manager section tables
- `Ntfs` - NTFS file system
- `Pool` - General pool allocations
- `Thre` - Thread objects

---

## Handle Leak Detection

### Finding Processes with Handle Leaks

```
"Find processes with high handle counts"
```

Atlas uses `find_handle_leaks` to identify potential leaks:

```json
{
  "processes": [
    { "pid": 1234, "name": "LeakyApp", "handleCount": 15000, "severity": "critical" },
    { "pid": 5678, "name": "AnotherApp", "handleCount": 3500, "severity": "medium" }
  ]
}
```

**Severity levels:**
- **critical**: 10,000+ handles
- **high**: 5,000+ handles
- **medium**: 2,000+ handles
- **low**: 1,000+ handles

### System Handle Statistics

```
"Show system handle statistics"
```

Atlas uses `list_handle_types` to show system-wide handle counts.

---

## Thread Analysis

### Analyzing Process Threads

```
"Analyze threads for process 1234"
```

Atlas uses `analyze_thread_stats` to show thread CPU time breakdown:

```json
{
  "threadCount": 45,
  "summary": {
    "totalUserTimeMs": 125000,
    "totalKernelTimeMs": 45000,
    "kernelTimePercent": 26.5
  },
  "threads": [
    { "id": 1234, "state": "Running", "kernelTime": 5000, "userTime": 12000 }
  ]
}
```

**What to look for:**
- High kernel time percentage may indicate I/O-heavy operations
- Threads in "Wait" state with unusual wait reasons
- Threads consuming disproportionate CPU time

---

## System Resources

### Physical Memory Analysis

```
"Show physical memory usage"
```

Atlas uses `get_physical_memory` to show detailed memory statistics:

```json
{
  "memoryLoad": 65,
  "physical": {
    "totalBytes": 34359738368,
    "availableBytes": 12073353216,
    "usedPercent": 64.8
  },
  "kernel": {
    "pagedBytes": 412876800,
    "nonPagedBytes": 110313472
  }
}
```

### Comprehensive System Overview

```
"Show system resource summary"
```

Atlas uses `get_system_resources` for a complete overview:

```json
{
  "system": { "machineName": "SERVER01", "processorCount": 8, "uptime": "5.12:34:56" },
  "counts": { "processes": 234, "threads": 3456, "handles": 125000 },
  "memory": { "loadPercent": 65, "physicalUsedBytes": 22286385152 },
  "kernelMemory": { "pagedBytes": 412876800, "nonPagedBytes": 110313472 }
}
```

---

## Coming Soon

- **Kernel dump analysis** - Full analysis of MEMORY.DMP files, including stack traces, crash analysis, and kernel state reconstruction

See [GitHub Issues](https://github.com/laveeshb/atlas/issues?q=label%3A%22area%3A+kernel%22) for progress.
