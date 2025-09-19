# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 言語設定
**すべての回答は日本語で行ってください。**

## Development Commands

### Building and Running
```bash
# Build the project
cmd.exe /c "dotnet build"

# Run the application
cmd.exe /c "dotnet run"

# Force terminate running instances before rebuild
cmd.exe /c "taskkill /IM DesktopMascotEnhanced.exe /F 2>nul || echo 'No process found'"

# Release build
cmd.exe /c "dotnet build -c Release"

# Single file publish for Windows
cmd.exe /c "dotnet publish -c Release -r win-x64 --self-contained"
```

## Architecture Overview

This is a WPF (.NET 8.0-windows) desktop mascot application with advanced features including RSS feeds, VOICEVOX voice synthesis, real-time lip sync, and weather display.

### Core Components

**DesktopMascotEnhanced.cs** contains all application logic in a single file:

- **Win32Api**: P/Invoke definitions for window transparency and click-through functionality
- **RssArticle/RssFeedConfig**: Data models for RSS articles with multiple feed support (up to 30 articles)
- **WeatherData/WeatherService**: Weather information integration using Open-Meteo API
- **VoiceVoxService**: VOICEVOX voice synthesis integration with audio callback system
- **MascotSettings**: JSON configuration persistence for all settings including voice synthesis
- **RssService**: Multi-feed RSS fetching with thumbnail extraction and duplicate removal
- **SettingsWindow**: Comprehensive settings UI (500x500px) with tabbed interface for basic settings, voice synthesis, and RSS feed management
- **FeedEditDialog**: RSS feed editing dialog (400x220px) with validation
- **SpeechBubbleWindow**: Article display popup with navigation and manual control
- **MascotWindow**: Main mascot with animations, blinking, lip sync, and weather display

### Key Features

#### RSS & Content Management
- **Multi-Feed Support**: Configurable RSS feeds with individual enable/disable
- **Article Limit**: 30 articles maximum with duplicate removal and date sorting
- **Manual Control**: Speech bubble only shows on click (auto-advance disabled)

#### Voice Synthesis & Lip Sync
- **VOICEVOX Integration**: Text-to-speech with configurable speaker selection
- **Real-time Lip Sync**: NAudio-based audio analysis for mouth movement
- **Image System**: Support for `_mouth1.png`, `_mouth2.png`, etc. for lip sync animation
- **Audio Synchronization**: Precise timing control with MediaPlayer callbacks

#### Animation System
- **Blinking**: Automatic blinking using `_blink.png` images with dynamic intervals
- **Hop Animation**: 15-second idle animation with 1.2x scale transformation
- **Lip Sync**: Real-time mouth movement during voice playback only
- **Image Management**: Automatic loading of animation images with consistent sizing (150px width)

#### Weather Integration
- **Location**: Tokyo area weather display at top of mascot window
- **Data Source**: Open-Meteo API with hourly updates
- **Display**: Weather text with temperature range in styled TextBlock

### Image Asset Requirements
- **Base Image**: Main mascot image
- **Blinking**: `[filename]_blink.png` for eye closing animation
- **Lip Sync**: `[filename]_mouth1.png`, `[filename]_mouth2.png`, etc. (full body images with different mouth shapes)
- **Sizing**: All images should maintain consistent aspect ratio, processed at 150px width

### Configuration
Settings are stored in JSON at: `%AppData%\DesktopMascot\settings.json`

### Window Layout
- **Mascot Window**: 150x300px (mascot 150x270px + 30px for weather)
- **Settings Window**: 500x500px with tabbed interface
- **Speech Bubble**: 420x280px, positioned left of mascot, manual display only
- **Feed Edit Dialog**: 400x220px modal dialog

### Dependencies
- **NAudio 2.2.1**: Audio analysis for lip sync
- **System.Net.Http 4.3.4**: RSS and API requests
- **System.Text.Json 8.0.5**: JSON configuration and API parsing