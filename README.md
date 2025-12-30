# Atlas

Sysinternals + AI.

## What is this?

Sysinternals gives you x-ray vision into Windows. Atlas lets AI interpret what you're seeing.

Instead of memorizing Process Explorer, TCPView, WinDbg - you just ask:
- "What's using all the CPU?"
- "Why did this machine crash?"
- "What process is connecting to this IP?"

## How it works

Atlas is an MCP server. It exposes Windows system APIs to AI assistants, letting them investigate processes, memory dumps, and network activity on your behalf.

## MVP Focus

| Feature | Status |
|---------|--------|
| Process analysis | Planned |
| Memory dump analysis | Planned |
| Network connections | Planned |

## Tech Stack

- **C# / .NET 8** - MCP server, live system APIs
- **Rust** - Dump parsing engine

## Project Structure

```
atlas/
├── src/
│   └── Atlas.Server/           # MCP server (C#)
├── rust/
│   └── atlas-dump-core/        # Dump parser (Rust)
└── README.md
```

## Building

```bash
# Build C# server
dotnet build src/Atlas.Server

# Build Rust library
cd rust/atlas-dump-core
cargo build --release
```

## License

Private.
