using System.IO;
using Microsoft.Win32;

namespace AgentGlow
{
    internal static class UninstallRegistration
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppIdentity.Name;

        public static string InstalledLocation()
        {
            foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using (RegistryKey key = root.OpenSubKey(KeyPath))
                {
                    string location = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(location)) return location;
                }
            }
            return null;
        }

        public static void Register(string installDirectory, long sizeInBytes)
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath))
            {
                key.SetValue("DisplayName", AppIdentity.Name);
                key.SetValue("DisplayVersion", AppIdentity.Version);
                key.SetValue("Publisher", "aokazantsev");
                key.SetValue("URLInfoAbout", AppIdentity.SiteUrl);
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
            Registry.LocalMachine.DeleteSubKeyTree(KeyPath, false);
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
        }
    }
}
