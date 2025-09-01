using System;
using System.Windows.Forms;

class TestApp
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("アプリケーション開始");
        
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            Console.WriteLine("WinForms初期化完了");
            
            var form = new Form()
            {
                Text = "Test Form",
                Size = new System.Drawing.Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true
            };
            
            Console.WriteLine("フォーム作成完了");
            
            form.Show();
            Console.WriteLine("フォーム表示完了");
            
            Application.Run(form);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
            Console.ReadKey();
        }
    }
}