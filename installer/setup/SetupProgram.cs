using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgentGlow
{
    internal static class SetupProgram
    {
        [STAThread]
        private static void Main()
        {
            SystemTools.LoadDllsOnlyFromSystem();
            Directory.SetCurrentDirectory(Environment.SystemDirectory);
            bool isFirstInstance;
            using (new Mutex(true, InstallFolder.SetupMutex, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("Установка " + AppIdentity.Name + " уже идёт.", AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
            }
        }
    }
}
