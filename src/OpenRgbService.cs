using System;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace ClaudeGlow
{
    internal static class OpenRgbService
    {
        private const string ServiceName = "OpenRGB";
        private const int RestartTimeoutMs = 60 * 1000;
        private const int CancelledByUser = 1223;

        public static bool IsInstalled()
        {
            try
            {
                using (var service = new ServiceController(ServiceName))
                {
                    return service.Status != 0;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        public static string RestartElevated()
        {
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Restart-Service -Name " + ServiceName + " -Force\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(RestartTimeoutMs)) return "служба не перезапустилась за " + RestartTimeoutMs / 1000 + " с";
                    return process.ExitCode == 0 ? null : "PowerShell завершился с кодом " + process.ExitCode;
                }
            }
            catch (Win32Exception error)
            {
                return error.NativeErrorCode == CancelledByUser ? "окно UAC отклонено" : error.Message;
            }
        }
    }
}
