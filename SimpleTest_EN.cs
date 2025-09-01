using System;
using System.Windows.Forms;
using System.Drawing;

class SimpleTest
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("=== Desktop Mascot Test Start ===");
        
        try
        {
            Console.WriteLine("1. Initializing WinForms...");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Console.WriteLine("OK WinForms initialized");
            
            Console.WriteLine("2. Creating form...");
            var form = new Form()
            {
                Text = "DesktopMascot Test",
                Size = new Size(400, 300),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                BackColor = Color.LightBlue
            };
            Console.WriteLine("OK Form created");
            
            var label = new Label()
            {
                Text = "Desktop Mascot Test\n\nIf you see this window,\nWinForms is working correctly!",
                Size = new Size(350, 200),
                Location = new Point(25, 50),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 12)
            };
            form.Controls.Add(label);
            
            Console.WriteLine("3. Showing form...");
            form.Show();
            Console.WriteLine("OK Form displayed");
            Console.WriteLine("SUCCESS Test application started!");
            
            Application.Run(form);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.Message);
            Console.WriteLine("Details: " + ex.StackTrace);
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
        
        Console.WriteLine("=== Application End ===");
    }
}