using System;
using System.IO;

namespace ClaudeGlow
{
    internal static class AppIdentity
    {
        public const string Name = "ClaudeGlow";
        public const string Version = "1.0";
        public const string ExecutableName = "ClaudeGlow.exe";
        public const string ProcessName = "ClaudeGlow";
        public const string UninstallerName = "Uninstall.exe";
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultInstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\" + Name); }
        }
    }
}
