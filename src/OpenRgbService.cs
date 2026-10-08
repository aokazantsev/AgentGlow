using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace AgentGlow
{
    internal static class OpenRgbService
    {
        public const string RestartTaskName = "AgentGlow OpenRGB restart";
        public const string RestartArgument = "/restart-openrgb";

        private const string ServiceName = "OpenRGB";
        private const int CommandTimeoutMs = 15 * 1000;
        private const int RestartTimeoutMs = 60 * 1000;
        private const int StatusPollMs = 1000;
        private const int CancelledByUser = 1223;
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

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

        public static int RestartNow()
        {
            try
            {
                using (var service = new ServiceController(ServiceName))
                {
                    AppLog.Append("openrgb restart: service " + service.Status);
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        if (service.Status != ServiceControllerStatus.StopPending) RequestStop(service);
                        try
                        {
                            service.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
                        }
                        catch (System.ServiceProcess.TimeoutException)
                        {
                            KillServiceProcess();
                            service.WaitForStatus(ServiceControllerStatus.Stopped, KillTimeout);
                        }
                    }
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
                    AppLog.Append("openrgb restart: service running");
                    return 0;
                }
            }
            catch (Exception error)
            {
                AppLog.Append("openrgb restart failed: " + error.GetType().Name + ": " + error.Message);
                return 1;
            }
        }

        public static bool HasRestartTask()
        {
            return RunSchtasks("/Query /TN \"" + RestartTaskName + "\"") == 0;
        }

        public static string InstallRestartTask(string executablePath)
        {
            string definitionPath = Path.Combine(Path.GetTempPath(), "AgentGlow-openrgb-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(definitionPath, TaskDefinition(executablePath), Encoding.Unicode);
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

        public static string RestartElevated(string executablePath)
        {
            var start = new ProcessStartInfo(executablePath, RestartArgument) { UseShellExecute = true, Verb = "runas" };
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(RestartTimeoutMs)) return "служба не перезапустилась за " + RestartTimeoutMs / 1000 + " с";
                    if (process.ExitCode != 0) return "перезапуск не удался, подробности в журнале";
                }
            }
            catch (Win32Exception error)
            {
                return error.NativeErrorCode == CancelledByUser ? "окно UAC отклонено" : error.Message;
            }
            return WaitUntilRunning();
        }

        private static void RequestStop(ServiceController service)
        {
            try
            {
                service.Stop();
            }
            catch (InvalidOperationException error)
            {
                AppLog.Append("openrgb restart: stop request failed: " + error.Message);
            }
        }

        private static void KillServiceProcess()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Service WHERE Name = '" + ServiceName + "'"))
            using (ManagementObjectCollection services = searcher.Get())
            {
                foreach (ManagementObject service in services)
                {
                    using (service)
                    {
                        int processId = Convert.ToInt32(service["ProcessId"]);
                        if (processId == 0) continue;
                        AppLog.Append("openrgb restart: service did not stop, killing process " + processId);
                        using (Process process = Process.GetProcessById(processId))
                        {
                            process.Kill();
                        }
                    }
                }
            }
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

        private static string TaskDefinition(string executablePath)
        {
            string user;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                user = SecurityElement.Escape(identity.Name);
            }
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "  <RegistrationInfo><Description>AgentGlow: перезапуск зависшей службы OpenRGB по запросу AgentGlow</Description></RegistrationInfo>\n"
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
                + "    <Exec><Command>" + SecurityElement.Escape(executablePath) + "</Command><Arguments>" + RestartArgument + "</Arguments></Exec>\n"
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
