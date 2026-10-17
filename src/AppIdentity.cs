using System;
using System.IO;

namespace AgentGlow
{
    internal static class AppIdentity
    {
        public const string Name = "AgentGlow";
        public const string Version = "3.2";
        public const string UpdateSetupArguments = "";
        public const string UpdateKeeps = "настройки сохранятся";
        public const string ExecutableName = "AgentGlow.exe";
        public const string ProcessName = "AgentGlow";
        public const string UninstallerName = "Uninstall.exe";
        public const string SingleInstanceMutex = "AgentGlow.SingleInstance";
        public const string GitHubUrl = "https://github.com/aokazantsev/AgentGlow";
        public const string SiteUrl = "https://aokazantsev.ru/pets/agentglow/";
        public static readonly string[] PayloadFiles = { ExecutableName, ExecutableName + ".config", UninstallerName, "README.md", "LICENSE", "CHANGELOG.md" };

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
