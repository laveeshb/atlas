# Remote Debugging Guide

This guide covers analyzing crash dumps on remote machines using WinDbg's remote debugging protocol.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Setup](#setup)
- [Usage](#usage)
- [Security](#security)
- [Troubleshooting](#troubleshooting)

---

## Overview

When crash dumps are on a secured cloud VM that you can't copy files from, Atlas can connect remotely using WinDbg's debug server protocol (dbgsrv).

**Key benefits:**
- Analyze multi-GB dumps without downloading them
- Keep sensitive data in the secured environment
- Use standard Windows debugging infrastructure

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Debug VM (VM2)                                    │
│  ┌─────────────────┐    ┌────────────────────────────────────────────┐  │
│  │ C:\dumps\       │    │  dbgsrv.exe -t tcp:port=5005               │  │
│  │ app.dmp         │◀───│  (part of Debugging Tools for Windows)     │  │
│  │ crash.dmp       │    │  Listens for WinDbg protocol connections   │  │
│  └─────────────────┘    └─────────────────────────────────────────────┘ │
│                                      ▲                                   │
└──────────────────────────────────────┼───────────────────────────────────┘
                                       │ TCP (WinDbg protocol)
                                       │
┌──────────────────────────────────────┼───────────────────────────────────┐
│                        Developer Machine (mc00)                          │
│  ┌───────────────────────────────────┴──────────────────────────────────┐│
│  │  Atlas.Server                                                        ││
│  │  - Spawns local cdb.exe with -remote connection string               ││
│  │  - Sends WinDbg commands: .opendump, !analyze, !dumpheap             ││
│  │  - Parses text output into structured JSON                           ││
│  └───────────────────────────────────────────────────────────────────────┘│
│                              ▲                                            │
│                              │ MCP                                        │
│  ┌───────────────────────────┴───────────────────────────────────────────┐│
│  │  VS Code + GitHub Copilot                                             ││
│  │  "Analyze the crash dump on debug-vm"                                 ││
│  └───────────────────────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────────────┘
```

---

## Setup

### Prerequisites

**On Debug VM (VM2):**
- Windows SDK / Debugging Tools for Windows (includes dbgsrv.exe)
- Firewall rule allowing inbound TCP on debug port
- Dump files accessible locally

**On Developer Machine (mc00):**
- Windows SDK / Debugging Tools for Windows (includes cdb.exe)
- Atlas.Server
- Network access to debug VM's port
- cdb.exe in PATH (or specify full path)

### Step 1: Start Debug Server on VM2

Basic (no authentication):
```cmd
dbgsrv -t tcp:port=5005
```

With password:
```cmd
dbgsrv -t tcp:port=5005,password=YourSecretPassword
```

With SSL encryption:
```cmd
dbgsrv -t ssl:port=5005,password=YourSecretPassword
```

With IP allowlist:
```cmd
dbgsrv -t tcp:port=5005,password=YourSecretPassword -ipportaccess 10.0.0.100
```

### Step 2: Configure Firewall on VM2

```powershell
# Allow inbound connections on debug port
New-NetFirewallRule -DisplayName "WinDbg Remote Debug" `
    -Direction Inbound -Protocol TCP -LocalPort 5005 `
    -Action Allow -RemoteAddress 10.0.0.0/24
```

### Step 3: Verify Connection from mc00

```cmd
cdb -remote tcp:server=vm2.corp.net,port=5005,password=YourSecretPassword
```

If you get a debugger prompt, connection is working. Type `q` to quit.

---

## Usage

### Analyze a Crash

Ask Copilot:
> "Analyze the crash dump at C:\dumps\app.dmp on debug server tcp:server=vm2,port=5005"

Or use the tool directly:
```json
{
  "tool": "remote_analyze_crash",
  "arguments": {
    "connectionString": "tcp:server=vm2.corp.net,port=5005",
    "dumpPath": "C:\\dumps\\app.dmp",
    "password": "YourSecretPassword"
  }
}
```

### Get Heap Statistics

> "Show heap stats from the dump on the debug server"

### Get Stack Trace

> "Show the managed stack trace from the remote dump"

For native code:
> "Show the native stack trace from C:\dumps\app.dmp on vm2"

### List Modules

> "What modules were loaded in the crashed process on the debug VM?"

### Run Custom Command

> "Run !pe on the remote dump to show the exception"

---

## Security

### Authentication Options

| Method | Connection String | Security Level |
|--------|-------------------|----------------|
| None | `tcp:server=vm2,port=5005` | ⚠️ Not recommended |
| Password | `tcp:server=vm2,port=5005,password=secret` | Basic |
| SSL + Password | `ssl:server=vm2,port=5005,password=secret` | Recommended |
| Named Pipe | `npipe:server=vm2,pipe=DebugPipe` | Uses Windows auth |

### Best Practices

1. **Use SSL** for encrypted traffic
2. **Use IP allowlists** on dbgsrv
3. **Store passwords** in environment variables, not in code
4. **Rotate passwords** periodically
5. **Limit firewall rules** to specific source IPs/subnets
6. **Stop dbgsrv** when not in use

### Environment Variable for Password

```powershell
# Set in environment
$env:DEBUG_SERVER_PASSWORD = "YourSecretPassword"
```

Then use in Atlas without exposing password in chat:
> "Analyze dump at C:\dumps\app.dmp on tcp:server=vm2,port=5005 using password from environment"

---

## Troubleshooting

### "Failed to start cdb.exe"

**Cause:** Debugging Tools for Windows not installed or not in PATH.

**Fix:** 
1. Install Windows SDK with "Debugging Tools" option
2. Add to PATH: `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64`

### "Failed to connect to debug server"

**Causes:**
- dbgsrv not running on target
- Firewall blocking connection
- Wrong hostname/port
- Wrong password

**Fix:**
1. Verify dbgsrv is running: `tasklist | findstr dbgsrv` on VM2
2. Check firewall rules on VM2
3. Test connection: `cdb -remote tcp:server=vm2,port=5005`

### "Timed out waiting for debugger response"

**Cause:** Debug server is busy or dump is very large.

**Fix:**
- Wait longer (large dumps take time to load)
- Check if dbgsrv is responding

### "Cannot open dump file"

**Causes:**
- Wrong path
- File doesn't exist
- Permissions issue

**Fix:**
1. Verify path exists on remote machine
2. Check file permissions

---

## References

- [WinDbg Remote Debugging](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/remote-debugging)
- [DbgSrv Command Line](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/dbgsrv-command-line-options)
- [Activating a Debugging Server](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/activating-a-debugging-server)
- [Process Server Examples](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/process-server-examples)
