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
using NAudio.Wave;
using NAudio.Dsp;
using WpfAnimatedGif;

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
    /// 記事のソース種別
    /// </summary>
    public enum ArticleSourceType
    {
        RSS,           // 従来のRSSフィード
        TechBlog       // 技術ブログ (Qiita/Zenn)
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
        public string SourceName { get; set; } = "";  // "Gizmodo", "ITmedia", "Qiita", "Zenn"
        public string SourceUrl { get; set; } = "";   // Feed URL
        public DateTime PublishedDate { get; set; }   // 日付ソート用
        public ArticleSourceType SourceType { get; set; } = ArticleSourceType.RSS;  // ソース種別
        public string AuthorName { get; set; } = "";  // 著者名 (技術ブログ用)
        public List<string> Tags { get; set; } = new();  // タグ (技術ブログ用)
    }

    /// <summary>
    /// RSS Feed設定データ
    /// </summary>
    public class RssFeedConfig
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
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
    /// 技術ブログ設定
    /// </summary>
    public class TechBlogSettings
    {
        // Qiita設定
        public bool QiitaEnabled { get; set; } = true;
        public string QiitaAccessToken { get; set; } = "";
        public List<string> QiitaTags { get; set; } = new() { "C#", "WPF", ".NET", "AI", "機械学習" };
        public bool QiitaUseTimeline { get; set; } = false;  // true=タイムライン, false=タグ検索

        // Zenn設定
        public bool ZennEnabled { get; set; } = true;
        public string ZennUsername { get; set; } = "";
        public List<string> ZennTopics { get; set; } = new() { "csharp", "dotnet", "ai", "nextjs" };
    }



    /// <summary>
    /// マスコット設定クラス
    /// </summary>
    public class MascotSettings
    {
        public string ImagePath { get; set; } = "";
        public string AnimationGifPath { get; set; } = "rolling_light.gif"; // アニメーションGIFのパス
        public string RssUrl { get; set; } = "https://www.gizmodo.jp/index.xml"; // 後方互換性のため残す
        public List<RssFeedConfig> RssFeeds { get; set; } = new();
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;

        // 音声合成設定
        public bool EnableVoiceSynthesis { get; set; } = false;
        public string VoiceVoxApiKey { get; set; } = "";
        public int VoiceSpeakerId { get; set; } = 61; // デフォルトは話者ID61
        public bool AutoReadArticles { get; set; } = false; // 記事切り替え時の自動読み上げ

        // 技術ブログ設定
        public TechBlogSettings TechBlog { get; set; } = new();

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
                Console.WriteLine($"設定保存エラー: {ex.Message}");
            }
        }

        public static MascotSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<MascotSettings>(json) ?? new MascotSettings();
                    settings.InitializeDefaultFeeds();
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定読込エラー: {ex.Message}");
            }
            var newSettings = new MascotSettings();
            newSettings.InitializeDefaultFeeds();
            return newSettings;
        }

        private void InitializeDefaultFeeds()
        {
            // 初回起動時またはRssFeedsが空の場合、デフォルトFeedを設定
            if (!RssFeeds.Any())
            {
                RssFeeds.AddRange(new List<RssFeedConfig>
                {
                    new RssFeedConfig { Name = "Gizmodo", Url = "https://www.gizmodo.jp/index.xml", IsEnabled = true },
                    new RssFeedConfig { Name = "ITmedia", Url = "https://rss.itmedia.co.jp/rss/2.0/news_bursts.xml", IsEnabled = true },
                    new RssFeedConfig { Name = "GIGAZINE", Url = "https://gigazine.net/news/rss_2.0/", IsEnabled = true }
                });
            }
        }
    }

    /// <summary>
    /// RSSサービスクラス（URL変更可能）
    /// </summary>
    public class RssService
    {
        private readonly HttpClient _httpClient;
        private List<RssFeedConfig> _rssFeeds;

        public List<RssArticle> Articles { get; private set; } = new List<RssArticle>();
        public DateTime LastUpdate { get; private set; }

        public RssService(List<RssFeedConfig> rssFeeds)
        {
            _rssFeeds = rssFeeds;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void UpdateFeedList(List<RssFeedConfig> newFeeds)
        {
            _rssFeeds = newFeeds;
        }

        public async Task<bool> FetchRssAsync()
        {
            var allArticles = new List<RssArticle>();
            bool anySuccess = false;

            foreach (var feed in _rssFeeds.Where(f => f.IsEnabled))
            {
                try
                {
                    Console.WriteLine($"RSS取得開始: {feed.Name} ({feed.Url})");
                    var response = await _httpClient.GetStringAsync(feed.Url);
                    var doc = XDocument.Parse(response);

                    var articles = doc.Descendants("item")
                        .Take(10) // 各Feedから10記事まで
                        .Select(item => new RssArticle
                        {
                            Title = CleanText(item.Element("title")?.Value ?? ""),
                            Description = CleanHtml(item.Element("description")?.Value ?? ""),
                            Link = item.Element("link")?.Value ?? "",
                            PubDate = item.Element("pubDate")?.Value ?? "",
                            ThumbnailUrl = GetThumbnailUrl(item),
                            SourceName = feed.Name,
                            SourceUrl = feed.Url,
                            PublishedDate = ParsePubDate(item.Element("pubDate")?.Value ?? "")
                        })
                        .ToList();

                    allArticles.AddRange(articles);
                    anySuccess = true;
                    Console.WriteLine($"RSS取得成功: {feed.Name} - {articles.Count}記事");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RSS取得エラー ({feed.Name}): {ex.Message}");
                }
            }

            // 重複除去とソート
            Articles = RemoveDuplicates(allArticles)
                .OrderByDescending(a => a.PublishedDate)
                .Take(30) // 最終的に30記事まで
                .ToList();

            LastUpdate = DateTime.Now;
            Console.WriteLine($"全Feed処理完了: {Articles.Count}記事（重複除去済み）");
            return anySuccess;
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

            // 画像関連のテキストを除去
            cleanText = Regex.Replace(cleanText, @"\b(?:photo|image|画像|写真)\b[^。]*[。.]", "", RegexOptions.IgnoreCase);
            cleanText = Regex.Replace(cleanText, @"[^。]*\b(?:photo|image|画像|写真)\b", "", RegexOptions.IgnoreCase);

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

        private DateTime ParsePubDate(string pubDateStr)
        {
            if (string.IsNullOrEmpty(pubDateStr))
                return DateTime.MinValue;

            try
            {
                // RFC822形式の日付をパース
                if (DateTime.TryParse(pubDateStr, out DateTime parsedDate))
                {
                    return parsedDate;
                }
            }
            catch
            {
                Console.WriteLine($"日付パースエラー: {pubDateStr}");
            }

            return DateTime.MinValue;
        }

        private List<RssArticle> RemoveDuplicates(List<RssArticle> articles)
        {
            var uniqueArticles = new List<RssArticle>();
            var seenTitles = new HashSet<string>();

            foreach (var article in articles)
            {
                // タイトルの類似度で重複判定
                var normalizedTitle = NormalizeTitle(article.Title);
                
                bool isDuplicate = false;
                foreach (var seenTitle in seenTitles)
                {
                    if (CalculateSimilarity(normalizedTitle, seenTitle) > 0.8) // 80%以上の類似度で重複とみなす
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    seenTitles.Add(normalizedTitle);
                    uniqueArticles.Add(article);
                }
            }

            Console.WriteLine($"重複除去: {articles.Count} → {uniqueArticles.Count}記事");
            return uniqueArticles;
        }

        private string NormalizeTitle(string title)
        {
            // タイトルを正規化（小文字化、特殊文字除去、スペース正規化）
            return Regex.Replace(title.ToLower(), @"[^\w\s]", "").Trim();
        }

        private double CalculateSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0;

            // 簡単なレーベンシュタイン距離ベースの類似度計算
            var distance = LevenshteinDistance(str1, str2);
            var maxLength = Math.Max(str1.Length, str2.Length);
            return 1.0 - (double)distance / maxLength;
        }

        private int LevenshteinDistance(string str1, string str2)
        {
            var matrix = new int[str1.Length + 1, str2.Length + 1];

            for (int i = 0; i <= str1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= str2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[str1.Length, str2.Length];
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
            var handler = new HttpClientHandler();

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DesktopMascot/1.0");
        }

        public async Task<bool> FetchWeatherAsync()
        {
            // Open-MeteoAPIのみを使用（気象庁APIは無効化）
            return await FetchOpenMeteoWeatherAsync();
        }

        private async Task<bool> FetchJmaWeatherAsync()
        {
            try
            {
                Console.WriteLine("気象庁API: 天気情報取得開始...");
                var response = await _httpClient.GetStringAsync(JMA_API_URL);
                Console.WriteLine($"気象庁API: 応答受信 {response.Length} 文字");
                
                var weatherJson = System.Text.Json.JsonDocument.Parse(response);
                
                // 最初の予報データを取得
                var forecasts = weatherJson.RootElement.GetProperty("timeSeries");
                Console.WriteLine($"時系列データ数: {forecasts.GetArrayLength()}");
                
                if (forecasts.GetArrayLength() > 0)
                {
                    var firstForecast = forecasts[0];
                    var areas = firstForecast.GetProperty("areas");
                    Console.WriteLine($"地域データ数: {areas.GetArrayLength()}");
                    
                    // 東京地方の天気を取得
                    foreach (var area in areas.EnumerateArray())
                    {
                        var areaName = area.GetProperty("area").GetProperty("name").GetString();
                        Console.WriteLine($"地域名: {areaName}");
                        
                        if (areaName == "東京地方")
                        {
                            var weathers = area.GetProperty("weathers");
                            if (weathers.GetArrayLength() > 0)
                            {
                                CurrentWeather.WeatherText = weathers[0].GetString() ?? "";
                                CurrentWeather.WeatherCode = GetWeatherCode(CurrentWeather.WeatherText);
                                Console.WriteLine($"天気: {CurrentWeather.WeatherText}, アイコン: {CurrentWeather.WeatherCode}");
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
                        Console.WriteLine($"気温地域名: {areaName}");
                        
                        if (areaName == "東京")
                        {
                            if (area.TryGetProperty("tempsMax", out var maxTemps) && maxTemps.GetArrayLength() > 0)
                            {
                                var maxTempStr = maxTemps[0].GetString();
                                Console.WriteLine($"最高気温文字列: {maxTempStr}");
                                if (!string.IsNullOrEmpty(maxTempStr) && int.TryParse(maxTempStr, out int maxTemp))
                                {
                                    CurrentWeather.MaxTemp = maxTemp;
                                }
                            }
                            
                            if (area.TryGetProperty("tempsMin", out var minTemps) && minTemps.GetArrayLength() > 0)
                            {
                                var minTempStr = minTemps[0].GetString();
                                Console.WriteLine($"最低気温文字列: {minTempStr}");
                                if (!string.IsNullOrEmpty(minTempStr) && int.TryParse(minTempStr, out int minTemp))
                                {
                                    CurrentWeather.MinTemp = minTemp;
                                }
                            }
                            break;
                        }
                    }
                }

                Console.WriteLine($"気象庁API成功 - 天気: {CurrentWeather.WeatherText}, 最高: {CurrentWeather.MaxTemp}, 最低: {CurrentWeather.MinTemp}");
                CurrentWeather.LastUpdate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"気象庁API取得エラー: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> FetchOpenMeteoWeatherAsync()
        {
            try
            {
                Console.WriteLine("Open-Meteo API: 天気情報取得開始...");
                var response = await _httpClient.GetStringAsync(OPEN_METEO_API_URL);
                Console.WriteLine($"Open-Meteo API: 応答受信 {response.Length} 文字");
                
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
                
                Console.WriteLine($"Open-Meteo API成功 - 天気: {CurrentWeather.WeatherText}, 最高: {CurrentWeather.MaxTemp}, 最低: {CurrentWeather.MinTemp}");
                CurrentWeather.LastUpdate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Open-Meteo API取得エラー: {ex.Message}");
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
    /// Qiita記事取得サービス
    /// </summary>
    public class QiitaService
    {
        private readonly HttpClient _httpClient;
        private readonly TechBlogSettings _settings;

        public QiitaService(TechBlogSettings settings)
        {
            _httpClient = new HttpClient();
            _settings = settings;
        }

        public async Task<List<RssArticle>> GetArticlesAsync()
        {
            try
            {
                if (!_settings.QiitaEnabled)
                {
                    Console.WriteLine("[Qiita] Qiitaは無効化されています");
                    return new List<RssArticle>();
                }

                if (_settings.QiitaUseTimeline && !string.IsNullOrEmpty(_settings.QiitaAccessToken))
                {
                    Console.WriteLine("[Qiita] タイムラインから記事を取得します");
                    return await GetTimelineAsync();
                }
                else
                {
                    Console.WriteLine($"[Qiita] タグ検索から記事を取得します: {string.Join(", ", _settings.QiitaTags)}");
                    return await GetArticlesByTagsAsync(_settings.QiitaTags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Qiita] エラー: {ex.Message}");
                return new List<RssArticle>();
            }
        }

        private async Task<List<RssArticle>> GetTimelineAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://qiita.com/api/v2/authenticated_user/items?per_page=20");
                request.Headers.Add("Authorization", $"Bearer {_settings.QiitaAccessToken}");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var items = System.Text.Json.JsonSerializer.Deserialize<List<QiitaItem>>(json);

                return items?.Select(ConvertToRssArticle).ToList() ?? new List<RssArticle>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Qiita] タイムライン取得エラー: {ex.Message}");
                return new List<RssArticle>();
            }
        }

        private async Task<List<RssArticle>> GetArticlesByTagsAsync(List<string> tags)
        {
            var allArticles = new List<RssArticle>();

            foreach (var tag in tags.Take(3))  // API制限考慮で最大3タグ
            {
                try
                {
                    var encodedTag = Uri.EscapeDataString(tag);
                    var url = $"https://qiita.com/api/v2/tags/{encodedTag}/items?per_page=10";

                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<QiitaItem>>(json);

                    if (items != null)
                    {
                        allArticles.AddRange(items.Select(ConvertToRssArticle));
                        Console.WriteLine($"[Qiita] タグ '{tag}' から {items.Count}件の記事を取得");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Qiita] タグ '{tag}' の取得エラー: {ex.Message}");
                }
            }

            // 重複削除、日付ソート
            return allArticles
                .DistinctBy(a => a.Link)
                .OrderByDescending(a => a.PublishedDate)
                .ToList();
        }

        private RssArticle ConvertToRssArticle(QiitaItem item)
        {
            return new RssArticle
            {
                Title = item.title ?? "",
                Description = item.body?.Length > 200 ? item.body.Substring(0, 200) + "..." : item.body ?? "",
                Link = item.url ?? "",
                ThumbnailUrl = "",  // Qiitaは記事サムネイルなし
                SourceName = "Qiita",
                SourceUrl = "https://qiita.com",
                PublishedDate = item.created_at,
                PubDate = item.created_at.ToString("R"),
                SourceType = ArticleSourceType.TechBlog,
                AuthorName = item.user?.id ?? "",
                Tags = item.tags?.Select(t => t.name ?? "").ToList() ?? new List<string>()
            };
        }
    }

    /// <summary>
    /// Qiita APIレスポンス用モデル
    /// </summary>
    public class QiitaItem
    {
        public string title { get; set; } = "";
        public string body { get; set; } = "";
        public string url { get; set; } = "";
        public DateTime created_at { get; set; }
        public QiitaUser user { get; set; } = new();
        public List<QiitaTag> tags { get; set; } = new();
    }

    public class QiitaUser
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
    }

    public class QiitaTag
    {
        public string name { get; set; } = "";
    }

    /// <summary>
    /// Zenn記事取得サービス
    /// </summary>
    public class ZennService
    {
        private readonly HttpClient _httpClient;
        private readonly TechBlogSettings _settings;

        public ZennService(TechBlogSettings settings)
        {
            _httpClient = new HttpClient();
            _settings = settings;
        }

        public async Task<List<RssArticle>> GetArticlesAsync()
        {
            try
            {
                if (!_settings.ZennEnabled)
                {
                    Console.WriteLine("[Zenn] Zennは無効化されています");
                    return new List<RssArticle>();
                }

                var allArticles = new List<RssArticle>();

                // ユーザーの記事を取得（RSS経由）
                if (!string.IsNullOrEmpty(_settings.ZennUsername))
                {
                    Console.WriteLine($"[Zenn] ユーザー '{_settings.ZennUsername}' の記事を取得します");
                    var userArticles = await GetUserArticlesAsync(_settings.ZennUsername);
                    allArticles.AddRange(userArticles);
                }

                // トピック別記事を取得
                foreach (var topic in _settings.ZennTopics.Take(3))  // 最大3トピック
                {
                    Console.WriteLine($"[Zenn] トピック '{topic}' の記事を取得します");
                    var topicArticles = await GetTopicArticlesAsync(topic);
                    allArticles.AddRange(topicArticles);
                }

                // 重複削除、日付ソート
                return allArticles
                    .DistinctBy(a => a.Link)
                    .OrderByDescending(a => a.PublishedDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zenn] エラー: {ex.Message}");
                return new List<RssArticle>();
            }
        }

        private async Task<List<RssArticle>> GetUserArticlesAsync(string username)
        {
            try
            {
                // Zenn RSSフィード: https://zenn.dev/{username}/feed
                var url = $"https://zenn.dev/{username}/feed";
                var response = await _httpClient.GetStringAsync(url);

                return ParseRssFeed(response, $"Zenn (@{username})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zenn] ユーザー '{username}' の取得エラー: {ex.Message}");
                return new List<RssArticle>();
            }
        }

        private async Task<List<RssArticle>> GetTopicArticlesAsync(string topic)
        {
            try
            {
                // Zenn トピックRSS: https://zenn.dev/topics/{topic}/feed
                var url = $"https://zenn.dev/topics/{topic}/feed";
                var response = await _httpClient.GetStringAsync(url);

                return ParseRssFeed(response, $"Zenn (#{topic})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zenn] トピック '{topic}' の取得エラー: {ex.Message}");
                return new List<RssArticle>();
            }
        }

        private List<RssArticle> ParseRssFeed(string rssXml, string sourceName)
        {
            var articles = new List<RssArticle>();

            try
            {
                var doc = XDocument.Parse(rssXml);
                var items = doc.Descendants("item").Take(10);  // 最大10件

                foreach (var item in items)
                {
                    var title = item.Element("title")?.Value ?? "";
                    var link = item.Element("link")?.Value ?? "";
                    var description = item.Element("description")?.Value ?? "";
                    var pubDateStr = item.Element("pubDate")?.Value ?? "";
                    var creator = item.Element(XName.Get("creator", "http://purl.org/dc/elements/1.1/"))?.Value ?? "";

                    // pubDateをパース
                    DateTime.TryParse(pubDateStr, out var pubDate);

                    // タグを抽出（descriptionから簡易的に）
                    var tags = new List<string>();

                    articles.Add(new RssArticle
                    {
                        Title = title,
                        Description = description.Length > 200 ? description.Substring(0, 200) + "..." : description,
                        Link = link,
                        ThumbnailUrl = "",
                        SourceName = sourceName,
                        SourceUrl = "https://zenn.dev",
                        PublishedDate = pubDate,
                        PubDate = pubDateStr,
                        SourceType = ArticleSourceType.TechBlog,
                        AuthorName = creator,
                        Tags = tags
                    });
                }

                Console.WriteLine($"[Zenn] {sourceName} から {articles.Count}件の記事を取得");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zenn] RSS解析エラー: {ex.Message}");
            }

            return articles;
        }
    }

    /// <summary>
    /// VOICEVOX音声合成サービス
    /// </summary>
    public class VoiceVoxService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BASE_URL = "https://api.tts.quest/v3/voicevox";
        private bool _disposed = false;
        private System.Windows.Media.MediaPlayer _currentMediaPlayer;

        // リップシンク制御用コールバック
        public Action OnAudioPlayStarted { get; set; }
        public Action OnAudioPlayEnded { get; set; }

        public VoiceVoxService(string apiKey = "")
        {
            _httpClient = new HttpClient();
            _apiKey = apiKey;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _currentMediaPlayer?.Close();
                    _currentMediaPlayer = null;
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// テキストを音声合成して直接再生
        /// </summary>
        public async Task<VoiceSynthesisResult> SynthesizeAndPlayAsync(string text, int speakerId = 61)
        {
            try
            {
                var encodedText = Uri.EscapeDataString(text);
                var url = $"{BASE_URL}/synthesis?speaker={speakerId}&text={encodedText}";
                
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    url += $"&key={_apiKey}";
                }
                
                Console.WriteLine($"VOICEVOX API リクエスト: Speaker ID = {speakerId}, URL = {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"VOICEVOX API レスポンス: Status = {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<VoiceSynthesisResponse>(responseContent);

                if (result?.success == true && !string.IsNullOrEmpty(result.mp3StreamingUrl))
                {
                    // 音声データを直接ダウンロードして再生
                    await PlayAudioFromUrlAsync(result.mp3StreamingUrl);
                    
                    return new VoiceSynthesisResult
                    {
                        IsSuccess = true,
                        AudioUrl = result.mp3StreamingUrl,
                        SpeakerName = result.speakerName ?? "不明"
                    };
                }

                return new VoiceSynthesisResult
                {
                    IsSuccess = false,
                    ErrorMessage = "音声合成に失敗しました"
                };
            }
            catch (Exception ex)
            {
                return new VoiceSynthesisResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// テキストを音声合成して再生URLを取得
        /// </summary>
        public async Task<VoiceSynthesisResult> SynthesizeAsync(string text, int speakerId = 61)
        {
            try
            {
                var encodedText = Uri.EscapeDataString(text);
                var url = $"{BASE_URL}/synthesis?speaker={speakerId}&text={encodedText}";
                
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    url += $"&key={_apiKey}";
                }
                
                Console.WriteLine($"VOICEVOX API リクエスト: Speaker ID = {speakerId}, URL = {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"VOICEVOX API レスポンス: Status = {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<VoiceSynthesisResponse>(responseContent);

                if (result?.success == true && !string.IsNullOrEmpty(result.mp3StreamingUrl))
                {
                    return new VoiceSynthesisResult
                    {
                        IsSuccess = true,
                        AudioUrl = result.mp3StreamingUrl,
                        SpeakerName = result.speakerName ?? "不明"
                    };
                }

                return new VoiceSynthesisResult
                {
                    IsSuccess = false,
                    ErrorMessage = "音声合成に失敗しました"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音声合成エラー: {ex.Message}");
                return new VoiceSynthesisResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 利用可能なスピーカーの一覧を取得
        /// </summary>
        public async Task<List<VoiceSpeaker>> GetSpeakersAsync()
        {
            try
            {
                var url = $"{BASE_URL}/speakers_array";
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    url += $"?key={_apiKey}";
                }
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var apiResponse = System.Text.Json.JsonSerializer.Deserialize<SpeakersArrayResponse>(responseContent);

                var result = new List<VoiceSpeaker>();
                if (apiResponse?.speakers != null)
                {
                    for (int i = 0; i < apiResponse.speakers.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(apiResponse.speakers[i]))
                        {
                            result.Add(new VoiceSpeaker
                            {
                                Id = i,
                                Name = apiResponse.speakers[i]
                            });
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"スピーカー取得エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"スピーカー取得エラー詳細: {ex}");

                // フォールバック: 静的JSONファイルから取得を試行
                try
                {
                    Console.WriteLine("静的JSONファイルからスピーカー情報を取得中...");
                    var staticUrl = "https://static.tts.quest/voicevox_speakers_utf8.json";
                    var staticResponse = await _httpClient.GetAsync(staticUrl);
                    staticResponse.EnsureSuccessStatusCode();

                    var staticContent = await staticResponse.Content.ReadAsStringAsync();
                    var staticSpeakers = System.Text.Json.JsonSerializer.Deserialize<string[]>(staticContent);

                    var result = new List<VoiceSpeaker>();
                    if (staticSpeakers != null)
                    {
                        for (int i = 0; i < staticSpeakers.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(staticSpeakers[i]))
                            {
                                result.Add(new VoiceSpeaker
                                {
                                    Id = i,
                                    Name = staticSpeakers[i]
                                });
                            }
                        }
                    }

                    Console.WriteLine($"静的JSONから{result.Count}個のスピーカーを取得しました");
                    return result;
                }
                catch (Exception staticEx)
                {
                    Console.WriteLine($"静的JSON取得エラー: {staticEx.Message}");
                    return new List<VoiceSpeaker>();
                }
            }
        }

        /// <summary>
        /// 音声URLから音声データをダウンロードして再生
        /// </summary>
        private async Task PlayAudioFromUrlAsync(string audioUrl)
        {
            try
            {
                Console.WriteLine($"音声ダウンロード開始: {audioUrl}");
                
                // 音声データをダウンロード
                var audioData = await _httpClient.GetByteArrayAsync(audioUrl);
                
                // 一時ファイルを作成
                var tempPath = Path.Combine(Path.GetTempPath(), $"voicevox_temp_{Guid.NewGuid()}.mp3");
                await File.WriteAllBytesAsync(tempPath, audioData);
                
                Console.WriteLine($"音声ファイル作成: {tempPath}");
                
                // 既存のMediaPlayerがあれば停止・クリア
                _currentMediaPlayer?.Close();

                // WindowsのMediaPlayerを使用して再生
                _currentMediaPlayer = new System.Windows.Media.MediaPlayer();
                var tcs = new TaskCompletionSource<bool>();

                _currentMediaPlayer.MediaEnded += (s, e) =>
                {
                    Console.WriteLine("音声再生完了イベント発生");
                    OnAudioPlayEnded?.Invoke(); // リップシンク停止コールバック
                    _currentMediaPlayer.Close();
                    _currentMediaPlayer = null;
                    tcs.SetResult(true);
                };

                _currentMediaPlayer.MediaFailed += (s, e) =>
                {
                    Console.WriteLine($"音声再生失敗: {e.ErrorException?.Message}");
                    OnAudioPlayEnded?.Invoke(); // リップシンク停止コールバック
                    _currentMediaPlayer.Close();
                    _currentMediaPlayer = null;
                    tcs.SetException(e.ErrorException ?? new Exception("音声再生に失敗しました"));
                };

                _currentMediaPlayer.Open(new Uri(tempPath));
                _currentMediaPlayer.Play();
                OnAudioPlayStarted?.Invoke(); // リップシンク開始コールバック

                Console.WriteLine("音声再生開始");

                // 実際の再生完了を待つ
                await tcs.Task;
                Console.WriteLine("音声再生完了");

                // 再生完了後に一時ファイルを削除
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                        Console.WriteLine($"一時ファイル削除: {tempPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"一時ファイル削除エラー: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音声再生エラー: {ex.Message}");
                throw;
            }
        }

    }

    /// <summary>
    /// 音声合成レスポンスデータ
    /// </summary>
    public class VoiceSynthesisResponse
    {
        public bool success { get; set; }
        public string speakerName { get; set; } = "";
        public string mp3StreamingUrl { get; set; } = "";
    }

    /// <summary>
    /// 音声合成結果
    /// </summary>
    public class VoiceSynthesisResult
    {
        public bool IsSuccess { get; set; }
        public string AudioUrl { get; set; } = "";
        public string SpeakerName { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// 音声スピーカー情報
    /// </summary>
    public class VoiceSpeaker
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// スピーカー配列APIのレスポンス
    /// </summary>
    public class SpeakersArrayResponse
    {
        public bool isApiKeyValid { get; set; }
        public string[] speakers { get; set; }
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
                RssFeeds = new List<RssFeedConfig>(currentSettings.RssFeeds.Select(f => new RssFeedConfig 
                { 
                    Name = f.Name, 
                    Url = f.Url, 
                    IsEnabled = f.IsEnabled 
                })),
                WindowLeft = currentSettings.WindowLeft,
                WindowTop = currentSettings.WindowTop,
                EnableVoiceSynthesis = currentSettings.EnableVoiceSynthesis,
                VoiceVoxApiKey = currentSettings.VoiceVoxApiKey,
                VoiceSpeakerId = currentSettings.VoiceSpeakerId,
                AutoReadArticles = currentSettings.AutoReadArticles
            };

            InitializeComponent();
            LoadCurrentSettings();
        }

        private void InitializeComponent()
        {
            Width = 500;
            Height = 500;
            Title = "デスクトップマスコット設定";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // タブコントロール
            var tabControl = new TabControl { Margin = new Thickness(10) };
            
            // 基本設定タブ
            var basicTab = new TabItem { Header = "基本設定" };
            var basicGrid = new Grid { Margin = new Thickness(20) };
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            basicTab.Content = basicGrid;

            // 音声合成設定タブ
            var voiceTab = new TabItem { Header = "音声合成設定" };
            var voiceGrid = new Grid { Margin = new Thickness(20) };
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            voiceTab.Content = voiceGrid;

            // RSS Feed設定タブ
            var feedTab = new TabItem { Header = "RSS Feed設定" };
            var feedGrid = new Grid { Margin = new Thickness(20) };
            feedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            feedGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            feedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            feedTab.Content = feedGrid;

            // 基本設定タブ - 画像設定
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

            basicGrid.Children.Add(imageLabel);
            basicGrid.Children.Add(imagePanel);

            // RSS Feed設定タブ - Feed管理UI
            var feedLabel = new Label { Content = "RSS Feed一覧 (最大10個):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(feedLabel, 0);

            // Feedリスト表示エリア
            FeedListBox = new ListBox { Margin = new Thickness(0, 0, 0, 10) };
            FeedListBox.SelectionChanged += FeedListBox_SelectionChanged;
            Grid.SetRow(FeedListBox, 1);

            // Feed操作ボタン
            var feedButtonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var addFeedBtn = new Button { Content = "追加", Width = 60, Margin = new Thickness(0, 0, 10, 0) };
            var editFeedBtn = new Button { Content = "編集", Width = 60, Margin = new Thickness(0, 0, 10, 0) };
            var deleteFeedBtn = new Button { Content = "削除", Width = 60, Margin = new Thickness(0, 0, 10, 0) };
            var toggleFeedBtn = new Button { Content = "有効/無効", Width = 80 };

            addFeedBtn.Click += AddFeed_Click;
            editFeedBtn.Click += EditFeed_Click;
            deleteFeedBtn.Click += DeleteFeed_Click;
            toggleFeedBtn.Click += ToggleFeed_Click;

            feedButtonPanel.Children.Add(addFeedBtn);
            feedButtonPanel.Children.Add(editFeedBtn);
            feedButtonPanel.Children.Add(deleteFeedBtn);
            feedButtonPanel.Children.Add(toggleFeedBtn);
            Grid.SetRow(feedButtonPanel, 2);

            feedGrid.Children.Add(feedLabel);
            feedGrid.Children.Add(FeedListBox);
            feedGrid.Children.Add(feedButtonPanel);

            // 音声合成設定タブ - UI要素
            var enableVoiceLabel = new Label { Content = "音声合成機能:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(enableVoiceLabel, 0);

            EnableVoiceCheckBox = new CheckBox { Content = "VOICEVOX による記事読み上げを有効にする", Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(EnableVoiceCheckBox, 1);

            var apiKeyLabel = new Label { Content = "TTS Quest API キー (オプション):", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(apiKeyLabel, 2);

            VoiceApiKeyTextBox = new TextBox { Width = 300, Margin = new Thickness(0, 0, 0, 15), HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(VoiceApiKeyTextBox, 3);

            var speakerLabel = new Label { Content = "音声キャラクター:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(speakerLabel, 4);

            var speakerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            SpeakerComboBox = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
            var testVoiceBtn = new Button { Content = "テスト再生", Width = 80 };
            testVoiceBtn.Click += TestVoice_Click;
            
            speakerPanel.Children.Add(SpeakerComboBox);
            speakerPanel.Children.Add(testVoiceBtn);
            Grid.SetRow(speakerPanel, 5);

            AutoReadCheckBox = new CheckBox { Content = "記事切り替え時に自動で読み上げる", Margin = new Thickness(0, 15, 0, 0) };
            Grid.SetRow(AutoReadCheckBox, 6);

            voiceGrid.Children.Add(enableVoiceLabel);
            voiceGrid.Children.Add(EnableVoiceCheckBox);
            voiceGrid.Children.Add(apiKeyLabel);
            voiceGrid.Children.Add(VoiceApiKeyTextBox);
            voiceGrid.Children.Add(speakerLabel);
            voiceGrid.Children.Add(speakerPanel);
            voiceGrid.Children.Add(AutoReadCheckBox);

            // タブをTabControlに追加
            tabControl.Items.Add(basicTab);
            tabControl.Items.Add(voiceTab);
            tabControl.Items.Add(feedTab);

            Grid.SetRow(tabControl, 0);

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

            grid.Children.Add(tabControl);
            grid.Children.Add(buttonPanel);
            Content = grid;
        }

        public TextBox ImagePathTextBox { get; private set; }
        public ListBox FeedListBox { get; private set; }
        
        // 音声合成設定UI要素
        public CheckBox EnableVoiceCheckBox { get; private set; }
        public TextBox VoiceApiKeyTextBox { get; private set; }
        public ComboBox SpeakerComboBox { get; private set; }
        public CheckBox AutoReadCheckBox { get; private set; }

        private async void LoadCurrentSettings()
        {
            ImagePathTextBox.Text = Settings.ImagePath;
            RefreshFeedList();
            
            // 音声合成設定の読み込み
            EnableVoiceCheckBox.IsChecked = Settings.EnableVoiceSynthesis;
            VoiceApiKeyTextBox.Text = Settings.VoiceVoxApiKey;
            AutoReadCheckBox.IsChecked = Settings.AutoReadArticles;
            
            // スピーカー一覧を取得して設定
            await LoadSpeakersAsync();
        }
        
        private async Task LoadSpeakersAsync()
        {
            try
            {
                var voiceService = new VoiceVoxService(Settings.VoiceVoxApiKey);
                var speakers = await voiceService.GetSpeakersAsync();
                
                SpeakerComboBox.Items.Clear();
                foreach (var speaker in speakers)
                {
                    SpeakerComboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = speaker.Name, 
                        Tag = speaker.Id 
                    });
                    
                    if (speaker.Id == Settings.VoiceSpeakerId)
                    {
                        SpeakerComboBox.SelectedIndex = SpeakerComboBox.Items.Count - 1;
                    }
                }
                
                if (SpeakerComboBox.SelectedIndex == -1 && SpeakerComboBox.Items.Count > 0)
                {
                    SpeakerComboBox.SelectedIndex = Math.Min(Settings.VoiceSpeakerId, SpeakerComboBox.Items.Count - 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"スピーカー読み込みエラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"スピーカー読み込みエラー詳細: {ex}");
                // デフォルトスピーカーを設定
                SpeakerComboBox.Items.Clear();
                SpeakerComboBox.Items.Add(new ComboBoxItem { Content = "ずんだもん（デフォルト）", Tag = 3 });
                SpeakerComboBox.Items.Add(new ComboBoxItem { Content = "四国めたん", Tag = 2 });
                SpeakerComboBox.Items.Add(new ComboBoxItem { Content = "春日部つむぎ", Tag = 8 });
                SpeakerComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshFeedList()
        {
            FeedListBox.Items.Clear();
            foreach (var feed in Settings.RssFeeds)
            {
                var status = feed.IsEnabled ? "有効" : "無効";
                FeedListBox.Items.Add($"[{status}] {feed.Name} - {feed.Url}");
            }
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
            
            // 音声合成設定の保存
            Settings.EnableVoiceSynthesis = EnableVoiceCheckBox.IsChecked ?? false;
            Settings.VoiceVoxApiKey = VoiceApiKeyTextBox.Text.Trim();
            Settings.AutoReadArticles = AutoReadCheckBox.IsChecked ?? false;
            
            if (SpeakerComboBox.SelectedItem is ComboBoxItem selectedSpeaker)
            {
                Settings.VoiceSpeakerId = (int)(selectedSpeaker.Tag ?? 61);
                Console.WriteLine($"設定画面で話者IDを保存: {Settings.VoiceSpeakerId}");
            }
            
            SettingsChanged = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void TestVoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var apiKey = VoiceApiKeyTextBox.Text.Trim();
                var speakerId = 61; // デフォルト
                
                if (SpeakerComboBox.SelectedItem is ComboBoxItem selectedSpeaker)
                {
                    speakerId = (int)(selectedSpeaker.Tag ?? 61);
                }
                
                var voiceService = new VoiceVoxService(apiKey);
                var testText = "こんにちは。音声合成のテストです。";
                
                MessageBox.Show("音声合成をテスト中...", "テスト実行中", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var result = await voiceService.SynthesizeAndPlayAsync(testText, speakerId);
                
                if (result.IsSuccess)
                {
                    MessageBox.Show($"音声合成成功！\n\nスピーカー: {result.SpeakerName}\n音声が再生されます。", 
                                  "テスト成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"音声合成に失敗しました。\n\nエラー: {result.ErrorMessage}", 
                                  "テスト失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"音声合成テストエラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Feed管理イベントハンドラ
        private void FeedListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 選択変更時の処理（必要に応じて後で実装）
        }

        private void AddFeed_Click(object sender, RoutedEventArgs e)
        {
            if (Settings.RssFeeds.Count >= 10)
            {
                MessageBox.Show("最大10個までしか追加できません。", "制限", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new FeedEditDialog();
            if (dialog.ShowDialog() == true)
            {
                Settings.RssFeeds.Add(new RssFeedConfig
                {
                    Name = dialog.FeedName,
                    Url = dialog.FeedUrl,
                    IsEnabled = true
                });
                RefreshFeedList();
            }
        }

        private void EditFeed_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = FeedListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Settings.RssFeeds.Count) return;

            var feed = Settings.RssFeeds[selectedIndex];
            var dialog = new FeedEditDialog(feed.Name, feed.Url);
            if (dialog.ShowDialog() == true)
            {
                feed.Name = dialog.FeedName;
                feed.Url = dialog.FeedUrl;
                RefreshFeedList();
            }
        }

        private void DeleteFeed_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = FeedListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Settings.RssFeeds.Count) return;

            var feed = Settings.RssFeeds[selectedIndex];
            var result = MessageBox.Show($"'{feed.Name}' を削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Settings.RssFeeds.RemoveAt(selectedIndex);
                RefreshFeedList();
            }
        }

        private void ToggleFeed_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = FeedListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Settings.RssFeeds.Count) return;

            var feed = Settings.RssFeeds[selectedIndex];
            feed.IsEnabled = !feed.IsEnabled;
            RefreshFeedList();
        }

    }

    /// <summary>
    /// Feed編集ダイアログ
    /// </summary>
    public partial class FeedEditDialog : Window
    {
        public string FeedName { get; private set; } = "";
        public string FeedUrl { get; private set; } = "";
        
        private TextBox nameTextBox;
        private TextBox urlTextBox;

        public FeedEditDialog(string name = "", string url = "")
        {
            FeedName = name;
            FeedUrl = url;
            InitializeComponent();
            nameTextBox.Text = name;
            urlTextBox.Text = url;
        }

        private void InitializeComponent()
        {
            Width = 400;
            Height = 220;
            Title = "RSS Feed 編集";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Feed名
            var nameLabel = new Label { Content = "Feed名:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(nameLabel, 0);

            nameTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(nameTextBox, 1);

            // Feed URL
            var urlLabel = new Label { Content = "Feed URL:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(urlLabel, 2);

            urlTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            Grid.SetRow(urlTextBox, 3);

            // ボタンパネル
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "キャンセル", Width = 80, IsCancel = true };

            okButton.Click += (s, e) => {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text) || string.IsNullOrWhiteSpace(urlTextBox.Text))
                {
                    MessageBox.Show("Feed名とURLを両方入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                FeedName = nameTextBox.Text.Trim();
                FeedUrl = urlTextBox.Text.Trim();
                DialogResult = true;
            };

            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 4);

            grid.Children.Add(nameLabel);
            grid.Children.Add(nameTextBox);
            grid.Children.Add(urlLabel);
            grid.Children.Add(urlTextBox);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    /// <summary>
    /// 吹き出しウィンドウ（記事ナビゲーション機能強化）
    /// </summary>
    public partial class SpeechBubbleWindow : Window
    {
        public int CurrentArticleIndex { get; set; } = 0;
        public int TotalArticles { get; set; } = 0;
        public bool IsReadingAloud { get; set; } = false; // 読み上げ中フラグ
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
                Console.WriteLine($"自動送りタイマー発火: IsReadingAloud = {IsReadingAloud}");
                // 読み上げ中は自動送りをスキップ
                if (!IsReadingAloud)
                {
                    Console.WriteLine("自動送り実行");
                    _autoAdvanceTimer.Stop();
                    NextRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Console.WriteLine("読み上げ中のため自動送りをスキップ、5秒後に再チェック");
                    // 読み上げ中の場合は5秒後に再チェック
                    _autoAdvanceTimer.Stop();
                    _autoAdvanceTimer.Interval = TimeSpan.FromSeconds(5);
                    _autoAdvanceTimer.Start();
                }
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
            SourceLabel = new TextBlock 
            { 
                Text = "", 
                FontSize = 9, 
                Foreground = Brushes.Gray, 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

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

            var readAloudButton = new Button
            {
                Content = "🔊読み上げ",
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 9
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

            readAloudButton.Click += (s, e) => {
                OnReadAloud();
            };
            openButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                OnOpenArticle();
            };
            closeButton.Click += (s, e) => {
                StopAutoAdvanceTimer();
                Hide();
            };

            buttonPanel.Children.Add(readAloudButton);
            buttonPanel.Children.Add(openButton);
            buttonPanel.Children.Add(closeButton);

            // 出典元表示パネル（右下）
            var sourcePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 0)
            };

            sourcePanel.Children.Add(SourceLabel);

            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(contentArea);
            stackPanel.Children.Add(buttonPanel);
            stackPanel.Children.Add(sourcePanel);

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
        public TextBlock SourceLabel { get; private set; }
        public Image ThumbnailImage { get; private set; }

        public event EventHandler OpenArticleRequested;
        public event EventHandler PreviousRequested;
        public event EventHandler NextRequested;
        public event EventHandler ReadAloudRequested;

        private void OnOpenArticle()
        {
            OpenArticleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnReadAloud()
        {
            ReadAloudRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowBubble(Point position, string title, string content, string thumbnailUrl, int currentIndex, int totalCount, string sourceName = "")
        {
            CurrentArticleIndex = currentIndex;
            TotalArticles = totalCount;

            TitleBlock.Text = title.Length > 60 ? title.Substring(0, 60) + "..." : title;
            ContentBlock.Text = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
            CounterLabel.Content = $"{currentIndex + 1}/{totalCount}";
            SourceLabel.Text = !string.IsNullOrEmpty(sourceName) ? $"出典: {sourceName}" : "";

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
                // インターバルを通常の15秒にリセット
                _autoAdvanceTimer.Interval = TimeSpan.FromSeconds(15);
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
        private VoiceVoxService _voiceVoxService;
        private int _currentArticleIndex = 0;
        private SpeechBubbleWindow _speechBubble;
        private DispatcherTimer _rssTimer;
        private DispatcherTimer _weatherTimer;
        private DispatcherTimer _blinkTimer;
        private bool _isRssUpdating = false;
        private bool _isWeatherUpdating = false;
        private bool _isClickThrough = false;
        private MascotSettings _settings;

        // 瞬きアニメーション用
        private BitmapImage _normalImage;
        private BitmapImage _blinkImage;
        private Random _random = new Random();

        // 口パクアニメーション用
        private List<BitmapImage> _mouthImages = new List<BitmapImage>();
        private int _currentMouthIndex = 0;

        // GIFアニメーション用
        private string _animationGifPath;
        private bool _isAnimating = false;

        // リップシンク用音声解析
        private WasapiLoopbackCapture _audioCapture;
        private DispatcherTimer _lipSyncTimer;
        private bool _isLipSyncActive = false;
        private float[] _audioBuffer;

        public MascotWindow()
        {
            _settings = MascotSettings.Load();
            InitializeComponent();
            InitializeServices();
            LoadMascotImage();
            InitializeBlinkAnimation();
            InitializeLipSync();
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

            // ウィンドウクローズ時のイベントハンドラ追加
            Closing += MascotWindow_Closing;

            // メインコンテナ（Grid）
            var mainGrid = new Grid();

            MascotImage = new Image
            {
                Width = 150,     // 80→150
                Height = 270,    // 80→270
                Cursor = Cursors.Hand,
                Stretch = Stretch.Uniform,  // アスペクト比を保持して均等拡縮
                StretchDirection = StretchDirection.Both,
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
                    // 通常画像の読み込み
                    _normalImage = new BitmapImage();
                    _normalImage.BeginInit();
                    _normalImage.UriSource = new Uri(_settings.ImagePath);
                    _normalImage.DecodePixelWidth = 150;
                    // アスペクト比を保持するためHeightは自動計算させる
                    _normalImage.EndInit();
                    MascotImage.Source = _normalImage;

                    // 瞬き画像の読み込み（同じディレクトリでファイル名に_blinkを追加）
                    LoadBlinkImage();

                    // 口パク画像の読み込み（同じディレクトリでファイル名に_mouth1, _mouth2...を追加）
                    LoadMouthImages();

                    // アニメーションGIFの読み込み
                    LoadAnimationGif();
                }
                else
                {
                    // デフォルトの絵文字
                    MascotImage.Source = CreateEmojiImage("🐱");
                    _normalImage = null;
                    _blinkImage = null;
                    _mouthImages.Clear();
                    _animationGifPath = null;
                }
            }
            catch
            {
                // エラー時はデフォルト絵文字
                MascotImage.Source = CreateEmojiImage("🐱");
                _normalImage = null;
                _blinkImage = null;
                _mouthImages.Clear();
                _animationGifPath = null;
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

        private void LoadBlinkImage()
        {
            try
            {
                // 通常画像のファイル名から瞬き画像のパスを生成
                var directory = Path.GetDirectoryName(_settings.ImagePath);
                var fileName = Path.GetFileNameWithoutExtension(_settings.ImagePath);
                var extension = Path.GetExtension(_settings.ImagePath);
                var blinkPath = Path.Combine(directory, $"{fileName}_blink{extension}");

                if (File.Exists(blinkPath))
                {
                    _blinkImage = new BitmapImage();
                    _blinkImage.BeginInit();
                    _blinkImage.UriSource = new Uri(blinkPath);
                    _blinkImage.DecodePixelWidth = 150;
                    // アスペクト比を保持するためHeightは自動計算させる
                    _blinkImage.EndInit();
                    Console.WriteLine($"瞬き画像を読み込みました: {blinkPath}");
                }
                else
                {
                    Console.WriteLine($"瞬き画像が見つかりません: {blinkPath}");
                    _blinkImage = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"瞬き画像読み込みエラー: {ex.Message}");
                _blinkImage = null;
            }
        }

        private void LoadAnimationGif()
        {
            try
            {
                // 設定からGIFパスを取得（絶対パスまたは相対パス）
                var gifPath = _settings.AnimationGifPath;
                Console.WriteLine($"[GIF] 設定ファイルから取得したパス: {gifPath}");

                // 相対パスの場合、複数の場所を検索
                if (!Path.IsPathRooted(gifPath))
                {
                    // 1. カレントディレクトリ
                    var currentDirPath = Path.Combine(Directory.GetCurrentDirectory(), gifPath);
                    Console.WriteLine($"[GIF] カレントディレクトリで検索: {currentDirPath}");

                    // 2. 実行ファイルのディレクトリ
                    var exeDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    var exeDirPath = Path.Combine(exeDirectory, gifPath);
                    Console.WriteLine($"[GIF] 実行ディレクトリで検索: {exeDirPath}");

                    // 3. プロジェクトルート（C:\DesktopMascot_Enhanced）
                    var projectRootPath = Path.Combine(@"C:\DesktopMascot_Enhanced", gifPath);
                    Console.WriteLine($"[GIF] プロジェクトルートで検索: {projectRootPath}");

                    // 優先順位: カレント → プロジェクトルート → 実行ディレクトリ
                    if (File.Exists(currentDirPath))
                        gifPath = currentDirPath;
                    else if (File.Exists(projectRootPath))
                        gifPath = projectRootPath;
                    else if (File.Exists(exeDirPath))
                        gifPath = exeDirPath;
                }

                Console.WriteLine($"[GIF] 最終的なパス: {gifPath}");

                if (File.Exists(gifPath))
                {
                    // パスを保存（再生時に使用）
                    _animationGifPath = gifPath;
                    Console.WriteLine($"[GIF] ✓ アニメーションGIFを読み込みました: {gifPath}");
                }
                else
                {
                    Console.WriteLine($"[GIF] ✗ アニメーションGIFが見つかりません: {gifPath}");
                    _animationGifPath = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GIF] ✗ アニメーションGIF読み込みエラー: {ex.Message}");
                _animationGifPath = null;
            }
        }

        private void LoadMouthImages()
        {
            try
            {
                _mouthImages.Clear();

                // 通常画像のファイル名から口パク画像のパスを生成
                var directory = Path.GetDirectoryName(_settings.ImagePath);
                var fileName = Path.GetFileNameWithoutExtension(_settings.ImagePath);
                var extension = Path.GetExtension(_settings.ImagePath);

                // _mouth1.png, _mouth2.png, ... の形式で検索
                int mouthIndex = 1;
                while (true)
                {
                    var mouthPath = Path.Combine(directory, $"{fileName}_mouth{mouthIndex}{extension}");

                    if (File.Exists(mouthPath))
                    {
                        var mouthImage = new BitmapImage();
                        mouthImage.BeginInit();
                        mouthImage.UriSource = new Uri(mouthPath);
                        mouthImage.DecodePixelWidth = 150;
                        // アスペクト比を保持するためHeightは自動計算させる
                        mouthImage.EndInit();

                        _mouthImages.Add(mouthImage);
                        Console.WriteLine($"口パク画像を読み込みました: {mouthPath}");
                        mouthIndex++;
                    }
                    else
                    {
                        break; // 連続する番号の画像が見つからなくなったら終了
                    }
                }

                Console.WriteLine($"口パク画像を{_mouthImages.Count}枚読み込みました");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"口パク画像読み込みエラー: {ex.Message}");
                _mouthImages.Clear();
            }
        }

        private void InitializeBlinkAnimation()
        {
            if (_blinkImage == null) return;

            _blinkTimer = new DispatcherTimer();
            _blinkTimer.Tick += BlinkTimer_Tick;
            ResetBlinkTimer();
            Console.WriteLine("瞬きアニメーションを初期化しました");
        }

        private void BlinkTimer_Tick(object sender, EventArgs e)
        {
            _blinkTimer.Stop(); // 瞬き中は次の瞬きをストップ
            DoBlinkAnimation();
        }

        private async void DoBlinkAnimation()
        {
            if (_blinkImage == null || _normalImage == null) return;

            try
            {
                // デバッグ情報出力
                Console.WriteLine($"通常画像サイズ: {_normalImage.PixelWidth} x {_normalImage.PixelHeight}");
                Console.WriteLine($"瞬き画像サイズ: {_blinkImage.PixelWidth} x {_blinkImage.PixelHeight}");

                // 瞬きアニメーション
                MascotImage.Source = _blinkImage;  // 目を閉じる
                await Task.Delay(120);             // 0.12秒間
                MascotImage.Source = _normalImage; // 目を開ける

                Console.WriteLine("瞬きアニメーション実行");
                ResetBlinkTimer(); // 次の瞬きをスケジュール
            }
            catch (Exception ex)
            {
                Console.WriteLine($"瞬きアニメーションエラー: {ex.Message}");
                MascotImage.Source = _normalImage; // 安全のため通常画像に戻す
                ResetBlinkTimer();
            }
        }

        private void ResetBlinkTimer()
        {
            if (_blinkTimer == null || _blinkImage == null) return;

            // 読み上げ中は頻繁に瞬き、そうでなければ2-6秒間隔でランダム
            var minInterval = _speechBubble?.IsReadingAloud == true ? 800 : 2000;
            var maxInterval = _speechBubble?.IsReadingAloud == true ? 1500 : 6000;

            var interval = _random.Next(minInterval, maxInterval);
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(interval);
            _blinkTimer.Start();
        }

        // 口パクアニメーション制御メソッド
        private void SetMouthImage(int index)
        {
            if (_mouthImages.Count == 0 || index < 0 || index >= _mouthImages.Count) return;

            try
            {
                MascotImage.Source = _mouthImages[index];
                _currentMouthIndex = index;
                Console.WriteLine($"口パク画像を設定: index={index}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"口パク画像設定エラー: {ex.Message}");
            }
        }

        private void NextMouthImage()
        {
            if (_mouthImages.Count == 0) return;

            _currentMouthIndex = (_currentMouthIndex + 1) % _mouthImages.Count;
            SetMouthImage(_currentMouthIndex);
        }

        private void ResetToNormalImage()
        {
            if (_normalImage == null) return;

            try
            {
                MascotImage.Source = _normalImage;
                _currentMouthIndex = 0;
                Console.WriteLine("通常画像に戻しました");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通常画像復帰エラー: {ex.Message}");
            }
        }

        public int GetMouthImageCount()
        {
            return _mouthImages.Count;
        }

        // リップシンク機能
        private void InitializeLipSync()
        {
            try
            {
                if (_mouthImages.Count == 0)
                {
                    Console.WriteLine("口パク画像がないため、リップシンクを無効化");
                    return;
                }

                // オーディオキャプチャ初期化（システム音声をループバック）
                _audioCapture = new WasapiLoopbackCapture();
                _audioCapture.DataAvailable += OnAudioDataAvailable;
                _audioCapture.RecordingStopped += OnRecordingStopped;

                // リップシンクタイマー初期化
                _lipSyncTimer = new DispatcherTimer();
                _lipSyncTimer.Interval = TimeSpan.FromMilliseconds(50); // 20fps
                _lipSyncTimer.Tick += LipSyncTimer_Tick;

                Console.WriteLine("リップシンクシステム初期化完了");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"リップシンク初期化エラー: {ex.Message}");
            }
        }

        private void StartLipSync()
        {
            try
            {
                if (_audioCapture == null || _mouthImages.Count == 0) return;

                _isLipSyncActive = true;
                _audioCapture.StartRecording();
                _lipSyncTimer.Start();
                Console.WriteLine("リップシンク開始");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"リップシンク開始エラー: {ex.Message}");
            }
        }

        private void StopLipSync()
        {
            try
            {
                _isLipSyncActive = false;
                _audioCapture?.StopRecording();
                _lipSyncTimer?.Stop();

                // 通常画像に戻す
                ResetToNormalImage();
                Console.WriteLine("リップシンク停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"リップシンク停止エラー: {ex.Message}");
            }
        }

        private void OnAudioDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (!_isLipSyncActive) return;

                // 16bit PCMデータをfloat配列に変換
                int samplesCount = e.BytesRecorded / 2; // 16bit = 2 bytes per sample
                if (_audioBuffer == null || _audioBuffer.Length < samplesCount)
                {
                    _audioBuffer = new float[samplesCount];
                }

                for (int i = 0; i < samplesCount; i++)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                    _audioBuffer[i] = sample / 32768f; // 正規化
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音声データ処理エラー: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            Console.WriteLine("音声録音停止");
        }

        private void LipSyncTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!_isLipSyncActive || _audioBuffer == null || _mouthImages.Count == 0) return;

                // 音声レベル解析
                float averageVolume = CalculateAverageVolume();

                // 音声レベルに基づいて口パク制御
                if (averageVolume > 0.01f) // 閾値調整可能
                {
                    // 音量に応じて口の開き度合いを決定
                    int mouthIndex = (int)(averageVolume * _mouthImages.Count * 10) % _mouthImages.Count;
                    SetMouthImage(mouthIndex);
                }
                else
                {
                    // 無音時は通常画像
                    ResetToNormalImage();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"リップシンクタイマーエラー: {ex.Message}");
            }
        }

        private float CalculateAverageVolume()
        {
            if (_audioBuffer == null || _audioBuffer.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < _audioBuffer.Length; i++)
            {
                sum += Math.Abs(_audioBuffer[i]);
            }

            return sum / _audioBuffer.Length;
        }

        private void MascotWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // リップシンクリソースの解放
                StopLipSync();
                _audioCapture?.Dispose();
                _lipSyncTimer?.Stop();

                // 設定保存
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.Save();

                Console.WriteLine("マスコットウィンドウのリソースを解放しました");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ウィンドウクローズ時のエラー: {ex.Message}");
            }
        }

        private void InitializeServices()
        {
            _rssService = new RssService(_settings.RssFeeds);
            _weatherService = new WeatherService();
            
            // 音声合成サービス初期化
            Console.WriteLine($"設定読み込み完了: VoiceSpeakerId = {_settings.VoiceSpeakerId}, EnableVoiceSynthesis = {_settings.EnableVoiceSynthesis}");
            if (_settings.EnableVoiceSynthesis)
            {
                _voiceVoxService = CreateVoiceService(_settings.VoiceVoxApiKey);
                Console.WriteLine("音声合成サービスを初期化しました");
            }
            
            _speechBubble = new SpeechBubbleWindow();
            _speechBubble.OpenArticleRequested += OnOpenArticleRequested;
            _speechBubble.PreviousRequested += OnPreviousRequested;
            _speechBubble.NextRequested += OnNextRequested;
            _speechBubble.ReadAloudRequested += OnReadAloudRequested;
            
            // 初期RSS取得
            _ = UpdateRssAsync();
            
            // 初期天気取得
            _ = Task.Run(async () =>
            {
                bool success = await _weatherService.FetchWeatherAsync();
                if (!success)
                {
                    // API取得失敗時はエラー表示
                    Console.WriteLine("API取得失敗、エラー表示を使用");
                    _weatherService.CurrentWeather.WeatherCode = "❌";
                    _weatherService.CurrentWeather.WeatherText = "取得失敗";
                    _weatherService.CurrentWeather.MaxTemp = null;
                    _weatherService.CurrentWeather.MinTemp = null;
                }
                Dispatcher.Invoke(() => UpdateWeatherDisplay());
            });
        }

        private VoiceVoxService CreateVoiceService(string apiKey)
        {
            Console.WriteLine("CreateVoiceService: 音声合成サービスを作成中...");
            var service = new VoiceVoxService(apiKey);

            // リップシンク制御コールバック設定
            service.OnAudioPlayStarted = () =>
            {
                Console.WriteLine("音声再生開始 - リップシンク開始");
                StartLipSync();
            };
            service.OnAudioPlayEnded = () =>
            {
                Console.WriteLine("音声再生終了 - リップシンク停止");
                StopLipSync();
            };

            Console.WriteLine("CreateVoiceService: コールバック設定完了");
            return service;
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
            // アイドルアニメーションは記事送り時のみ実行するため、ここでは何もしない
            // 15秒ごとの自動アニメーションは無効化
        }

        private void AnimateMascot()
        {
            // GIFアニメーション再生（拡大アニメーションは廃止）
            if (!string.IsNullOrEmpty(_animationGifPath) && !_isAnimating)
            {
                PlayGifAnimation();
            }
        }

        private void PlayGifAnimation()
        {
            Console.WriteLine($"[GIF] PlayGifAnimation呼び出し - _isAnimating={_isAnimating}, _animationGifPath={(!string.IsNullOrEmpty(_animationGifPath) ? "有効" : "null")}");

            if (_isAnimating || string.IsNullOrEmpty(_animationGifPath))
            {
                Console.WriteLine($"[GIF] アニメーション中断: _isAnimating={_isAnimating}, _animationGifPath={(!string.IsNullOrEmpty(_animationGifPath) ? "有効" : "null")}");
                return;
            }

            _isAnimating = true;
            Console.WriteLine("[GIF] アニメーション開始");

            // 元の画像を保存（瞬き中でなければ現在の画像、瞬き中なら_normalImage）
            var originalImage = MascotImage.Source == _blinkImage ? _normalImage : MascotImage.Source;

            try
            {
                // WpfAnimatedGifを使用してGIFアニメーションを設定
                var gifImage = new BitmapImage();
                gifImage.BeginInit();
                gifImage.UriSource = new Uri(_animationGifPath);
                gifImage.EndInit();

                // ImageBehavior.AnimatedSourceでGIFアニメーションを有効化
                ImageBehavior.SetAnimatedSource(MascotImage, gifImage);

                // RepeatBehaviorを1回に設定（1ループのみ再生）
                ImageBehavior.SetRepeatBehavior(MascotImage, new RepeatBehavior(1));

                Console.WriteLine("[GIF] GIF画像に切り替え完了");

                // アニメーション完了時のイベントハンドラを設定
                void OnAnimationCompleted(object sender, EventArgs e)
                {
                    Console.WriteLine("[GIF] アニメーション完了");

                    // イベントハンドラを解除
                    ImageBehavior.RemoveAnimationCompletedHandler(MascotImage, OnAnimationCompleted);

                    // 元の画像に戻す
                    ImageBehavior.SetAnimatedSource(MascotImage, null);
                    MascotImage.Source = originalImage;
                    _isAnimating = false;
                    Console.WriteLine("[GIF] 元の画像に戻しました");
                }

                ImageBehavior.AddAnimationCompletedHandler(MascotImage, OnAnimationCompleted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GIF] エラー: {ex.Message}");
                _isAnimating = false;
            }
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
            if (_isRssUpdating)
            {
                Console.WriteLine("RSS更新要求をスキップしました（処理中）");
                return;
            }

            _isRssUpdating = true;
            try
            {
                var success = await _rssService.FetchRssAsync();
                if (success && _rssService.Articles.Any())
                {
                    _currentArticleIndex = 0;
                    AnimateMascot();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RSS更新エラー: {ex.Message}");
            }
            finally
            {
                _isRssUpdating = false;
            }
        }

        private async Task UpdateWeatherAsync()
        {
            if (_isWeatherUpdating)
            {
                Console.WriteLine("天気更新要求をスキップしました（処理中）");
                return;
            }

            _isWeatherUpdating = true;
            try
            {
                var success = await _weatherService.FetchWeatherAsync();
                if (success)
                {
                    UpdateWeatherDisplay();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"天気更新エラー: {ex.Message}");
            }
            finally
            {
                _isWeatherUpdating = false;
            }
        }

        private void UpdateWeatherDisplay()
        {
            var weather = _weatherService.CurrentWeather;
            Console.WriteLine($"UpdateWeatherDisplay呼び出し - 天気: {weather.WeatherText}, アイコン: {weather.WeatherCode}");
            
            // 天気アイコンを更新
            WeatherIcon.Text = weather.WeatherCode;
            Console.WriteLine($"WeatherIcon.Text設定: {WeatherIcon.Text}");
            
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
            Console.WriteLine($"TemperatureText.Text設定: {TemperatureText.Text}");
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
                Console.WriteLine($"設定変更を反映: VoiceSpeakerId = {settingsWindow.Settings.VoiceSpeakerId}");
                _settings = settingsWindow.Settings;
                _settings.Save();
                Console.WriteLine($"設定保存完了: VoiceSpeakerId = {_settings.VoiceSpeakerId}");

                // 画像を再読み込み
                LoadMascotImage();

                // 音声合成サービスを再初期化（読み上げ中でなければ）
                if (_settings.EnableVoiceSynthesis)
                {
                    // 読み上げ中でなければサービスを再初期化
                    if (_speechBubble?.IsReadingAloud != true)
                    {
                        _voiceVoxService?.Dispose();
                        _voiceVoxService = CreateVoiceService(_settings.VoiceVoxApiKey);
                        Console.WriteLine("音声合成サービスを再初期化しました");
                    }
                    else
                    {
                        Console.WriteLine("読み上げ中のため音声合成サービスの再初期化をスキップ");
                    }
                }
                else
                {
                    // 無効化は読み上げ中でも実行（安全性のため）
                    _voiceVoxService?.Dispose();
                    _voiceVoxService = null;
                    Console.WriteLine("音声合成サービスを無効化しました");
                }

                // RSS Feed設定が変更された場合は更新
                _rssService.UpdateFeedList(_settings.RssFeeds);
                _ = UpdateRssAsync();
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
                _speechBubble.ShowBubble(position, "記事がありません", "RSS更新を実行してください。", "", 0, 0, "");
                return;
            }

            var article = _rssService.Articles[_currentArticleIndex];
            var bubblePosition = GetBubblePosition();
            
            var title = article.Title;
            var description = article.Description;
            
            _speechBubble.ShowBubble(bubblePosition, title, description, article.ThumbnailUrl, _currentArticleIndex, _rssService.Articles.Count, article.SourceName);
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

        private async void OnReadAloudRequested(object sender, EventArgs e)
        {
            Console.WriteLine($"OnReadAloudRequested: 読み上げ要求を受信 - EnableVoiceSynthesis={_settings.EnableVoiceSynthesis}, VoiceVoxService={(_voiceVoxService != null ? "存在" : "null")}, Articles={_rssService.Articles.Count}件");

            if (!_settings.EnableVoiceSynthesis || _voiceVoxService == null || !_rssService.Articles.Any())
            {
                Console.WriteLine("OnReadAloudRequested: 読み上げ条件を満たしていないため終了");
                return;
            }

            try
            {
                // 読み上げ開始フラグを設定
                _speechBubble.IsReadingAloud = true;
                Console.WriteLine("読み上げ開始フラグを設定: IsReadingAloud = true");

                // 瞬きタイマーを更新（読み上げ中は頻繁に）
                ResetBlinkTimer();
                
                var article = _rssService.Articles[_currentArticleIndex];
                var textToRead = $"{article.Title}。{article.Description}";
                
                // 長いテキストは制限（VOICEVOX APIの制限に配慮）
                if (textToRead.Length > 300)
                {
                    textToRead = textToRead.Substring(0, 300) + "...";
                }

                Console.WriteLine($"音声合成開始: {textToRead.Substring(0, Math.Min(50, textToRead.Length))}...");
                Console.WriteLine($"使用する話者ID: {_settings.VoiceSpeakerId}");

                var result = await _voiceVoxService.SynthesizeAndPlayAsync(textToRead, _settings.VoiceSpeakerId);

                if (result.IsSuccess)
                {
                    Console.WriteLine($"音声再生成功: {result.SpeakerName}");
                    // SynthesizeAndPlayAsyncが完了 = 音声再生完了なので、フラグをリセット
                    _speechBubble.IsReadingAloud = false;
                    Console.WriteLine("音声再生完了によりフラグをリセット: IsReadingAloud = false");

                    // 瞬きタイマーを更新（通常間隔に戻す）
                    ResetBlinkTimer();
                }
                else
                {
                    Console.WriteLine($"音声合成失敗: {result.ErrorMessage}");
                    // 失敗時もフラグをリセット
                    _speechBubble.IsReadingAloud = false;

                    // 瞬きタイマーを更新（通常間隔に戻す）
                    ResetBlinkTimer();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音声読み上げエラー: {ex.Message}");
                // エラー時は即座にフラグをリセット
                if (_speechBubble != null)
                    _speechBubble.IsReadingAloud = false;

                // 瞬きタイマーを更新（通常間隔に戻す）
                ResetBlinkTimer();
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
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [STAThread]
        public static void Main()
        {
            // デバッグ用にコンソールウィンドウを表示
            AllocConsole();
            Console.WriteLine("=== DesktopMascot Enhanced Debug Console ===");
            
            var app = new App();
            app.Run();
        }
    }
}
