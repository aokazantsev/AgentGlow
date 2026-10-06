using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace ClaudeGlow
{
    internal static class OpenRgbService
    {
        public const string RestartTaskName = "ClaudeGlow OpenRGB restart";

        private const string ServiceName = "OpenRGB";
        private const int CommandTimeoutMs = 15 * 1000;
        private const int RestartTimeoutMs = 60 * 1000;
        private const int StatusPollMs = 1000;
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
            return Status() != null;
        }

        public static ServiceControllerStatus? Status()
        {
            try
            {
                using (var service = new ServiceController(ServiceName))
                {
                    return service.Status;
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }

        public static bool HasRestartTask()
        {
            return RunSchtasks("/Query /TN \"" + RestartTaskName + "\"") == 0;
        }

        public static string InstallRestartTask()
        {
            string definitionPath = Path.Combine(Path.GetTempPath(), "ClaudeGlow-openrgb-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(definitionPath, TaskDefinition(), Encoding.Unicode);
                int code = RunSchtasks("/Create /TN \"" + RestartTaskName + "\" /XML \"" + definitionPath + "\" /F");
                return code == 0 ? null : "schtasks вернул код " + code;
            }
            finally
            {
                File.Delete(definitionPath);
            }
        }

        public static string RemoveRestartTask()
        {
            if (!HasRestartTask()) return null;
            int code = RunSchtasks("/Delete /TN \"" + RestartTaskName + "\" /F");
            return code == 0 ? null : "schtasks вернул код " + code;
        }

        public static string RestartWithTask()
        {
            int code = RunSchtasks("/Run /TN \"" + RestartTaskName + "\"");
            if (code != 0) return "задача перезапуска не запустилась, schtasks вернул код " + code;
            return WaitUntilRunning();
        }

        public static string RestartElevated()
        {
            var start = new ProcessStartInfo("powershell.exe", PowerShellArguments())
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
                    if (process.ExitCode != 0) return "PowerShell завершился с кодом " + process.ExitCode;
                }
            }
            catch (Win32Exception error)
            {
                return error.NativeErrorCode == CancelledByUser ? "окно UAC отклонено" : error.Message;
            }
            return WaitUntilRunning();
        }

        private static string WaitUntilRunning()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(RestartTimeoutMs);
            ServiceControllerStatus? status = Status();
            while (status != ServiceControllerStatus.Running && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(StatusPollMs);
                status = Status();
            }
            return status == ServiceControllerStatus.Running ? null : "служба не поднялась за " + RestartTimeoutMs / 1000 + " с, состояние " + status;
        }

        private static string PowerShellArguments()
        {
            return "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(RestartScript));
        }

        private static string TaskDefinition()
        {
            string user;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                user = SecurityElement.Escape(identity.Name);
            }
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "  <RegistrationInfo><Description>ClaudeGlow: перезапуск зависшей службы OpenRGB по запросу ClaudeGlow</Description></RegistrationInfo>\n"
                + "  <Triggers />\n"
                + "  <Principals>\n"
                + "    <Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>\n"
                + "  </Principals>\n"
                + "  <Settings>\n"
                + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n"
                + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
                + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n"
                + "    <AllowStartOnDemand>true</AllowStartOnDemand>\n"
                + "    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>\n"
                + "    <Hidden>true</Hidden>\n"
                + "    <Enabled>true</Enabled>\n"
                + "  </Settings>\n"
                + "  <Actions Context=\"Author\">\n"
                + "    <Exec><Command>powershell.exe</Command><Arguments>" + SecurityElement.Escape(PowerShellArguments()) + "</Arguments></Exec>\n"
                + "  </Actions>\n"
                + "</Task>\n";
        }

        private static int RunSchtasks(string arguments)
        {
            var start = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (Process process = Process.Start(start))
                {
                    return process.WaitForExit(CommandTimeoutMs) ? process.ExitCode : -1;
                }
            }
            catch (Win32Exception)
            {
                return -1;
            }
        }
    }
}
