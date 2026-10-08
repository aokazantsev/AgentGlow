using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AgentGlow
{
    internal static class OpenRgbLauncher
    {
        private const string ProcessName = "OpenRGB";
        private const string Arguments = "--startminimized --server";

        public static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName(ProcessName);
            foreach (Process process in processes)
            {
                process.Dispose();
            }
            return processes.Length > 0;
        }

        public static bool TryStart(string path)
        {
            if (IsRunning() || !File.Exists(path)) return false;
            var startInfo = new ProcessStartInfo(path, Arguments);
            startInfo.UseShellExecute = true;
            startInfo.WorkingDirectory = Path.GetDirectoryName(path);
            try
            {
                using (Process.Start(startInfo))
                {
                }
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
