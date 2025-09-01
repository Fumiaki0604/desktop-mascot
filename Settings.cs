using System;
using System.IO;
using Newtonsoft.Json;
using System.Drawing;

namespace DesktopMascot
{
    public class AppSettings
    {
        public string ImagePath { get; set; } = "";
        public float Scale { get; set; } = 1.0f;
        public float Opacity { get; set; } = 1.0f;
        public int Rotation { get; set; } = 0;
        public bool TopMost { get; set; } = true;
        public bool ClickThrough { get; set; } = false;
        public PositionInfo Position { get; set; } = new PositionInfo();
        public int Fps { get; set; } = 30;
        public string[] Feeds { get; set; } = { "https://www.gizmodo.jp/index.xml" };
        public int FeedRefreshMinutes { get; set; } = 10;
        public RssSettings Rss { get; set; } = new RssSettings();
        public BubbleSettings Bubble { get; set; } = new BubbleSettings();
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
    }

    public class PositionInfo
    {
        public int Screen { get; set; } = 1;
        public int X { get; set; } = 1200;
        public int Y { get; set; } = 800;
    }

    public class RssSettings
    {
        public string DisplayMode { get; set; } = "title+summary";
        public bool AlwaysShowBubble { get; set; } = true;
        public int PerItemSeconds { get; set; } = 15;
        public int MaxSummaryChars { get; set; } = 120;
        public bool PauseOnHover { get; set; } = true;
        public string[] HighlightWords { get; set; } = { "Wii" };
        public int FadeMs { get; set; } = 150;
    }

    public class BubbleSettings
    {
        public int Width { get; set; } = 320;
        public int Padding { get; set; } = 20;
        public int OffsetX { get; set; } = -340;
        public int OffsetY { get; set; } = -60;
        public int CornerRadius { get; set; } = 12;
        public float Opacity { get; set; } = 0.92f;
        public string FontName { get; set; } = "Meiryo UI";
        public int FontSize { get; set; } = 12;
        public int LineClamp { get; set; } = 6;
        public bool ShowOpenHint { get; set; } = true;
        public bool ShowDescription { get; set; } = true;
        public int MaxDescriptionLength { get; set; } = 150;
        public bool Stroke { get; set; } = true;
        public PointerSettings Pointer { get; set; } = new PointerSettings();
        public bool CloseButton { get; set; } = true;
    }

    public class PointerSettings
    {
        public int W { get; set; } = 12;
        public int H { get; set; } = 10;
    }

    public class HotkeySettings
    {
        public string NextKey { get; set; } = "N";
        public string PrevKey { get; set; } = "P";
        public bool WheelWithCtrl { get; set; } = true;
    }

    public static class Settings
    {
        private static AppSettings _current = new AppSettings();
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopMascot", "settings.json");

        public static AppSettings Current => _current;

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _current = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    CreateDefaultSettings();
                    Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定読み込みエラー: {ex.Message}");
                _current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定保存エラー: {ex.Message}");
            }
        }

        private static void CreateDefaultSettings()
        {
            _current = new AppSettings();
        }

        public static void SaveWindowPosition(Point location, int screenIndex = 1)
        {
            _current.Position.X = location.X;
            _current.Position.Y = location.Y;
            _current.Position.Screen = screenIndex;
            Save();
        }

        public static Point GetWindowPosition()
        {
            return new Point(_current.Position.X, _current.Position.Y);
        }

        public static void UpdateImagePath(string path)
        {
            _current.ImagePath = path;
            Save();
        }
    }
}