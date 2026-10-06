using System;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeGlow
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ClaudeGlow";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static bool TrySetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (SecurityException)
            {
                return false;
            }
        }
    }
}
