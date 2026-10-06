using System;
using System.IO;

namespace ClaudeGlow
{
    internal static class AppIdentity
    {
        public const string Name = "ClaudeGlow";
        public const string Version = "1.5";
        public const string ExecutableName = "ClaudeGlow.exe";
        public const string ProcessName = "ClaudeGlow";
        public const string UninstallerName = "Uninstall.exe";
        public const string SingleInstanceMutex = "ClaudeGlow.SingleInstance";
        public const string GitHubUrl = "https://github.com/aokazantsev/ClaudeGlow";
        public const string SiteUrl = "https://aokazantsev.ru/pets/claudeglow/";

        public static string DefaultInstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name); }
        }

        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name); }
        }
    }
}
