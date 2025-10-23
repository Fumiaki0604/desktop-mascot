using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace DesktopMascot
{
    /// <summary>
    /// 音量コントロールの位置設定
    /// </summary>
    public class VolumeControlSettings
    {
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
    }

    /// <summary>
    /// WPF版音量・マイク制御用のオーバーレイUI
    /// マスコット画像の右側に横長ミニマルUIで表示
    /// </summary>
    public class VolumeControlWindow : Window
    {
        private MMDevice _outputDevice;
        private MMDevice _inputDevice;

        private Slider _volumeSlider;
        private Canvas _speakerIcon;
        private Canvas _micIcon;
        private TextBlock _volumeLabel;
        private Button _closeButton;

        private DateTime _lastVolumeChange = DateTime.Now;
        private float _lastVolume = 0.5f;
        private bool _isDragging = false;
        private Point _dragStartPoint;

        private const float MAX_VOLUME = 0.8f; // 80%上限（聴覚保護）
        private const int CHANGE_INTERVAL_MS = 100; // レート制限
        private const string SETTINGS_FILE = "VolumeControlSettings.json";

        public VolumeControlWindow()
        {
            try
            {
                // Core Audio APIデバイス取得
                var enumerator = new MMDeviceEnumerator();
                _outputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _inputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

                _lastVolume = _outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar;

                InitializeWindow();
                SetupControls();
                LoadPosition();
                SetupDragHandlers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"音量制御の初期化に失敗しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void LoadPosition()
        {
            try
            {
                if (File.Exists(SETTINGS_FILE))
                {
                    var json = File.ReadAllText(SETTINGS_FILE);
                    var settings = JsonSerializer.Deserialize<VolumeControlSettings>(json);
                    if (settings != null)
                    {
                        Left = settings.Left;
                        Top = settings.Top;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"位置読み込みエラー: {ex.Message}");
            }
        }

        private void SavePosition()
        {
            try
            {
                var settings = new VolumeControlSettings
                {
                    Left = Left,
                    Top = Top
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SETTINGS_FILE, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"位置保存エラー: {ex.Message}");
            }
        }

        private void SetupDragHandlers()
        {
            // Borderに直接ドラッグハンドラを設定
            var border = (Border)Content;
            border.MouseLeftButtonDown += OnBorderMouseDown;
            border.MouseMove += OnBorderMouseMove;
            border.MouseLeftButtonUp += OnBorderMouseUp;
        }

        private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Borderの空白部分をクリックした場合のみドラッグ開始
            var border = (Border)sender;
            if (e.OriginalSource == border)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                border.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnBorderMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPoint;
                Left += offset.X;
                Top += offset.Y;
                e.Handled = true;
            }
        }

        private void OnBorderMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                var border = (Border)sender;
                border.ReleaseMouseCapture();
                SavePosition();
                e.Handled = true;
            }
        }

        private void InitializeWindow()
        {
            // ウィンドウスタイル
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent; // 完全透明
            Topmost = true;
            ShowInTaskbar = false;
            Width = 280;
            Height = 50;
            ResizeMode = ResizeMode.NoResize;

            // 角丸効果（背景を半透明に：マウスイベント取得のため）
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // ほぼ透明だがマウスイベント取得可能
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), // 白い枠線
                BorderThickness = new Thickness(2)
            };

            Content = border;
        }

        private void SetupControls()
        {
            var mainGrid = new Grid();
            var border = (Border)Content;
            border.Child = mainGrid;

            // スピーカーアイコン（クリックでミュートトグル）
            _speakerIcon = CreateSpeakerIcon();
            _speakerIcon.Margin = new Thickness(10, 10, 0, 0);
            _speakerIcon.Width = 30;
            _speakerIcon.Height = 30;
            _speakerIcon.HorizontalAlignment = HorizontalAlignment.Left;
            _speakerIcon.VerticalAlignment = VerticalAlignment.Top;
            _speakerIcon.Cursor = Cursors.Hand;
            _speakerIcon.MouseLeftButtonDown += OnSpeakerToggle;

            // ボリュームスライダー
            _volumeSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                IsSnapToTickEnabled = false,
                Width = 100,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(45, 15, 0, 0),
                Value = _outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100
            };
            _volumeSlider.ValueChanged += OnVolumeChanged;
            _volumeSlider.MouseWheel += OnSliderMouseWheel;

            // 音量パーセント表示
            _volumeLabel = new TextBlock
            {
                Text = $"{(int)_volumeSlider.Value}%",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, // 白色に変更
                Width = 35,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(150, 15, 0, 0)
            };

            // マイクアイコン
            _micIcon = CreateMicIcon();
            _micIcon.Margin = new Thickness(195, 10, 0, 0);
            _micIcon.Width = 30;
            _micIcon.Height = 30;
            _micIcon.HorizontalAlignment = HorizontalAlignment.Left;
            _micIcon.VerticalAlignment = VerticalAlignment.Top;
            _micIcon.Cursor = Cursors.Hand;
            _micIcon.MouseLeftButtonDown += OnMicToggle;

            // 閉じるボタン
            _closeButton = new Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 5, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            _closeButton.Click += (s, e) => Hide();

            // コントロール追加
            mainGrid.Children.Add(_speakerIcon);
            mainGrid.Children.Add(_volumeSlider);
            mainGrid.Children.Add(_volumeLabel);
            mainGrid.Children.Add(_micIcon);
            mainGrid.Children.Add(_closeButton);
        }

        private Canvas CreateSpeakerIcon()
        {
            var canvas = new Canvas();
            UpdateSpeakerIcon(canvas);
            return canvas;
        }

        private void UpdateSpeakerIcon(Canvas canvas)
        {
            canvas.Children.Clear();

            bool isMuted = _outputDevice?.AudioEndpointVolume.Mute ?? false;
            var color = Brushes.White; // 白色に変更（背景が透明なので見やすい）

            // スピーカー本体（台形）
            var polygon = new Polygon
            {
                Points = new PointCollection(new[]
                {
                    new Point(8, 12),
                    new Point(15, 8),
                    new Point(15, 22),
                    new Point(8, 18)
                }),
                Fill = color,
                Stroke = color,
                StrokeThickness = 1
            };
            canvas.Children.Add(polygon);

            if (!isMuted)
            {
                // 音波（3本の弧）
                for (int i = 0; i < 3; i++)
                {
                    var arc = new System.Windows.Shapes.Path
                    {
                        Stroke = color,
                        StrokeThickness = 2,
                        Data = Geometry.Parse($"M {18 + i * 3},10 A {4 + i * 3},{8 + i * 6} 0 0 1 {18 + i * 3},20")
                    };
                    canvas.Children.Add(arc);
                }
            }
            else
            {
                // ミュート時は×印
                var line1 = new Line { X1 = 18, Y1 = 10, X2 = 26, Y2 = 20, Stroke = color, StrokeThickness = 2 };
                var line2 = new Line { X1 = 26, Y1 = 10, X2 = 18, Y2 = 20, Stroke = color, StrokeThickness = 2 };
                canvas.Children.Add(line1);
                canvas.Children.Add(line2);
            }
        }

        private Canvas CreateMicIcon()
        {
            var canvas = new Canvas();
            UpdateMicIcon(canvas);
            return canvas;
        }

        private void UpdateMicIcon(Canvas canvas)
        {
            canvas.Children.Clear();

            bool isMuted = _inputDevice?.AudioEndpointVolume.Mute ?? false;
            var color = isMuted ? Brushes.Red : Brushes.Green;

            // マイク本体（カプセル型）
            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 14,
                Fill = color,
                Margin = new Thickness(10, 6, 0, 0)
            };
            canvas.Children.Add(ellipse);

            // スタンド（弧）
            var arc = new System.Windows.Shapes.Path
            {
                Stroke = color,
                StrokeThickness = 2,
                Data = Geometry.Parse("M 8,18 A 7,4 0 0 0 22,18")
            };
            canvas.Children.Add(arc);

            // スタンド（縦線）
            var line1 = new Line { X1 = 15, Y1 = 22, X2 = 15, Y2 = 26, Stroke = color, StrokeThickness = 2 };
            canvas.Children.Add(line1);

            // スタンド（底）
            var line2 = new Line { X1 = 12, Y1 = 26, X2 = 18, Y2 = 26, Stroke = color, StrokeThickness = 2 };
            canvas.Children.Add(line2);

            if (isMuted)
            {
                // ミュート時は赤い×
                var line3 = new Line { X1 = 5, Y1 = 5, X2 = 25, Y2 = 25, Stroke = Brushes.Red, StrokeThickness = 3 };
                var line4 = new Line { X1 = 25, Y1 = 5, X2 = 5, Y2 = 25, Stroke = Brushes.Red, StrokeThickness = 3 };
                canvas.Children.Add(line3);
                canvas.Children.Add(line4);
            }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_outputDevice == null || _volumeLabel == null) return;

            try
            {
                var newVolume = (float)(_volumeSlider.Value / 100.0);

                // 音量上限適用
                newVolume = Math.Min(newVolume, MAX_VOLUME);

                // システム音量設定
                _outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;

                // ラベル更新（常に実行）
                _volumeLabel.Text = $"{(int)_volumeSlider.Value}%";

                // アイコン再描画（ミュート状態反映）
                UpdateSpeakerIcon(_speakerIcon);

                _lastVolume = newVolume;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"音量変更エラー: {ex.Message}");
            }
        }

        private void OnSpeakerToggle(object sender, MouseButtonEventArgs e)
        {
            if (_outputDevice == null) return;

            try
            {
                // スピーカーミュートトグル
                bool currentMute = _outputDevice.AudioEndpointVolume.Mute;
                _outputDevice.AudioEndpointVolume.Mute = !currentMute;

                // デバッグ出力
                System.Diagnostics.Debug.WriteLine($"スピーカーミュート切替: {currentMute} → {!currentMute}");
                System.Diagnostics.Debug.WriteLine($"実際の状態: {_outputDevice.AudioEndpointVolume.Mute}");

                // アイコン再描画
                UpdateSpeakerIcon(_speakerIcon);

                // ツールチップ更新（視覚的フィードバック）
                _speakerIcon.ToolTip = _outputDevice.AudioEndpointVolume.Mute ? "スピーカー: ミュート" : "スピーカー: ON";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"スピーカーミュート切替エラー: {ex.Message}");
                MessageBox.Show($"スピーカーミュート切替エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnMicToggle(object sender, MouseButtonEventArgs e)
        {
            if (_inputDevice == null) return;

            try
            {
                // マイクミュートトグル
                bool currentMute = _inputDevice.AudioEndpointVolume.Mute;
                _inputDevice.AudioEndpointVolume.Mute = !currentMute;

                // デバッグ出力
                System.Diagnostics.Debug.WriteLine($"マイクミュート切替: {currentMute} → {!currentMute}");
                System.Diagnostics.Debug.WriteLine($"実際の状態: {_inputDevice.AudioEndpointVolume.Mute}");

                // アイコン再描画
                UpdateMicIcon(_micIcon);

                // ツールチップ更新（視覚的フィードバック）
                _micIcon.ToolTip = _inputDevice.AudioEndpointVolume.Mute ? "マイク: ミュート" : "マイク: ON";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"マイクミュート切替エラー: {ex.Message}");
                MessageBox.Show($"マイクミュート切替エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSliderMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // ホイール操作で5%刻み調整
            double delta = e.Delta > 0 ? 5 : -5;
            double newValue = Math.Clamp(_volumeSlider.Value + delta, 0, 100);
            _volumeSlider.Value = newValue;
        }

        /// <summary>
        /// マスコットの位置に合わせて表示位置を更新
        /// </summary>
        public void UpdatePosition(Point mascotLocation, double mascotWidth, double mascotHeight)
        {
            // マスコット画像の右側に配置
            Left = mascotLocation.X + mascotWidth + 10;  // 10px右にオフセット
            Top = mascotLocation.Y + (mascotHeight - Height) / 2;  // 縦方向中央揃え
        }

        /// <summary>
        /// フェードイン表示
        /// </summary>
        public void ShowWithFade()
        {
            Opacity = 0;
            Show();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (s, e) =>
            {
                Opacity += 0.1;
                if (Opacity >= 0.95)
                {
                    Opacity = 0.95;
                    timer.Stop();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// フェードアウト非表示
        /// </summary>
        public void HideWithFade()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (s, e) =>
            {
                Opacity -= 0.1;
                if (Opacity <= 0)
                {
                    Hide();
                    Opacity = 0.95; // 次回表示用にリセット
                    timer.Stop();
                }
            };
            timer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _outputDevice?.Dispose();
            _inputDevice?.Dispose();
            base.OnClosed(e);
        }
    }
}
