using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AgentGlow
{
    internal static class SystemTools
    {
        private const uint LoadLibrarySearchSystem32 = 0x800;

        public static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        public static readonly string Schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        public static readonly string Ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        public static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        public static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        public static readonly string PowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        public static void LoadDllsOnlyFromSystem()
        {
            SetDefaultDllDirectories(LoadLibrarySearchSystem32);
        }
    }
}
