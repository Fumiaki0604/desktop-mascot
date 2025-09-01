using System;
using System.Windows.Forms;

namespace DesktopMascot
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            try
            {
                Settings.Load();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アプリケーションエラー: {ex.Message}", "エラー", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}