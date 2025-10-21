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
- **GIF Animation**: Article navigation triggers animated GIF playback (1-loop, ~1 second)
- **Lip Sync**: Real-time mouth movement during voice playback only
- **Image Management**: Automatic loading of animation images with consistent sizing (150px width)
- **GIF Library**: Uses WpfAnimatedGif 2.0.2 for smooth GIF animation playback

#### Weather Integration
- **Location**: Tokyo area weather display at top of mascot window
- **Data Source**: Open-Meteo API with hourly updates
- **Display**: Weather text with temperature range in styled TextBlock

### Image Asset Requirements
- **Base Image**: Main mascot image
- **Blinking**: `[filename]_blink.png` for eye closing animation
- **Lip Sync**: `[filename]_mouth1.png`, `[filename]_mouth2.png`, etc. (full body images with different mouth shapes)
- **GIF Animation**: `rolling_light.gif` (or custom path via `AnimationGifPath` setting) for article navigation animation
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
- **WpfAnimatedGif 2.0.2**: Animated GIF playback for article navigation animation
- **System.Net.Http 4.3.4**: RSS and API requests
- **System.Text.Json 8.0.5**: JSON configuration and API parsing

### Animation Triggers
- **GIF Animation**: Plays once when navigating between articles (Next/Previous buttons or auto-advance)
- **No Idle Animation**: GIF does NOT play during idle time, only on article navigation
- **Fallback**: If GIF file not found, no animation plays (scale animation removed)

## 🚧 実装中の機能: 技術ブログ統合 (Qiita/Zenn)

### 実装済み (Phase 1-3)
✅ **データモデル拡張**
- `ArticleSourceType` 列挙型追加 (RSS/TechBlog)
- `RssArticle` に `SourceType`, `AuthorName`, `Tags` プロパティ追加
- `TechBlogSettings` クラス作成（Qiita/Zenn設定）
- `MascotSettings` に `TechBlog` プロパティ追加

✅ **QiitaService実装**
- タグ検索機能 (デフォルト: C#, WPF, .NET, AI, 機械学習)
- タイムライン機能 (要アクセストークン)
- `QiitaItem`, `QiitaUser`, `QiitaTag` モデルクラス

✅ **ZennService実装**
- RSS経由でユーザー記事取得 (https://zenn.dev/{username}/feed)
- RSS経由でトピック記事取得 (https://zenn.dev/topics/{topic}/feed)
- デフォルトトピック: csharp, dotnet, ai, nextjs

### 未実装 (Phase 4-7)
⏳ **ArticleAggregatorService** - RSS/Qiita/Zennの記事を統合管理
⏳ **SpeechBubbleWindow拡張** - タブUI追加 (📰 RSS / 💻 技術ブログ)
⏳ **MascotWindow拡張** - タブ切り替えロジック、記事インデックス管理
⏳ **SettingsWindow拡張** - 技術ブログ設定タブ追加

### ユーザー情報
- Qiita: @Fumiaki0604
- Zenn: fumiaki sato

### 実装設計
詳細な設計は以下のドキュメントを参照:
- タブUIは `SpeechBubbleWindow` に追加（📰 RSS / 💻 技術ブログ）
- 各タブごとに独立した記事インデックスを管理
- タグ表示は技術ブログタブのみ（最大3つまで表示）
- 記事取得は非同期並列実行、重複はURLベースで削除
- 最大30件の記事を保持（RSS + 技術ブログ合計）