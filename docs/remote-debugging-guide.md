# Remote Debugging Guide

This guide covers analyzing crash dumps on remote machines using `remote.exe`.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Setup](#setup)
- [Usage](#usage)
- [Security](#security)
- [Troubleshooting](#troubleshooting)

---

## Overview

When crash dumps are on a secured cloud VM that you can't copy files from, Atlas can connect remotely using Microsoft's `remote.exe` utility.

**Key benefits:**
- Analyze multi-GB dumps without downloading them
- Keep sensitive data in the secured environment
- Use standard Windows debugging infrastructure
- **Persistent sessions** - server stays up for multiple queries
- **Multiple clients** can connect to the same session

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Debug VM (VM2)                                    │
│  ┌─────────────────┐    ┌────────────────────────────────────────────┐  │
│  │ C:\dumps\       │    │  remote.exe /s "cdb -z dump.dmp" Session   │  │
│  │ app.dmp         │◀───│  (part of Debugging Tools for Windows)     │  │
│  │ crash.dmp       │    │  Loads dump and exposes named session      │  │
│  └─────────────────┘    └─────────────────────────────────────────────┘ │
│                                      ▲                                   │
└──────────────────────────────────────┼───────────────────────────────────┘
                                       │ SMB (port 445)
                                       │ Named Pipes
┌──────────────────────────────────────┼───────────────────────────────────┐
│                        Developer Machine (mc00)                          │
│  ┌───────────────────────────────────┴──────────────────────────────────┐│
│  │  Atlas.Server                                                        ││
│  │  - Uses remote.exe /c to connect to named session                    ││
│  │  - Sends WinDbg commands: !analyze, !dumpheap, lm, k                 ││
│  │  - Parses text output into structured JSON                           ││
│  └───────────────────────────────────────────────────────────────────────┘│
│                              ▲                                            │
│                              │ MCP                                        │
│  ┌───────────────────────────┴───────────────────────────────────────────┐│
│  │  VS Code + GitHub Copilot                                             ││
│  │  "Analyze the crash on vm2/DumpSession"                               ││
│  └───────────────────────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────────────┘
```

> **Note:** Using `remote.exe` instead of `cdb -server` provides persistent
> sessions that stay alive for multiple queries.

---

## Setup

### Prerequisites

**On Debug VM (VM2):**
- Windows SDK / Debugging Tools for Windows (includes remote.exe, cdb.exe)
- Firewall allowing SMB (port 445) from developer machine
- Dump files accessible locally

**On Developer Machine (mc00):**
- Windows SDK / Debugging Tools for Windows (includes remote.exe)
- Atlas.Server
- Network access to debug VM (SMB port 445)
- remote.exe in PATH

### Step 1: Start Debug Session on VM2

Start a named session with the dump loaded:

```cmd
remote.exe /s "cdb -z C:\dumps\app.dmp" DumpSession
```

This creates a session named "DumpSession" that anyone can connect to.

**Options:**
- Session name can be anything descriptive (e.g., `CrashAnalysis`, `Issue123`)
- Multiple sessions can run simultaneously with different names

### Step 2: Configure Firewall on VM2

```powershell
# Allow SMB from specific network (required for remote.exe)
New-NetFirewallRule -DisplayName "Remote Debug (SMB)" `
    -Direction Inbound -Protocol TCP -LocalPort 445 `
    -Action Allow -RemoteAddress 10.0.0.0/24
```

### Step 3: Verify Connection from mc00

```cmd
remote.exe /c VM2 DumpSession
```

If you get a debugger prompt, connection is working. Type `q` to disconnect (session stays alive).

---

## Usage

### Connection String Format

Atlas supports these formats:
- `hostname/session` - e.g., `vm2/DumpSession`
- `server=hostname,session=name` - e.g., `server=vm2,session=DumpSession`

### Analyze a Crash

Ask Copilot:
> "Analyze the crash on vm2/DumpSession"

Or use the tool directly:
```json
{
  "tool": "remote_analyze_crash",
  "arguments": {
    "connectionString": "vm2/DumpSession"
  }
}
```

### Get Heap Statistics

> "Show heap stats from vm2/DumpSession"

### Get Stack Trace

> "Show the managed stack trace from vm2/DumpSession"

For native code:
> "Show the native stack trace from vm2/DumpSession"

### List Modules

> "What modules were loaded in vm2/DumpSession?"

### Run Custom Command

> "Run !pe on vm2/DumpSession to show the exception"

---

## Security

### Access Control

`remote.exe` uses Windows named pipes over SMB (port 445). Access is controlled by:
- **Network access** to the server via SMB
- **Windows file sharing permissions** on the machine
- **Firewall rules** restricting source IPs

### Best Practices

1. **Restrict SMB access** to specific subnets in firewall rules
2. **Use VPN or private network** for connections
3. **Stop the session** when not in use (press `x` in the server console)
4. **Use descriptive session names** to track active sessions
5. **Limit who can access** the debug VM via Windows permissions

### Session Management

List active sessions on VM2:
```cmd
remote.exe /q VM2
```

Stop a session from the server:
- Press `x` in the remote.exe server window

---

## Troubleshooting

### "Failed to start remote.exe"

**Cause:** Debugging Tools for Windows not installed or not in PATH.

**Fix:** 
1. Install Windows SDK with "Debugging Tools" option
2. Add to PATH: `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64`

### "Failed to connect to session"

**Causes:**
- Session not running on target
- Firewall blocking SMB (port 445)
- Wrong hostname or session name
- Network connectivity issue

**Fix:**
1. Verify session is running: `remote.exe /q VM2` from client
2. Check firewall allows SMB from your network
3. Test: `remote.exe /c VM2 SessionName`

### "Timed out waiting for debugger response"

**Cause:** Debug session is busy or dump is very large.

**Fix:**
- Wait longer (large dumps take time to process commands)
- Check if session is responding via direct `remote.exe /c` connection

### "Session not found"

**Causes:**
- Session name typo
- Session was stopped
- Wrong server name

**Fix:**
1. Query available sessions: `remote.exe /q VM2`
2. Verify correct session name and server

---

## References

- [Remote.exe Tool](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/the-remote-exe-utility)
- [Starting a Remote.exe Session](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/starting-a-remote-exe-session)
- [WinDbg Remote Debugging](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/remote-debugging)
- [Debugging Tools for Windows](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/)
