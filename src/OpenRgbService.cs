using System;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

namespace ClaudeGlow
{
    internal static class OpenRgbService
    {
        private const string ServiceName = "OpenRGB";
        private const int RestartTimeoutMs = 60 * 1000;
        private const int CancelledByUser = 1223;

        private const string RestartScript =
            "$ErrorActionPreference = 'Stop'\n"
            + "$name = '" + ServiceName + "'\n"
            + "Stop-Service -Name $name -Force -NoWait -ErrorAction SilentlyContinue\n"
            + "$service = Get-Service -Name $name\n"
            + "try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10)) }\n"
            + "catch {\n"
            + "  $processId = (Get-CimInstance Win32_Service -Filter \"Name='$name'\").ProcessId\n"
            + "  if ($processId) { Stop-Process -Id $processId -Force }\n"
            + "  $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))\n"
            + "}\n"
            + "Start-Service -Name $name\n";

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
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(RestartScript));
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
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
