using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgentGlow
{
    internal static class SetupProgram
    {
        [STAThread]
        private static int Main()
        {
            SystemTools.LoadDllsOnlyFromSystem();
            Directory.SetCurrentDirectory(Environment.SystemDirectory);
            bool isFirstInstance;
            using (new Mutex(true, InstallFolder.SetupMutex, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    SetupLog.AppendWithoutHeader("another " + AppIdentity.Name + " " + AppIdentity.Version + " setup is already running, this one exits");
                    MessageBox.Show("Установка " + AppIdentity.Name + " уже идёт.", AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
                return 0;
            }
        }
    }
}
