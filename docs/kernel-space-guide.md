# Kernel-Space Investigation Guide

This guide covers investigating kernel-level issues: driver problems, BSODs, system stability, and kernel resource usage.

## Table of Contents

- [Getting Started](#getting-started)
- [Driver Analysis](#driver-analysis)
- [BSOD Investigation](#bsod-investigation)
- [Security Auditing](#security-auditing)
- [Tips and Best Practices](#tips-and-best-practices)

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

## Coming Soon

The following kernel investigation capabilities are planned:

- **Pool memory analysis** - Track kernel memory usage by pool tag
- **Handle leak detection** - Find processes leaking kernel handles
- **Kernel thread analysis** - Inspect kernel stacks and DPC latency
- **Kernel dump analysis** - Full analysis of MEMORY.DMP files

See [GitHub Issues](https://github.com/laveeshb/atlas/issues?q=label%3A%22area%3A+kernel%22) for progress.
