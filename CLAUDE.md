# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Building and Running
```bash
# Debug build with hot reload (recommended for development)
dotnet watch run

# Standard debug run
dotnet run

# Release build
dotnet build -c Release

# Publish as single executable for Windows
dotnet publish -c Release -r win-x64 --self-contained
```

## Architecture Overview

This is a WPF (.NET 8.0-windows) desktop mascot application that displays RSS articles from configurable news sources with thumbnail support and automatic article rotation.

### Core Components

**DesktopMascotEnhanced.cs** contains all application logic in a single file:

- **Win32Api**: P/Invoke definitions for window transparency and click-through functionality
- **RssArticle**: Data model for RSS articles with thumbnail URL support
- **MascotSettings**: Configuration persistence (JSON) for mascot image, RSS URL, and window position
- **RssService**: HTTP-based RSS fetching with thumbnail extraction from multiple media tag formats (enclosure, media:thumbnail, media:content)
- **SettingsWindow**: Configuration UI for mascot image selection and RSS URL management with preset options
- **SpeechBubbleWindow**: Article display popup with navigation, auto-advance timer (15 seconds), and responsive layout
- **MascotWindow**: Main mascot window with drag functionality, context menu, animation system, and RSS management
- **App**: Application entry point and window management

### Key Features

- **RSS Integration**: Configurable RSS feeds with automatic thumbnail extraction and 30-minute refresh cycle
- **Auto-Advance**: 15-second timer for automatic article progression with user interaction reset
- **UI Layout**: Speech bubble with title at top, horizontal layout below (left: thumbnail, right: description)
- **Window Management**: Transparent, always-on-top mascot with click-through toggle and position persistence
- **Animation System**: Mascot scaling animations on hover/interaction with idle timer
- **Loop Navigation**: Seamless article navigation with wraparound at boundaries

### Configuration
Settings are stored in JSON at: `%AppData%\DesktopMascot\settings.json`

### Window Positioning
- Mascot: 150x270px with configurable position persistence
- Speech Bubble: 420x280px positioned relative to mascot (left-aligned, slight offset)
- Auto-positioning updates when mascot is dragged