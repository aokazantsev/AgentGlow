using System;
using System.Windows.Forms;

namespace ClaudeGlow
{
    internal static class SetupProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
