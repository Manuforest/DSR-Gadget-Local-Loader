using System;
using System.Windows.Forms;

namespace DSR_QuickWarp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new QuickWarpHost());
        }
    }
}
