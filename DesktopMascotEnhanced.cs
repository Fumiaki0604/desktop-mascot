using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesktopMascot
{
    /// <summary>
    /// Windows API用のP/Invoke定義
    /// </summary>
    public static class Win32Api
    {
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll")]
        public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        public const int GWL_EXSTYLE = -20;
        public const uint WS_EX_LAYERED = 0x80000;
        public const uint WS_EX_TRANSPARENT = 0x20;
        public const uint LWA_ALPHA = 0x2;
        public const uint LWA_COLORKEY = 0x1;
    }

    /// <summary>
    /// RSS記事データ
    /// </summary>
    public class RssArticle
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Link { get; set; } = "";
        public string PubDate { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
    }

    /// <summary>
    /// 天気情報データ
    /// </summary>
    public class WeatherData
    {
        public string WeatherCode { get; set; } = "";
        public string WeatherText { get; set; } = "";
        public int? MaxTemp { get; set; }
        public int? MinTemp { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// マスコット設定クラス
    /// </summary>
    public class MascotSettings
    {
        public string ImagePath { get; set; } = "";
        public string RssUrl { get; set; } = "https://www.gizmodo.jp/index.xml";
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopMascot", "settings.json");

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定保存エラー: {ex.Message}");
            }
        }

        public static MascotSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return System.Text.Json.JsonSerializer.Deserialize<MascotSettings>(json) ?? new MascotSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定読込エラー: {ex.Message}");
            }
            return new MascotSettings();
        }
    }

    /// <summary>
    /// RSSサービスクラス（URL変更可能）
    /// </summary>
    public class RssService
    {
        private readonly HttpClient _httpClient;
        private string _rssUrl;

        public List<RssArticle> Articles { get; private set; } = new List<RssArticle>();
        public DateTime LastUpdate { get; private set; }
        public string CurrentRssUrl => _rssUrl;

        public RssService(string rssUrl)
        {
            _rssUrl = rssUrl;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void SetRssUrl(string newUrl)
        {
            _rssUrl = newUrl;
        }

        public async Task<bool> FetchRssAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(_rssUrl);
                var doc = XDocument.Parse(response);

                var articles = doc.Descendants("item")
                    .Take(10)
                    .Select(item => new RssArticle
                    {
                        Title = CleanText(item.Element("title")?.Value ?? ""),
                        Description = CleanHtml(item.Element("description")?.Value ?? ""),
                        Link = item.Element("link")?.Value ?? "",
                        PubDate = item.Element("pubDate")?.Value ?? "",
                        ThumbnailUrl = GetThumbnailUrl(item)
                    })
                    .ToList();

                Articles = articles;
                LastUpdate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RSS取得エラー: {ex.Message}");
                return false;
            }
        }

        private string CleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            var cleanText = Regex.Replace(html, "<.*?>", "");
            cleanText = cleanText.Replace("&lt;", "<")
                                 .Replace("&gt;", ">")
                                 .Replace("&amp;", "&")
                                 .Replace("&quot;", "\"")
                                 .Replace("&#39;", "'");
            cleanText = Regex.Replace(cleanText, @"\s+", " ");
            return cleanText.Trim();
        }

        private string CleanText(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Trim();
        }

        private string GetThumbnailUrl(XElement item)
        {
            try
            {
                // enclosureタグから画像URLを取得
                var enclosure = item.Element("enclosure");
                if (enclosure != null)
                {
                    var type = enclosure.Attribute("type")?.Value ?? "";
                    var url = enclosure.Attribute("url")?.Value ?? "";
                    
                    // 画像ファイルの場合のみ返す
                    if (type.StartsWith("image/") && !string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
                
                // media:thumbnail (Media RSS) も確認
                var ns = XNamespace.Get("http://search.yahoo.com/mrss/");
                var mediaThumbnail = item.Element(ns + "thumbnail");
                if (mediaThumbnail != null)
                {
                    var url = mediaThumbnail.Attribute("url")?.Value ?? "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
                
                // media:content も確認
                var mediaContent = item.Element(ns + "content");
                if (mediaContent != null)
                {
                    var type = mediaContent.Attribute("type")?.Value ?? "";
                    var url = mediaContent.Attribute("url")?.Value ?? "";
                    
                    if (type.StartsWith("image/") && !string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
            }
            catch
            {
                // サムネイルURL取得エラーは無視（空文字を返す）
            }
            
            return "";
        }
    }

    /// <summary>
    /// 天気情報サービスクラス（気象庁API使用）
    /// </summary>
    public class WeatherService
    {
        private readonly HttpClient _httpClient;
        private const string JMA_API_URL = "https://www.jma.go.jp/bosai/forecast/data/forecast/130000.json";
        private const string OPEN_METEO_API_URL = "https://api.open-meteo.com/v1/forecast?latitude=35.6785&longitude=139.6823&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=Asia/Tokyo&forecast_days=1";

        public WeatherData CurrentWeather { get; private set; } = new WeatherData();

        public WeatherService()
        {
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DesktopMascot/1.0");
        }

        public async Task<bool> FetchWeatherAsync()
        {
            // まず気象庁APIを試す
            if (await FetchJmaWeatherAsync())
            {
                return true;
            }
            
            // 気象庁APIが失敗した場合、Open-MeteoAPIを試す
            System.Diagnostics.Debug.WriteLine("気象庁API失敗、Open-Meteo APIを試行中...");
            return await FetchOpenMeteoWeatherAsync();
        }

        private async Task<bool> FetchJmaWeatherAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("気象庁API: 天気情報取得開始...");
                var response = await _httpClient.GetStringAsync(JMA_API_URL);
                System.Diagnostics.Debug.WriteLine($"気象庁API: 応答受信 {response.Length} 文字");
                
                var weatherJson = System.Text.Json.JsonDocument.Parse(response);
                
                // 最初の予報データを取得
                var forecasts = weatherJson.RootElement.GetProperty("timeSeries");
                System.Diagnostics.Debug.WriteLine($"時系列データ数: {forecasts.GetArrayLength()}");
                
                if (forecasts.GetArrayLength() > 0)
                {
                    var firstForecast = forecasts[0];
                    var areas = firstForecast.GetProperty("areas");
                    System.Diagnostics.Debug.WriteLine($"地域データ数: {areas.GetArrayLength()}");
                    
                    // 東京地方の天気を取得
                    foreach (var area in areas.EnumerateArray())
                    {
                        var areaName = area.GetProperty("area").GetProperty("name").GetString();
                        System.Diagnostics.Debug.WriteLine($"地域名: {areaName}");
                        
                        if (areaName == "東京地方")
                        {
                            var weathers = area.GetProperty("weathers");
                            if (weathers.GetArrayLength() > 0)
                            {
                                CurrentWeather.WeatherText = weathers[0].GetString() ?? "";
                                CurrentWeather.WeatherCode = GetWeatherCode(CurrentWeather.WeatherText);
                                System.Diagnostics.Debug.WriteLine($"天気: {CurrentWeather.WeatherText}, アイコン: {CurrentWeather.WeatherCode}");
                            }
                            break;
                        }
                    }
                }

                // 気温データの取得
                if (forecasts.GetArrayLength() > 1)
                {
                    var tempForecast = forecasts[1];
                    var tempAreas = tempForecast.GetProperty("areas");
                    
                    foreach (var area in tempAreas.EnumerateArray())
                    {
                        var areaName = area.GetProperty("area").GetProperty("name").GetString();
                        System.Diagnostics.Debug.WriteLine($"気温地域名: {areaName}");
                        
                        if (areaName == "東京")
                        {
                            if (area.TryGetProperty("tempsMax", out var maxTemps) && maxTemps.GetArrayLength() > 0)
                            {
                                var maxTempStr = maxTemps[0].GetString();
                                System.Diagnostics.Debug.WriteLine($"最高気温文字列: {maxTempStr}");
                                if (!string.IsNullOrEmpty(maxTempStr) && int.TryParse(maxTempStr, out int maxTemp))
                                {
                                    CurrentWeather.MaxTemp = maxTemp;
                                }
                            }
                            
                            if (area.TryGetProperty("tempsMin", out var minTemps) && minTemps.GetArrayLength() > 0)
                            {
                                var minTempStr = minTemps[0].GetString();
                                System.Diagnostics.Debug.WriteLine($"最低気温文字列: {minTempStr}");
                                if (!string.IsNullOrEmpty(minTempStr) && int.TryParse(minTempStr, out int minTemp))
                                {
                                    CurrentWeather.MinTemp = minTemp;
                                }
                            }
                            break;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"気象庁API成功 - 天気: {CurrentWeather.WeatherText}, 最高: {CurrentWeather.MaxTemp}, 最低: {CurrentWeather.MinTemp}");
                CurrentWeather.LastUpdate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"気象庁API取得エラー: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> FetchOpenMeteoWeatherAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Open-Meteo API: 天気情報取得開始...");
                var response = await _httpClient.GetStringAsync(OPEN_METEO_API_URL);
                System.Diagnostics.Debug.WriteLine($"Open-Meteo API: 応答受信 {response.Length} 文字");
                
                var weatherJson = System.Text.Json.JsonDocument.Parse(response);
                var daily = weatherJson.RootElement.GetProperty("daily");
                
                // 天気コード
                if (daily.TryGetProperty("weather_code", out var weatherCodes) && weatherCodes.GetArrayLength() > 0)
                {
                    var code = weatherCodes[0].GetInt32();
                    CurrentWeather.WeatherCode = GetWeatherCodeFromWMO(code);
                    CurrentWeather.WeatherText = GetWeatherTextFromWMO(code);
                }
                
                // 最高気温
                if (daily.TryGetProperty("temperature_2m_max", out var maxTemps) && maxTemps.GetArrayLength() > 0)
                {
                    CurrentWeather.MaxTemp = (int)Math.Round(maxTemps[0].GetDouble());
                }
                
                // 最低気温
                if (daily.TryGetProperty("temperature_2m_min", out var minTemps) && minTemps.GetArrayLength() > 0)
                {
                    CurrentWeather.MinTemp = (int)Math.Round(minTemps[0].GetDouble());
                }
                
                System.Diagnostics.Debug.WriteLine($"Open-Meteo API成功 - 天気: {CurrentWeather.WeatherText}, 最高: {CurrentWeather.MaxTemp}, 最低: {CurrentWeather.MinTemp}");
                CurrentWeather.LastUpdate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open-Meteo API取得エラー: {ex.Message}");
                return false;
            }
        }

        private string GetWeatherCode(string weatherText)
        {
            // 天気テキストから簡易的な天気コードを生成
            if (weatherText.Contains("晴")) return "☀️";
            if (weatherText.Contains("曇")) return "☁️";
            if (weatherText.Contains("雨")) return "🌧️";
            if (weatherText.Contains("雪")) return "❄️";
            if (weatherText.Contains("雷")) return "⚡";
            return "🌤️"; // デフォルト
        }

        private string GetWeatherCodeFromWMO(int wmoCode)
        {
            // WMO天気コードから絵文字に変換
            return wmoCode switch
            {
                0 => "☀️", // Clear sky
                1 or 2 or 3 => "🌤️", // Mainly clear, partly cloudy, overcast
                45 or 48 => "🌫️", // Fog
                51 or 53 or 55 => "🌦️", // Drizzle
                56 or 57 => "🌧️", // Freezing drizzle
                61 or 63 or 65 => "🌧️", // Rain
                66 or 67 => "🌧️", // Freezing rain
                71 or 73 or 75 => "❄️", // Snow
                77 => "❄️", // Snow grains
                80 or 81 or 82 => "🌦️", // Rain showers
                85 or 86 => "❄️", // Snow showers
                95 or 96 or 99 => "⛈️", // Thunderstorm
                _ => "🌤️"
            };
        }

        private string GetWeatherTextFromWMO(int wmoCode)
        {
            // WMO天気コードから日本語テキストに変換
            return wmoCode switch
            {
                0 => "快晴",
                1 => "晴れ",
                2 => "薄曇り",
                3 => "曇り",
                45 or 48 => "霧",
                51 or 53 or 55 => "霧雨",
                56 or 57 => "着氷性霧雨",
                61 or 63 or 65 => "雨",
                66 or 67 => "着氷性の雨",
                71 or 73 or 75 => "雪",
                77 => "雪あられ",
                80 or 81 or 82 => "にわか雨",
                85 or 86 => "にわか雪",
                95 or 96 or 99 => "雷雨",
                _ => "不明"
            };
        }
    }

    /// <summary>
    /// 設定ウィンドウ
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public MascotSettings Settings { get; private set; }
        public bool SettingsChanged { get; private set; } = false;

        public SettingsWindow(MascotSettings currentSettings)
        {
            Settings = new MascotSettings
            {
                ImagePath = currentSettings.ImagePath,
                RssUrl = currentSettings.RssUrl,
                WindowLeft = currentSettings.WindowLeft,
                WindowTop = currentSettings.WindowTop
            };

            InitializeComponent();
            LoadCurrentSettings();
        }

        private void InitializeComponent()
        {
            Width = 450;
            Height = 300;
            Title = "デスクトップマスコット設定";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // メイングリッド
            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 画像設定
            var imageLabel = new Label { Content = "マスコット画像:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(imageLabel, 0);

            var imagePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            ImagePathTextBox = new TextBox { Width = 250, Margin = new Thickness(0, 0, 10, 0), IsReadOnly = true };
            var browseButton = new Button { Content = "参照...", Width = 80 };
            var clearButton = new Button { Content = "クリア", Width = 60, Margin = new Thickness(5, 0, 0, 0) };
            
            browseButton.Click += BrowseImage_Click;
            clearButton.Click += ClearImage_Click;

            imagePanel.Children.Add(ImagePathTextBox);
            imagePanel.Children.Add(browseButton);
            imagePanel.Children.Add(clearButton);
            Grid.SetRow(imagePanel, 1);

            // RSS URL設定
            var rssLabel = new Label { Content = "RSS URL:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(rssLabel, 2);

            RssUrlTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(RssUrlTextBox, 3);

            // プリセットボタン
            var presetPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            var gizmodoBtn = new Button { Content = "Gizmodo", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
            var itmediaBtn = new Button { Content = "ITmedia", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
            var gigazineBtn = new Button { Content = "Gigazine", Width = 80 };

            gizmodoBtn.Click += (s, e) => RssUrlTextBox.Text = "https://www.gizmodo.jp/index.xml";
            itmediaBtn.Click += (s, e) => RssUrlTextBox.Text = "https://rss.itmedia.co.jp/rss/2.0/news_bursts.xml";
            gigazineBtn.Click += (s, e) => RssUrlTextBox.Text = "https://gigazine.net/news/rss_2.0/";

            presetPanel.Children.Add(gizmodoBtn);
            presetPanel.Children.Add(itmediaBtn);
            presetPanel.Children.Add(gigazineBtn);
            Grid.SetRow(presetPanel, 4);

            mainGrid.Children.Add(imageLabel);
            mainGrid.Children.Add(imagePanel);
            mainGrid.Children.Add(rssLabel);
            mainGrid.Children.Add(RssUrlTextBox);
            mainGrid.Children.Add(presetPanel);

            Grid.SetRow(mainGrid, 0);

            // ボタンパネル
            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "キャンセル", Width = 80, IsCancel = true };

            okButton.Click += OK_Click;
            cancelButton.Click += Cancel_Click;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 1);

            grid.Children.Add(mainGrid);
            grid.Children.Add(buttonPanel);
            Content = grid;
        }

        public TextBox ImagePathTextBox { get; private set; }
        public TextBox RssUrlTextBox { get; private set; }

        private void LoadCurrentSettings()
        {
            ImagePathTextBox.Text = Settings.ImagePath;
            RssUrlTextBox.Text = Settings.RssUrl;
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "マスコット画像を選択",
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.gif;*.bmp|すべてのファイル|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ImagePathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void ClearImage_Click(object sender, RoutedEventArgs e)
        {
            ImagePathTextBox.Text = "";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Settings.ImagePath = ImagePathTextBox.Text;
            Settings.RssUrl = RssUrlTextBox.Text;
            SettingsChanged = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    /// <summary>
    /// 吹き出しウィンドウ（記事ナビゲーション機能強化）
    /// </summary>
    public partial class SpeechBubbleWindow : Window
    {
        public int CurrentArticleIndex { get; set; } = 0;
        public int TotalArticles { get; set; } = 0;
        private DispatcherTimer _autoAdvanceTimer;

        public SpeechBubbleWindow()
        {
            InitializeComponent();
            InitializeAutoAdvanceTimer();
        }

        private void InitializeAutoAdvanceTimer()
        {
            _autoAdvanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _autoAdvanceTimer.Tick += (s, e) =>
            {
                _autoAdvanceTimer.Stop();
                NextRequested?.Invoke(this, EventArgs.Empty);
            };
            
            // ウィンドウ上でのマウス操作やキー操作でタイマーリセット
            MouseMove += (s, e) => StartAutoAdvanceTimer();
            MouseLeftButtonDown += (s, e) => StopAutoAdvanceTimer();
            MouseRightButtonDown += (s, e) => StopAutoAdvanceTimer();
            KeyDown += (s, e) => StartAutoAdvanceTimer();
        }

        private void InitializeComponent()
        {
            Width = 420;  // 横幅はそのまま
            Height = 280;  // 元のサイズに戻す
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 100, 100, 100)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 3,
                    Opacity = 0.3
                }
            };

            var stackPanel = new StackPanel { Margin = new Thickness(15) };

            // ヘッダー（タイトル + ナビゲーション）
            var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            // ナビゲーションボタン
            var navPanel = new StackPanel { Orientation = Orientation.Horizontal };
            PrevButton = new Button { Content = "◀", Width = 25, Height = 25, FontSize = 10, Margin = new Thickness(0, 0, 3, 0) };
            NextButton = new Button { Content = "▶", Width = 25, Height = 25, FontSize = 10, Margin = new Thickness(0, 0, 8, 0) };
            CounterLabel = new Label { Content = "1/1", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };

            PrevButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                PreviousRequested?.Invoke(this, EventArgs.Empty);
            };
            NextButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                NextRequested?.Invoke(this, EventArgs.Empty);
            };

            navPanel.Children.Add(PrevButton);
            navPanel.Children.Add(NextButton);
            navPanel.Children.Add(CounterLabel);
            DockPanel.SetDock(navPanel, Dock.Right);

            TitleBlock = new TextBlock
            {
                FontSize = 14,  // 12→14（+2）
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 0)
            };

            headerPanel.Children.Add(navPanel);
            headerPanel.Children.Add(TitleBlock);

            // 記事コンテンツエリア（サムネイル左、テキスト右）
            var contentArea = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            // サムネイル画像（左側）
            ThumbnailImage = new Image
            {
                MaxWidth = 120,
                MaxHeight = 80,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0),
                Stretch = Stretch.Uniform
            };
            DockPanel.SetDock(ThumbnailImage, Dock.Left);

            // 記事内容（右側）
            ContentBlock = new TextBlock
            {
                FontSize = 12,  // 10→12（+2）
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };

            contentArea.Children.Add(ThumbnailImage);
            contentArea.Children.Add(ContentBlock);

            // ボタンパネル
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var openButton = new Button
            {
                Content = "記事を開く",
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 9
            };

            var closeButton = new Button
            {
                Content = "閉じる",
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 9
            };

            openButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                OnOpenArticle();
            };
            closeButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                Hide();
            };

            buttonPanel.Children.Add(openButton);
            buttonPanel.Children.Add(closeButton);

            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(contentArea);
            stackPanel.Children.Add(buttonPanel);

            mainBorder.Child = stackPanel;
            Content = mainBorder;

            OpenButton = openButton;
        }

        public TextBlock TitleBlock { get; private set; }
        public TextBlock ContentBlock { get; private set; }
        public Button OpenButton { get; private set; }
        public Button PrevButton { get; private set; }
        public Button NextButton { get; private set; }
        public Label CounterLabel { get; private set; }
        public Image ThumbnailImage { get; private set; }

        public event EventHandler OpenArticleRequested;
        public event EventHandler PreviousRequested;
        public event EventHandler NextRequested;

        private void OnOpenArticle()
        {
            OpenArticleRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        public void ShowBubble(Point position, string title, string content, string thumbnailUrl, int currentIndex, int totalCount)
        {
            CurrentArticleIndex = currentIndex;
            TotalArticles = totalCount;

            TitleBlock.Text = title.Length > 60 ? title.Substring(0, 60) + "..." : title;
            ContentBlock.Text = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
            CounterLabel.Content = $"{currentIndex + 1}/{totalCount}";

            // サムネイル画像の設定
            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                ThumbnailImage.Source = new BitmapImage(new Uri(thumbnailUrl));
                ThumbnailImage.Visibility = Visibility.Visible;
            }
            else
            {
                ThumbnailImage.Source = null;
                ThumbnailImage.Visibility = Visibility.Collapsed;
            }

            // ループ機能対応：常にボタンを有効にする
            PrevButton.IsEnabled = totalCount > 1;
            NextButton.IsEnabled = totalCount > 1;

            Left = position.X;
            Top = position.Y;

            Show();
            Activate();

            // 15秒の自動送りタイマーを開始
            StartAutoAdvanceTimer();
        }

        public void StartAutoAdvanceTimer()
        {
            _autoAdvanceTimer?.Stop();
            if (TotalArticles > 1)
            {
                _autoAdvanceTimer?.Start();
            }
        }

        public void StopAutoAdvanceTimer()
        {
            _autoAdvanceTimer?.Stop();
        }
    }

    /// <summary>
    /// メインのデスクトップマスコットウィンドウ（機能強化版）
    /// </summary>
    public partial class MascotWindow : Window
    {
        private RssService _rssService;
        private WeatherService _weatherService;
        private int _currentArticleIndex = 0;
        private SpeechBubbleWindow _speechBubble;
        private DispatcherTimer _idleTimer;
        private DispatcherTimer _rssTimer;
        private DispatcherTimer _weatherTimer;
        private bool _isClickThrough = false;
        private MascotSettings _settings;

        public MascotWindow()
        {
            _settings = MascotSettings.Load();
            InitializeComponent();
            InitializeServices();
            LoadMascotImage();
            StartIdleAnimation();
            StartRssAutoUpdate();
            StartWeatherAutoUpdate();
        }

        private void InitializeComponent()
        {
            Width = 150;   // マスコット画像に合わせて拡大
            Height = 300;  // 天気表示分を考慮して30px拡張
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;

            // メインコンテナ（Grid）
            var mainGrid = new Grid();

            MascotImage = new Image
            {
                Width = 150,     // 80→150
                Height = 270,    // 80→270
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 30, 0, 0) // 天気表示のスペースを空けるため下に移動
            };

            // 天気情報全体のコンテナ
            var weatherContainer = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 15, 0, 0) // 上端から15pxの余裕を確保（10px下げる）
            };

            // 「今日の天気」ラベル
            var weatherLabel = new TextBlock
            {
                Text = "今日の天気",
                FontSize = 10, // 8→10（2段階アップ）
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent, // 背景透過
                Margin = new Thickness(0, 0, 0, 2),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Opacity = 0.8
                }
            };

            // 天気情報表示パネル
            var weatherBorder = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent, // 背景透過
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4)
            };

            var weatherPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // 天気アイコン
            WeatherIcon = new TextBlock
            {
                FontSize = 16,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Opacity = 0.8
                }
            };

            // 気温表示
            TemperatureText = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Opacity = 0.8
                }
            };

            weatherPanel.Children.Add(WeatherIcon);
            weatherPanel.Children.Add(TemperatureText);
            weatherBorder.Child = weatherPanel;

            weatherContainer.Children.Add(weatherLabel);
            weatherContainer.Children.Add(weatherBorder);

            mainGrid.Children.Add(MascotImage);
            mainGrid.Children.Add(weatherContainer);

            Content = mainGrid;

            // イベントハンドラ
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseRightButtonDown += OnMouseRightButtonDown;
            MouseDoubleClick += OnMouseDoubleClick;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            LocationChanged += OnLocationChanged;

            // ドラッグ可能にする
            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            Loaded += OnLoaded;
        }

        public Image MascotImage { get; private set; }
        public TextBlock WeatherIcon { get; private set; }
        public TextBlock TemperatureText { get; private set; }

        private void LoadMascotImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(_settings.ImagePath) && File.Exists(_settings.ImagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_settings.ImagePath);
                    bitmap.DecodePixelWidth = 150;   // 80→150
                    bitmap.DecodePixelHeight = 270;  // 80→270
                    bitmap.EndInit();
                    MascotImage.Source = bitmap;
                }
                else
                {
                    // デフォルトの絵文字
                    MascotImage.Source = CreateEmojiImage("🐱");
                }
            }
            catch
            {
                // エラー時はデフォルト絵文字
                MascotImage.Source = CreateEmojiImage("🐱");
            }
        }

        private BitmapSource CreateEmojiImage(string emoji)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var typeface = new Typeface("Segoe UI Emoji");
                var formattedText = new FormattedText(emoji,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, 60, Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                context.DrawText(formattedText, new Point(10, 10));
            }

            var bitmap = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private void InitializeServices()
        {
            _rssService = new RssService(_settings.RssUrl);
            _weatherService = new WeatherService();
            _speechBubble = new SpeechBubbleWindow();
            _speechBubble.OpenArticleRequested += OnOpenArticleRequested;
            _speechBubble.PreviousRequested += OnPreviousRequested;
            _speechBubble.NextRequested += OnNextRequested;

            // 初期RSS取得
            _ = UpdateRssAsync();
            
            // 初期天気取得
            _ = Task.Run(async () =>
            {
                bool success = await _weatherService.FetchWeatherAsync();
                if (!success)
                {
                    // API取得失敗時はエラー表示
                    System.Diagnostics.Debug.WriteLine("API取得失敗、エラー表示を使用");
                    _weatherService.CurrentWeather.WeatherCode = "❌";
                    _weatherService.CurrentWeather.WeatherText = "取得失敗";
                    _weatherService.CurrentWeather.MaxTemp = null;
                    _weatherService.CurrentWeather.MinTemp = null;
                }
                Dispatcher.Invoke(() => UpdateWeatherDisplay());
            });
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save();
            
            // スピーチバブルが表示されている場合は位置を更新
            if (_speechBubble != null && _speechBubble.IsVisible)
            {
                UpdateSpeechBubblePosition();
            }
        }

        private void UpdateSpeechBubblePosition()
        {
            if (_speechBubble != null)
            {
                var newPosition = GetBubblePosition();
                _speechBubble.Left = newPosition.X;
                _speechBubble.Top = newPosition.Y;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowTransparent(hwnd);
            }
        }

        private void SetWindowTransparent(IntPtr hwnd)
        {
            var extendedStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, extendedStyle | Win32Api.WS_EX_LAYERED);
        }

        private void StartIdleAnimation()
        {
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleTimer.Tick += (s, e) =>
            {
                if (!_speechBubble.IsVisible)
                {
                    AnimateMascot();
                }
            };
            _idleTimer.Start();
        }

        private void AnimateMascot()
        {
            var scaleTransform = new ScaleTransform(1.0, 1.0);
            MascotImage.RenderTransform = scaleTransform;
            MascotImage.RenderTransformOrigin = new Point(0.5, 0.5);

            var animation = new DoubleAnimation(1.2, 1.0, TimeSpan.FromSeconds(0.5))
            {
                AutoReverse = false
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void StartRssAutoUpdate()
        {
            _rssTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _rssTimer.Tick += async (s, e) => await UpdateRssAsync();
            _rssTimer.Start();
        }

        private void StartWeatherAutoUpdate()
        {
            _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _weatherTimer.Tick += async (s, e) => await UpdateWeatherAsync();
            _weatherTimer.Start();
        }

        private async Task UpdateRssAsync()
        {
            var success = await _rssService.FetchRssAsync();
            if (success && _rssService.Articles.Any())
            {
                _currentArticleIndex = 0;
                AnimateMascot();
            }
        }

        private async Task UpdateWeatherAsync()
        {
            var success = await _weatherService.FetchWeatherAsync();
            if (success)
            {
                UpdateWeatherDisplay();
            }
        }

        private void UpdateWeatherDisplay()
        {
            var weather = _weatherService.CurrentWeather;
            System.Diagnostics.Debug.WriteLine($"UpdateWeatherDisplay呼び出し - 天気: {weather.WeatherText}, アイコン: {weather.WeatherCode}");
            
            // 天気アイコンを更新
            WeatherIcon.Text = weather.WeatherCode;
            System.Diagnostics.Debug.WriteLine($"WeatherIcon.Text設定: {WeatherIcon.Text}");
            
            // 気温テキストを更新
            var tempText = "";
            if (weather.WeatherText == "取得失敗")
            {
                tempText = "取得失敗";
            }
            else if (weather.MaxTemp.HasValue && weather.MinTemp.HasValue)
            {
                tempText = $"{weather.MaxTemp}°/{weather.MinTemp}°";
            }
            else if (weather.MaxTemp.HasValue)
            {
                tempText = $"{weather.MaxTemp}°";
            }
            else if (weather.MinTemp.HasValue)
            {
                tempText = $"{weather.MinTemp}°";
            }
            else
            {
                tempText = "--°";
            }
            
            TemperatureText.Text = tempText;
            System.Diagnostics.Debug.WriteLine($"TemperatureText.Text設定: {TemperatureText.Text}");
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _speechBubble?.StopAutoAdvanceTimer();
            ShowSpeechBubble();
            AnimateMascot();
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var contextMenu = new ContextMenu();

            var settingsItem = new MenuItem { Header = "設定..." };
            settingsItem.Click += ShowSettings;

            var updateItem = new MenuItem { Header = "RSS更新" };
            updateItem.Click += async (s, args) => await UpdateRssAsync();

            var clickThroughItem = new MenuItem 
            { 
                Header = _isClickThrough ? "クリックスルー解除" : "クリックスルー有効",
                IsCheckable = true,
                IsChecked = _isClickThrough
            };
            clickThroughItem.Click += ToggleClickThrough;

            var exitItem = new MenuItem { Header = "終了" };
            exitItem.Click += (s, args) => Application.Current.Shutdown();

            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(updateItem);
            contextMenu.Items.Add(clickThroughItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            contextMenu.IsOpen = true;
        }

        private void ShowSettings(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true && settingsWindow.SettingsChanged)
            {
                _settings = settingsWindow.Settings;
                _settings.Save();

                // 画像を再読み込み
                LoadMascotImage();

                // RSS URLが変更された場合は更新
                if (_rssService.CurrentRssUrl != _settings.RssUrl)
                {
                    _rssService.SetRssUrl(_settings.RssUrl);
                    _ = UpdateRssAsync();
                }
            }
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleClickThrough(null, null);
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClickThrough)
            {
                AnimateMascot();
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            // マウスが離れた時の処理
        }

        private void ShowSpeechBubble()
        {
            if (!_rssService.Articles.Any())
            {
                var position = GetBubblePosition();
                _speechBubble.ShowBubble(position, "記事がありません", "RSS更新を実行してください。", "", 0, 0);
                return;
            }

            var article = _rssService.Articles[_currentArticleIndex];
            var bubblePosition = GetBubblePosition();
            _speechBubble.ShowBubble(bubblePosition, article.Title, article.Description, article.ThumbnailUrl, _currentArticleIndex, _rssService.Articles.Count);
        }

        private Point GetBubblePosition()
        {
            // スピーチバブルをマスコットの左上に配置
            // バブルの幅が420pxなので、左端から少し余裕を持って配置
            var bubbleWidth = 420;
            var offsetX = -bubbleWidth + 10;  // 右に20px移動（-10から+10へ）
            var offsetY = 10;  // さらに10px下に移動（0から10へ）
            
            return new Point(Left + offsetX, Top + offsetY);
        }

        private void OnPreviousRequested(object sender, EventArgs e)
        {
            if (_rssService.Articles.Any())
            {
                // ループ機能付き前の記事へ
                _currentArticleIndex--;
                if (_currentArticleIndex < 0)
                {
                    _currentArticleIndex = _rssService.Articles.Count - 1;
                }
                ShowSpeechBubble();
                AnimateMascot();
            }
        }

        private void OnNextRequested(object sender, EventArgs e)
        {
            if (_rssService.Articles.Any())
            {
                // ループ機能付き次の記事へ
                _currentArticleIndex++;
                if (_currentArticleIndex >= _rssService.Articles.Count)
                {
                    _currentArticleIndex = 0;
                }
                ShowSpeechBubble();
                AnimateMascot();
            }
        }

        private void OnOpenArticleRequested(object sender, EventArgs e)
        {
            if (_rssService.Articles.Any())
            {
                var article = _rssService.Articles[_currentArticleIndex];
                if (!string.IsNullOrEmpty(article.Link))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = article.Link,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void ToggleClickThrough(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            _isClickThrough = !_isClickThrough;

            var extendedStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            if (_isClickThrough)
            {
                Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, extendedStyle | Win32Api.WS_EX_TRANSPARENT);
            }
            else
            {
                Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, extendedStyle & ~Win32Api.WS_EX_TRANSPARENT);
            }
        }
    }

    /// <summary>
    /// アプリケーションのメインクラス
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mascot = new MascotWindow();
            mascot.Show();

            MainWindow = mascot;
        }
    }

    /// <summary>
    /// エントリーポイント
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run();
        }
    }
}