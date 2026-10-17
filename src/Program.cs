using System;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;

namespace AgentGlow
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            if (args.Length > 0 && args[0] == OpenRgbService.RestartArgument)
            {
                UserDataPaths.Ensure();
                return OpenRgbService.RestartNow();
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            bool isFirstInstance;
            AppSettings settings;
            using (new Mutex(true, AppIdentity.SingleInstanceMutex, out isFirstInstance))
            {
                if (!isFirstInstance) return 0;
                UserDataPaths.Ensure();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppLog.Append("start " + AppIdentity.Version + ", user=" + Environment.UserDomainName + "\\" + Environment.UserName
                    + ", os=" + Environment.OSVersion.VersionString + ", exe=" + Application.ExecutablePath);
                settings = ReadSettings();
                if (settings.ReadError == null) Application.Run(new TrayApplication(settings));
            }
            if (settings.ReadError == null) return 0;
            MessageBox.Show("Не удалось прочитать настройки " + AppSettings.FilePath + ": " + settings.ReadError + Environment.NewLine + Environment.NewLine
                + settings.ReadAdvice + " Программа закрыта, после этого запусти " + AppIdentity.Name + " снова.",
                AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        private static AppSettings ReadSettings()
        {
            AppSettings settings = AppSettings.LoadPatiently(AppLog.Append);
            if (settings.ReadError != null) AppLog.Append("settings not read, exit: " + settings.ReadError);
            return settings;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            CrashReport.Write("ui thread", e.Exception, false);
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            CrashReport.Write("unhandled", e.ExceptionObject as Exception, e.IsTerminating);
        }
    }
}
