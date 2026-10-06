using System.IO;
using Microsoft.Win32;

namespace ClaudeGlow
{
    internal static class UninstallRegistration
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppIdentity.Name;

        public static void Register(string installDirectory, long sizeInBytes)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue("DisplayName", AppIdentity.Name);
                key.SetValue("DisplayVersion", AppIdentity.Version);
                key.SetValue("Publisher", AppIdentity.Name);
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("DisplayIcon", Path.Combine(installDirectory, AppIdentity.ExecutableName) + ",0");
                key.SetValue("UninstallString", "\"" + Path.Combine(installDirectory, AppIdentity.UninstallerName) + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(sizeInBytes / 1024), RegistryValueKind.DWord);
            }
        }

        public static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
        }
    }
}
