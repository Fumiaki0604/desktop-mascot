using System;
using System.Windows.Forms;
using System.Drawing;

class SimpleTest
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("=== デスクトップマスコット テスト開始 ===");
        
        try
        {
            Console.WriteLine("1. WinForms初期化中...");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Console.WriteLine("✓ WinForms初期化完了");
            
            Console.WriteLine("2. フォーム作成中...");
            var form = new Form()
            {
                Text = "DesktopMascot Test",
                Size = new Size(400, 300),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                BackColor = Color.LightBlue
            };
            Console.WriteLine("✓ フォーム作成完了");
            
            // ラベル追加
            var label = new Label()
            {
                Text = "デスクトップマスコット テスト\n\nこのウィンドウが表示されれば\nWinFormsは正常に動作しています",
                Size = new Size(350, 200),
                Location = new Point(25, 50),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("メイリオ", 12)
            };
            form.Controls.Add(label);
            
            Console.WriteLine("3. フォーム表示中...");
            form.Show();
            Console.WriteLine("✓ フォーム表示完了");
            Console.WriteLine("✓ テストアプリケーション起動成功！");
            
            Application.Run(form);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー発生: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
            Console.WriteLine("何かキーを押してください...");
            Console.ReadKey();
        }
        
        Console.WriteLine("=== アプリケーション終了 ===");
    }
}