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

        public SpeechBubbleWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Width = 420;  // 350 * 1.2 = 420
            Height = 280;
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

            PrevButton.Click += (s, e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
            NextButton.Click += (s, e) => NextRequested?.Invoke(this, EventArgs.Empty);

            navPanel.Children.Add(PrevButton);
            navPanel.Children.Add(NextButton);
            navPanel.Children.Add(CounterLabel);
            DockPanel.SetDock(navPanel, Dock.Right);

            TitleBlock = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 0)
            };

            headerPanel.Children.Add(navPanel);
            headerPanel.Children.Add(TitleBlock);

            // 内容
            ContentBlock = new TextBlock
            {
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            // サムネイル画像
            ThumbnailImage = new Image
            {
                MaxWidth = 120,
                MaxHeight = 80,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
                Stretch = Stretch.Uniform
            };

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

            openButton.Click += (s, e) => OnOpenArticle();
            closeButton.Click += (s, e) => Hide();

            buttonPanel.Children.Add(openButton);
            buttonPanel.Children.Add(closeButton);

            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(ContentBlock);
            stackPanel.Children.Add(ThumbnailImage);
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

            // 常時表示（自動非表示タイマー無効化）
            // ユーザーが×ボタンまたは他の記事をクリックするまで表示を維持
        }
    }

    /// <summary>
    /// メインのデスクトップマスコットウィンドウ（機能強化版）
    /// </summary>
    public partial class MascotWindow : Window
    {
        private RssService _rssService;
        private int _currentArticleIndex = 0;
        private SpeechBubbleWindow _speechBubble;
        private DispatcherTimer _idleTimer;
        private DispatcherTimer _rssTimer;
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
        }

        private void InitializeComponent()
        {
            Width = 100;
            Height = 100;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;

            MascotImage = new Image
            {
                Width = 80,
                Height = 80,
                Cursor = Cursors.Hand
            };

            Content = MascotImage;

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

        private void LoadMascotImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(_settings.ImagePath) && File.Exists(_settings.ImagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_settings.ImagePath);
                    bitmap.DecodePixelWidth = 80;
                    bitmap.DecodePixelHeight = 80;
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
            _speechBubble = new SpeechBubbleWindow();
            _speechBubble.OpenArticleRequested += OnOpenArticleRequested;
            _speechBubble.PreviousRequested += OnPreviousRequested;
            _speechBubble.NextRequested += OnNextRequested;

            // 初期RSS取得
            _ = UpdateRssAsync();
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save();
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

        private async Task UpdateRssAsync()
        {
            var success = await _rssService.FetchRssAsync();
            if (success && _rssService.Articles.Any())
            {
                _currentArticleIndex = 0;
                AnimateMascot();
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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
                var position = new Point(Left + Width + 10, Top);
                _speechBubble.ShowBubble(position, "記事がありません", "RSS更新を実行してください。", "", 0, 0);
                return;
            }

            var article = _rssService.Articles[_currentArticleIndex];
            var bubblePosition = new Point(Left + Width + 10, Top);
            _speechBubble.ShowBubble(bubblePosition, article.Title, article.Description, article.ThumbnailUrl, _currentArticleIndex, _rssService.Articles.Count);
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