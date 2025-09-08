using System;
using System.Threading;
using System.Windows.Forms;

namespace DesktopMascot
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // 単一インスタンス制御
            using (var mutex = new Mutex(true, "DesktopMascot_SingleInstance", out bool isNewInstance))
            {
                if (!isNewInstance)
                {
                    Console.WriteLine("既にDesktopMascotが起動中です。終了します。");
                    return;
                }

                Console.WriteLine("=== Application Start ===");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                try
                {
                    Console.WriteLine("Settings.Load()開始...");
                    Settings.Load();
                    Console.WriteLine("Settings.Load()完了");
                    
                    Console.WriteLine("MainForm作成開始...");
                    var mainForm = new MainForm();
                    Console.WriteLine("MainForm作成完了");
                    
                    Console.WriteLine("Application.Run開始...");
                    Application.Run(mainForm);
                    Console.WriteLine("Application.Run終了");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Program.Main ERROR: {ex.Message}");
                    Console.WriteLine($"StackTrace: {ex.StackTrace}");
                    MessageBox.Show($"アプリケーションエラー: {ex.Message}", "エラー", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}