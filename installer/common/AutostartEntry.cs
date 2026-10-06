using Microsoft.Win32;

namespace ClaudeGlow
{
    internal static class AutostartEntry
    {
        public static string CurrentPath()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppIdentity.RunKeyPath))
            {
                string value = key == null ? null : key.GetValue(AppIdentity.Name) as string;
                return value == null ? null : value.Trim('"');
            }
        }

        public static void Set(string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppIdentity.RunKeyPath))
            {
                key.SetValue(AppIdentity.Name, "\"" + executablePath + "\"");
            }
        }

        public static void Remove()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppIdentity.RunKeyPath, true))
            {
                if (key != null) key.DeleteValue(AppIdentity.Name, false);
            }
        }
    }
}
