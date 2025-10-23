using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace DesktopMascot
{
    /// <summary>
    /// 音量・マイク制御用のオーバーレイUI
    /// マスコット画像の右側に横長ミニマルUIで表示
    /// </summary>
    public class VolumeControlOverlay : Form
    {
        private readonly MMDevice _outputDevice;
        private readonly MMDevice _inputDevice;

        private TrackBar _volumeSlider;
        private PictureBox _speakerIcon;
        private PictureBox _micIcon;
        private Label _volumeLabel;
        private Button _closeButton;

        private DateTime _lastVolumeChange = DateTime.Now;
        private float _lastVolume = 0.5f;

        private const float MAX_VOLUME = 0.8f; // 80%上限（聴覚保護）
        private const int CHANGE_INTERVAL_MS = 100; // レート制限

        public VolumeControlOverlay()
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"音量制御の初期化に失敗しました: {ex.Message}",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void InitializeWindow()
        {
            // ウィンドウスタイル
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(240, 240, 240); // 薄いグレー
            Size = new Size(280, 50); // 横長ミニマル

            // 半透明
            Opacity = 0.95;

            // 角丸効果（オプション）
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 10, 10));
        }

        private void SetupControls()
        {
            int leftMargin = 10;
            int topMargin = 10;

            // スピーカーアイコン（シンプルな絵文字風テキスト）
            _speakerIcon = new PictureBox
            {
                Size = new Size(30, 30),
                Location = new Point(leftMargin, topMargin),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent
            };
            _speakerIcon.Paint += DrawSpeakerIcon;

            // ボリュームスライダー
            _volumeSlider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Location = new Point(leftMargin + 35, topMargin - 5),
                Width = 100,
                Height = 40,
                Value = (int)(_outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100)
            };
            _volumeSlider.ValueChanged += OnVolumeChanged;
            _volumeSlider.MouseWheel += OnSliderMouseWheel;

            // 音量パーセント表示
            _volumeLabel = new Label
            {
                Text = $"{_volumeSlider.Value}%",
                Location = new Point(leftMargin + 140, topMargin + 7),
                Size = new Size(35, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 9, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            // マイクアイコン（クリックでトグル）
            _micIcon = new PictureBox
            {
                Size = new Size(30, 30),
                Location = new Point(leftMargin + 185, topMargin),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            _micIcon.Paint += DrawMicIcon;
            _micIcon.Click += OnMicToggle;

            // 閉じるボタン
            _closeButton = new Button
            {
                Text = "×",
                Size = new Size(20, 20),
                Location = new Point(Width - 25, 5),
                Font = new Font("Arial", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Click += (s, e) => Hide();

            // コントロール追加
            Controls.AddRange(new Control[] {
                _speakerIcon, _volumeSlider, _volumeLabel, _micIcon, _closeButton
            });
        }

        private void DrawSpeakerIcon(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // シンプルなスピーカーアイコン描画
            using (var brush = new SolidBrush(Color.Black))
            using (var pen = new Pen(Color.Black, 2))
            {
                // スピーカー本体（台形）
                var points = new Point[] {
                    new Point(8, 12),
                    new Point(15, 8),
                    new Point(15, 22),
                    new Point(8, 18)
                };
                g.FillPolygon(brush, points);

                // 音波（3本の弧）
                if (!_outputDevice.AudioEndpointVolume.Mute)
                {
                    g.DrawArc(pen, 16, 10, 8, 10, -60, 120);
                    g.DrawArc(pen, 19, 7, 12, 16, -60, 120);
                    g.DrawArc(pen, 22, 4, 16, 22, -60, 120);
                }
                else
                {
                    // ミュート時は×印
                    g.DrawLine(pen, 18, 10, 26, 20);
                    g.DrawLine(pen, 26, 10, 18, 20);
                }
            }
        }

        private void DrawMicIcon(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool isMuted = _inputDevice.AudioEndpointVolume.Mute;
            var color = isMuted ? Color.Red : Color.Green;

            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(color, 2))
            {
                // マイク本体（カプセル型）
                g.FillEllipse(brush, 10, 6, 10, 14);

                // スタンド
                g.DrawArc(pen, 8, 18, 14, 8, 0, 180);
                g.DrawLine(pen, 15, 22, 15, 26);
                g.DrawLine(pen, 12, 26, 18, 26);

                if (isMuted)
                {
                    // ミュート時は赤い×
                    using (var mutePen = new Pen(Color.Red, 3))
                    {
                        g.DrawLine(mutePen, 5, 5, 25, 25);
                        g.DrawLine(mutePen, 25, 5, 5, 25);
                    }
                }
            }
        }

        private void OnVolumeChanged(object sender, EventArgs e)
        {
            try
            {
                var newVolume = _volumeSlider.Value / 100f;
                var elapsed = DateTime.Now - _lastVolumeChange;

                // レート制限チェック
                if (elapsed.TotalMilliseconds < CHANGE_INTERVAL_MS)
                {
                    return;
                }

                // 急激な変化を検出（誤操作防止）
                int diff = Math.Abs(_volumeSlider.Value - (int)(_lastVolume * 100));
                if (diff > 30 && elapsed.TotalMilliseconds < 200)
                {
                    _volumeSlider.Value = (int)(_lastVolume * 100);
                    return;
                }

                // 音量上限適用
                newVolume = Math.Min(newVolume, MAX_VOLUME);

                // システム音量設定
                _outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;

                // ラベル更新
                _volumeLabel.Text = $"{_volumeSlider.Value}%";

                // アイコン再描画（ミュート状態反映）
                _speakerIcon.Invalidate();

                _lastVolume = newVolume;
                _lastVolumeChange = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音量変更エラー: {ex.Message}");
            }
        }

        private void OnMicToggle(object sender, EventArgs e)
        {
            try
            {
                // マイクミュートトグル
                bool currentMute = _inputDevice.AudioEndpointVolume.Mute;
                _inputDevice.AudioEndpointVolume.Mute = !currentMute;

                // アイコン再描画
                _micIcon.Invalidate();

                // ツールチップ更新
                var tooltip = new ToolTip();
                tooltip.SetToolTip(_micIcon, _inputDevice.AudioEndpointVolume.Mute ? "マイク: OFF" : "マイク: ON");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"マイクミュート切替エラー: {ex.Message}");
            }
        }

        private void OnSliderMouseWheel(object sender, MouseEventArgs e)
        {
            // ホイール操作で5%刻み調整
            int delta = e.Delta > 0 ? 5 : -5;
            int newValue = Math.Clamp(_volumeSlider.Value + delta, 0, 100);
            _volumeSlider.Value = newValue;
        }

        /// <summary>
        /// マスコットの位置に合わせて表示位置を更新
        /// </summary>
        public void UpdatePosition(Point mascotLocation, Size mascotSize)
        {
            // マスコット画像の右側に配置
            Location = new Point(
                mascotLocation.X + mascotSize.Width + 10,  // 10px右にオフセット
                mascotLocation.Y + (mascotSize.Height - Height) / 2  // 縦方向中央揃え
            );
        }

        /// <summary>
        /// フェードイン表示
        /// </summary>
        public async void ShowWithFade()
        {
            Opacity = 0;
            Show();
            while (Opacity < 0.95)
            {
                await System.Threading.Tasks.Task.Delay(20);
                Opacity += 0.1;
            }
        }

        /// <summary>
        /// フェードアウト非表示
        /// </summary>
        public async void HideWithFade()
        {
            while (Opacity > 0)
            {
                await System.Threading.Tasks.Task.Delay(20);
                Opacity -= 0.1;
            }
            Hide();
            Opacity = 0.95; // 次回表示用にリセット
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _outputDevice?.Dispose();
                _inputDevice?.Dispose();
            }
            base.Dispose(disposing);
        }

        // Win32 API for rounded corners
        [System.Runtime.InteropServices.DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse);
    }
}
