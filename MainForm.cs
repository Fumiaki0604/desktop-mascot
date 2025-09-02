using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace DesktopMascot
{
    public partial class MainForm : Form
    {
        private RssTicker _rssTicker;
        private Rectangle _bubbleRect = Rectangle.Empty;
        private Rectangle _textRect = Rectangle.Empty;
        private Point _anchorPoint = Point.Empty;
        private Image _mascotImage;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private Timer _renderTimer;
        private bool _bubbleVisible = true;
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private Rectangle _linkButtonRect = Rectangle.Empty;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const uint LWA_COLORKEY = 0x1;

        public MainForm()
        {
            try
            {
                Console.WriteLine("=== MainForm Constructor Start ===");
                InitializeComponent();
                Console.WriteLine("InitializeComponent completed");
                
                InitializeWindow();
                Console.WriteLine("InitializeWindow completed");
                
                InitializeComponents();
                Console.WriteLine("InitializeComponents completed");
                
                LoadSettings();
                Console.WriteLine("LoadSettings completed");
                Console.WriteLine("=== MainForm Constructor End ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in MainForm Constructor: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            
            AutoScaleDimensions = new SizeF(6F, 12F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(640, 320);  // 適切なサイズ
            FormBorderStyle = FormBorderStyle.None;
            Name = "MainForm";
            Text = "DesktopMascot";
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;  // 透明化用のキーカラー
            TransparencyKey = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(200, 200);  // 初期位置
            TopMost = true;
            
            ResumeLayout(false);
        }

        private void InitializeWindow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            
            TopMost = Settings.Current.TopMost;
            KeyPreview = true;
            
            var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED;
            if (Settings.Current.ClickThrough)
            {
                exStyle |= WS_EX_TRANSPARENT;
            }
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);
            
            SetLayeredWindowAttributes(Handle, 0xFF00FF, 0, LWA_COLORKEY);
        }

        private void InitializeComponents()
        {
            try
            {
                Console.WriteLine("Creating RssTicker...");
                _rssTicker = new RssTicker(
                    Settings.Current.Rss.PerItemSeconds,
                    Settings.Current.FeedRefreshMinutes);
                
                _rssTicker.Updated += OnRssUpdated;
                _rssTicker.ErrorOccurred += OnRssError;
                Console.WriteLine("RssTicker created successfully");

                Console.WriteLine("Creating render timer...");
                _renderTimer = new Timer();
                _renderTimer.Interval = 1000 / Settings.Current.Fps;
                _renderTimer.Tick += OnRenderTick;
                _renderTimer.Start();
                Console.WriteLine("Render timer started");

                Console.WriteLine("Creating notify icon...");
                CreateNotifyIcon();
                Console.WriteLine("Notify icon created");
                
                Console.WriteLine("Creating context menu...");
                CreateContextMenu();
                Console.WriteLine("Context menu created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in InitializeComponents: {ex.Message}");
                throw;
            }
        }

        private void LoadSettings()
        {
            try
            {
                Settings.Load();
                var pos = Settings.GetWindowPosition();
                
                Location = pos;
                
                LoadMascotImage();
                
                _rssTicker?.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in LoadSettings: {ex.Message}");
                throw;
            }
        }

        private void LoadMascotImage()
        {
            var imagePath = Settings.Current.ImagePath;
            
            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    _mascotImage = Image.FromFile(imagePath);
                }
                catch
                {
                    _mascotImage = null;
                }
            }
            
            // ウィンドウサイズは左側バブル + 中央空間 + 右側画像（150×150）
            var bubbleWidth = Settings.Current.Bubble.Width;
            var imageSize = 150;
            var spacing = 60; // バブルと画像の間のスペース（縮小）
            var totalWidth = bubbleWidth + spacing + imageSize + 40; // 左右余白20px×2
            var totalHeight = Math.Max(imageSize + 40, 220); // 上下余白20px×2、最小高さ220px
            
            ClientSize = new Size(totalWidth, totalHeight);
        }

        private void OnRenderTick(object sender, EventArgs e)
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                var graphics = e.Graphics;
                graphics.Clear(Color.Magenta);  // 透明化用のキーカラー
                // アンチエイリアシングを無効化して透明化処理を正確に
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                
                // 画像を右側に配置（150×150）
                var imageSize = 150;
                var imageX = ClientSize.Width - imageSize - 20; // 右から20pxの余白
                var imageY = ClientSize.Height - imageSize - 20; // 下から20pxの余白
                
                if (_mascotImage != null)
                {
                    var destRect = new Rectangle(imageX, imageY, imageSize, imageSize);
                    // 画像描画時は補間モードを最近傍に設定してマゼンタの混色を防ぐ
                    var originalInterpolationMode = graphics.InterpolationMode;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    graphics.DrawImage(_mascotImage, destRect);
                    graphics.InterpolationMode = originalInterpolationMode;
                }
                else
                {
                    using (var brush = new SolidBrush(Color.White))
                    {
                        graphics.FillEllipse(brush, imageX + 25, imageY + 25, 100, 100);
                    }
                    using (var font = new Font("Arial", 14))
                    using (var brush = new SolidBrush(Color.Black))
                    {
                        graphics.DrawString("MASCOT", font, brush, imageX + 35, imageY + 70);
                    }
                }

                // アンカーポイントを計算（画像の左辺中央）
                _anchorPoint = new Point(imageX, imageY + imageSize / 2);

                if (_bubbleVisible)
                {
                    DrawBubbleComicStyle(graphics);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Paint error: {ex.Message}");
            }
        }

        private void DrawBubbleComicStyle(Graphics graphics)
        {
            var text = _rssTicker?.GetDisplayText() ?? "読み込み中…";
            if (string.IsNullOrEmpty(text)) text = "記事がありません";

            var settings = Settings.Current.Bubble;
            var bubbleWidth = settings.Width;
            var padding = settings.Padding;
            var cornerRadius = settings.CornerRadius;

            // 吹き出し描画時のみアンチエイリアシングを有効化
            var originalSmoothingMode = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var font = new Font(settings.FontName, settings.FontSize))
            {
                // テキストサイズを測定
                var stringFormat = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.LineLimit
                };
                
                var measuredSize = graphics.MeasureString(text, font, bubbleWidth - padding * 2, stringFormat);
                var lines = Math.Min(settings.LineClamp, (int)Math.Ceiling(measuredSize.Height / font.Height));
                var textHeight = lines * font.Height;
                var bubbleHeight = textHeight + padding * 2;

                // 吹き出しを左側に配置（アンカーポイントの左側）
                var bubbleX = _anchorPoint.X - bubbleWidth - 30;
                var bubbleY = _anchorPoint.Y - bubbleHeight / 2;

                // 画面外補正
                var workArea = Screen.FromPoint(_anchorPoint).WorkingArea;
                if (bubbleX < workArea.Left) bubbleX = workArea.Left + 10;
                if (bubbleY < workArea.Top) bubbleY = workArea.Top + 10;
                if (bubbleY + bubbleHeight > workArea.Bottom) bubbleY = workArea.Bottom - bubbleHeight - 10;

                _bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleWidth, bubbleHeight);

                // 黒い吹き出しを描画
                var bgColor = Color.FromArgb((int)(settings.Opacity * 255), 0, 0, 0);
                using (var bgBrush = new SolidBrush(bgColor))
                using (var borderPen = new Pen(Color.FromArgb(200, 100, 100, 100), 2))
                {
                    DrawRoundedRectangle(graphics, _bubbleRect, cornerRadius, bgBrush, borderPen);
                }

                // テキストを描画（URL部分を除く）
                var currentItem = _rssTicker?.GetCurrentItem();
                var displayText = currentItem?.Title ?? text;
                if (!string.IsNullOrEmpty(currentItem?.Summary))
                {
                    displayText += $"\n\n{currentItem.Summary}";
                }
                
                _textRect = new Rectangle(bubbleX + padding, bubbleY + padding, 
                                        bubbleWidth - padding * 2, textHeight - 40);
                using (var textBrush = new SolidBrush(Color.White))
                {
                    graphics.DrawString(displayText, font, textBrush, _textRect, stringFormat);
                }
                
                // リンクボタンを描画
                if (currentItem != null && !string.IsNullOrEmpty(currentItem.Link))
                {
                    var buttonY = bubbleY + bubbleHeight - 35;
                    _linkButtonRect = new Rectangle(bubbleX + padding, buttonY, 100, 25);
                    
                    using (var buttonBrush = new SolidBrush(Color.FromArgb(255, 0, 255, 0))) // 蛍光緑
                    using (var buttonBorderPen = new Pen(Color.FromArgb(255, 0, 200, 0), 2))
                    using (var buttonTextBrush = new SolidBrush(Color.Black))
                    {
                        graphics.FillRectangle(buttonBrush, _linkButtonRect);
                        graphics.DrawRectangle(buttonBorderPen, _linkButtonRect);
                        
                        var buttonFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        graphics.DrawString("記事を開く", font, buttonTextBrush, _linkButtonRect, buttonFormat);
                        buttonFormat.Dispose();
                    }
                }
                
                stringFormat.Dispose();
            }
            
            // 元のSmoothingModeに戻す
            graphics.SmoothingMode = originalSmoothingMode;
        }

        private void DrawRoundedRectangle(Graphics graphics, Rectangle rect, int cornerRadius, Brush brush, Pen pen)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var diameter = cornerRadius * 2;
                var size = new Size(diameter, diameter);
                var arc = new Rectangle(rect.Location, size);
                
                // 左上の角
                path.AddArc(arc, 180, 90);
                
                // 右上の角
                arc.X = rect.Right - diameter;
                path.AddArc(arc, 270, 90);
                
                // 右下の角
                arc.Y = rect.Bottom - diameter;
                path.AddArc(arc, 0, 90);
                
                // 左下の角
                arc.X = rect.Left;
                path.AddArc(arc, 90, 90);
                
                path.CloseFigure();
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }
        }





        private void OnRssUpdated()
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(OnRssUpdated));
                    return;
                }
                
                Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RSS Update error: {ex.Message}");
            }
        }

        private void OnRssError(string error)
        {
            Console.WriteLine($"RSS Error: {error}");
        }

        private void CreateNotifyIcon()
        {
            _notifyIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                Text = "DesktopMascot",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => { 
                Visible = !Visible; 
                if (Visible)
                {
                    Show();
                    BringToFront();
                    Focus();
                }
            };
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("Show Window", null, (s, e) => { Show(); BringToFront(); });
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            // RSS操作
            _contextMenu.Items.Add("Next Article", null, (s, e) => _rssTicker?.NextItem());
            _contextMenu.Items.Add("Previous Article", null, (s, e) => _rssTicker?.PreviousItem());
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            // バブル操作
            var bubbleMenuItem = new ToolStripMenuItem("Show Bubble");
            bubbleMenuItem.CheckOnClick = true;
            bubbleMenuItem.Checked = _bubbleVisible;
            bubbleMenuItem.Click += (s, e) => 
            {
                _bubbleVisible = bubbleMenuItem.Checked;
                Invalidate();
            };
            _contextMenu.Items.Add(bubbleMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            _contextMenu.Items.Add("Change Image", null, (s, e) => ShowImageChangeDialog());
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("Exit", null, (s, e) => Close());

            ContextMenuStrip = _contextMenu;
            _notifyIcon.ContextMenuStrip = _contextMenu;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Settings.SaveWindowPosition(Location);
            
            _renderTimer?.Stop();
            _rssTicker?.Stop();
            _rssTicker?.Dispose();
            _notifyIcon?.Dispose();
            _mascotImage?.Dispose();
            
            base.OnFormClosing(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            
            if (e.Button == MouseButtons.Right)
            {
                // 右クリックで次の記事に移動
                if (_bubbleVisible && _bubbleRect.Contains(e.Location))
                {
                    _rssTicker?.Next();
                    return;
                }
            }
            else if (e.Button == MouseButtons.Left && !_isDragging)
            {
                // 吹き出し以外の場所での画像変更
                if (!(_bubbleVisible && _bubbleRect.Contains(e.Location)))
                {
                    ShowImageChangeDialog();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            if (e.KeyCode == Keys.N)
            {
                _rssTicker?.NextItem();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.P)
            {
                _rssTicker?.PreviousItem();
                e.Handled = true;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            
            if (ModifierKeys == Keys.Control)
            {
                if (e.Delta > 0)
                {
                    _rssTicker?.PreviousItem();
                }
                else
                {
                    _rssTicker?.NextItem();
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _rssTicker?.Pause();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _rssTicker?.Resume();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            
            if (e.Button == MouseButtons.Left)
            {
                // リンクボタンクリック
                if (_bubbleVisible && _linkButtonRect.Contains(e.Location))
                {
                    var currentItem = _rssTicker?.GetCurrentItem();
                    if (currentItem != null && !string.IsNullOrEmpty(currentItem.Link))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(currentItem.Link) 
                            { 
                                UseShellExecute = true 
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to open URL: {ex.Message}");
                        }
                    }
                    return;
                }
                
                // 吹き出しクリックで次の記事表示
                if (_bubbleVisible && _bubbleRect.Contains(e.Location))
                {
                    _rssTicker?.Next();
                    return;
                }
                
                // マスコット画像のドラッグ開始
                _isDragging = true;
                _dragStartPoint = e.Location;
                this.Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            
            if (_isDragging && e.Button == MouseButtons.Left)
            {
                // ウィンドウを移動
                var currentLocation = this.Location;
                this.Location = new Point(
                    currentLocation.X + (e.X - _dragStartPoint.X),
                    currentLocation.Y + (e.Y - _dragStartPoint.Y)
                );
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            
            if (_isDragging)
            {
                _isDragging = false;
                this.Capture = false;
                
                // 位置を設定に保存（オプション）
                // Settings.Current.Position = this.Location;
                // Settings.Save();
            }
        }

        private void ShowImageChangeDialog()
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "マスコット画像を選択";
                openFileDialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|すべてのファイル (*.*)|*.*";
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // 古い画像を解放
                        _mascotImage?.Dispose();
                        
                        // 新しい画像を読み込み
                        _mascotImage = Image.FromFile(openFileDialog.FileName);
                        
                        // 設定を更新
                        Settings.Current.ImagePath = openFileDialog.FileName;
                        Settings.Save();
                        
                        // 再描画
                        Invalidate();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"画像の読み込みに失敗しました: {ex.Message}", 
                            "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}