using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AgentGlow
{
    internal static class RunningApp
    {
        private const int ExitWaitMs = 5000;

        public static bool IsUninstalling()
        {
            string installed = UninstallRegistration.InstalledLocation();
            int self = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == self) continue;
                    if (process.ProcessName.StartsWith(InstallFolder.UninstallWorkerPrefix, StringComparison.OrdinalIgnoreCase)) return true;
                    if (installed == null || !string.Equals(process.ProcessName, "Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = PathOf(process);
                    if (path != null && string.Equals(Path.GetDirectoryName(path).TrimEnd('\\'), installed.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        public static bool Stop()
        {
            bool stoppedAll = true;
            foreach (Process process in Process.GetProcessesByName(AppIdentity.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        if (!process.WaitForExit(ExitWaitMs)) stoppedAll = false;
                    }
                    catch (Win32Exception)
                    {
                        if (!process.WaitForExit(ExitWaitMs)) stoppedAll = false;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            return stoppedAll;
        }

        private static string PathOf(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }
    }
}
