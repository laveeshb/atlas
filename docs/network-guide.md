# Network Investigation Guide

This guide covers investigating network-related issues: identifying connections, finding which processes are using network resources, and troubleshooting connectivity.

## Table of Contents

- [Getting Started](#getting-started)
- [Finding Connections by IP](#finding-connections-by-ip)
- [Port Investigation](#port-investigation)
- [Process Network Activity](#process-network-activity)
- [Common Scenarios](#common-scenarios)
- [Tips and Best Practices](#tips-and-best-practices)

---

## Getting Started

### Available Network Tools

| Tool | Description |
|------|-------------|
| `list_network_connections` | Active TCP connections with owning process |
| `list_tcp_listeners` | TCP ports being listened on |

### Quick Commands

```
"What network connections are active?"
"What ports are being listened on?"
"What process is connecting to 10.0.0.50?"
"What's listening on port 443?"
```

---

## Finding Connections by IP

### Outbound Connections

```
"What processes are connecting to 10.0.0.50?"
```

Atlas uses `list_network_connections` and filters by remote address:

| Local | Remote | State | PID | Process |
|-------|--------|-------|-----|---------|
| 192.168.1.100:52341 | 10.0.0.50:443 | ESTABLISHED | 1234 | MyApp.exe |
| 192.168.1.100:52342 | 10.0.0.50:443 | ESTABLISHED | 1234 | MyApp.exe |

### Investigating the Process

Once you have the PID:

```
"Show details for process 1234"
```

Atlas uses `get_process_details` to show:
- Full command line
- Executable path
- Parent process
- Loaded modules

### Connection States

| State | Meaning |
|-------|---------|
| ESTABLISHED | Active connection |
| TIME_WAIT | Connection closed, waiting for timeout |
| CLOSE_WAIT | Remote closed, waiting for local close |
| SYN_SENT | Connection attempt in progress |
| LISTENING | (Use `list_tcp_listeners` instead) |

---

## Port Investigation

### Finding What's Listening

```
"What's listening on port 8080?"
```

Atlas uses `list_tcp_listeners` to show:

| Address | Port | PID | Process |
|---------|------|-----|---------|
| 0.0.0.0 | 8080 | 5678 | node.exe |

### Common Port Conflicts

When an app fails to start with "port already in use":

```
"What's using port 443?"
```

Common culprits:
- IIS (80, 443)
- SQL Server (1433)
- Development servers (3000, 5000, 8080)

### Listening on Specific Interfaces

| Address | Meaning |
|---------|---------|
| 0.0.0.0 | All IPv4 interfaces |
| 127.0.0.1 | Localhost only |
| 192.168.1.100 | Specific interface |

---

## Process Network Activity

### Full Network Profile of a Process

```
"Show all network activity for chrome.exe"
```

1. Get PID(s) for the process
2. Filter connections by those PIDs
3. Show both connections and listeners

### Identifying Network-Heavy Processes

```
"Which processes have the most network connections?"
```

Look for:
- Unusually high connection counts
- Connections to unexpected destinations
- Processes that shouldn't have network activity

---

## Common Scenarios

### Scenario: Application Can't Connect

**Symptoms:** App fails with connection timeout or refused

**Investigation:**
```
"Is anything listening on port 5432?"
```

If nothing is listening:
- Service not started
- Wrong port configuration
- Service crashed

If something is listening:
- Firewall blocking
- Binding to wrong interface (127.0.0.1 vs 0.0.0.0)

### Scenario: Unexpected Outbound Connections

**Symptoms:** Firewall logs show unexpected traffic

**Investigation:**
```
"What processes are connecting to external IPs?"
```

```
"Show details for process 9876"
```

Look for:
- Unknown executables
- Processes connecting to suspicious IPs
- Unexpected processes with network activity

### Scenario: Port Already in Use

**Symptoms:** Service fails to start

**Investigation:**
```
"What's using port 80?"
```

Common solutions:
- Stop the conflicting service
- Change port configuration
- Check for zombie processes (TIME_WAIT with old PID)

### Scenario: Connection Leak

**Symptoms:** Application gradually uses more connections until limit hit

**Investigation:**
```
"How many connections does MyApp have?"
```

Compare over time. If growing without bound:
- Connections not being closed
- Connection pooling issues
- Resource exhaustion

---

## Tips and Best Practices

### Combining with Process Tools

Network issues often need process context:

1. Find the connection → get PID
2. Get process details → understand what's running
3. Check process tree → find parent/related processes
4. Check command line → see configuration

### Remote Investigation

Process tools work on remote machines:

```
"List processes on SERVER01"
"Show process details for PID 1234 on SERVER01"
```

Note: Network tools currently work on local machine only.

### Security Considerations

Network activity can reveal:
- Internal service architecture
- Database connection strings (from process command lines)
- API endpoints and authentication details

### Common False Positives

**High connection counts for:**
- Web browsers (many connections to CDNs, trackers)
- Database connection pools (intentionally kept open)
- Load balancers / reverse proxies

**TIME_WAIT connections:**
- Normal after connection closes
- Doesn't indicate a problem unless overwhelming

### Correlating with Firewall Logs

Windows Firewall logs:
1. Enable logging: Windows Security → Firewall → Advanced Settings
2. Check: `C:\Windows\System32\LogFiles\Firewall\pfirewall.log`
3. Correlate timestamps with Atlas output

---

## Limitations

Current limitations of network tools:

- **TCP only** - UDP connections not yet enumerated
- **IPv4 focus** - IPv6 support in progress (see #40)
- **Local only** - Remote network queries not yet supported
- **No historical data** - Shows current state only

See [GitHub Issues](https://github.com/laveeshb/atlas/issues?q=label%3A%22area%3A+network%22) for planned improvements.
