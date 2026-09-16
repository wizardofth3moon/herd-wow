# Herd WoW Installer

This is the installer application for Herd WoW, a World of Warcraft 3.3.5a private server client.

## Features

- Downloads the WoW client from GitHub releases (split into parts for large files)
- Assembles and extracts the client automatically
- Downloads and installs the Herd WoW Launcher
- Creates desktop shortcuts
- Modern, dark-themed UI with progress tracking

## Building

Requirements:
- .NET 10.0 SDK or later
- Windows (targets win-x64)

To build the installer:

```bash
.\build.bat
```

The compiled installer will be in `build\HerdWoW-Setup.exe`

## How It Works

1. **Download**: Fetches client archive parts from GitHub releases (`patches-v1` tag)
2. **Verify**: Checks SHA256 hashes of downloaded parts
3. **Assemble**: Concatenates parts into a single archive
4. **Extract**: Extracts the WoW client to the chosen directory
5. **Install Launcher**: Downloads and places WowLauncher.exe
6. **Create Shortcut**: Adds a desktop shortcut

## Configuration

Edit `Config.cs` to customize:
- Server name
- Default install path
- GitHub release URLs
- Launcher executable name

## Recent Fixes

- **v1.0.1**: Fixed "cannot determine compressed stream type" error by using `ArchiveFactory.OpenArchive()` instead of `ReaderFactory.OpenReader()` for more robust archive detection after assembly.
