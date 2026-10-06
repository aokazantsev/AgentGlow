using System.IO;
using Microsoft.Win32;

namespace ClaudeGlow
{
    internal static class OpenRgbPresence
    {
        public static bool IsInstalled()
        {
            if (File.Exists(@"C:\Program Files\OpenRGB\OpenRGB.exe")) return true;
            using (RegistryKey service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\OpenRGB"))
            {
                return service != null;
            }
        }
    }
}
