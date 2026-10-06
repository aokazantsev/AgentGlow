using System;
using System.ComponentModel;
using System.Diagnostics;

namespace ClaudeGlow
{
    internal static class RunningApp
    {
        private const int ExitWaitMs = 5000;

        public static string ExecutablePath()
        {
            foreach (Process process in Process.GetProcessesByName(AppIdentity.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        return process.MainModule.FileName;
                    }
                    catch (Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            return null;
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
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1000))
                        {
                            process.Kill();
                            if (!process.WaitForExit(ExitWaitMs)) stoppedAll = false;
                        }
                    }
                    catch (Win32Exception)
                    {
                        stoppedAll = false;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            return stoppedAll;
        }
    }
}
