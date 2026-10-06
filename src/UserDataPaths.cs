using System;
using System.IO;

namespace ClaudeGlow
{
    internal static class UserDataPaths
    {
        public static readonly string Directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeGlow");

        public static string File(string name)
        {
            return Path.Combine(Directory, name);
        }

        public static void Ensure()
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
    }
}
