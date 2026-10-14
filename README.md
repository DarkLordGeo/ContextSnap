# ContextSnap

A lightweight Windows utility that captures desktop context and copies it as Markdown for manual pasting into an LLM.

Planned sources include application windows, File Explorer paths, browser tabs, and Desktop entries.

## Requirements
- Windows
- .NET 10 SDK

## Development
```powershell
dotnet build
dotnet test
dotnet run --project src/ContextSnap
```

## Status
Early development. Context collection, browser integration, clipboard support, and global hotkey are not yet implemented.
