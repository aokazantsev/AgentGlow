using System;
using System.Collections.Generic;
using System.IO;

namespace AgentGlow
{
    internal static class InstallFolder
    {
        public const string SetupMutex = AppIdentity.Name + ".Setup";
        public const string UninstallWorkerPrefix = AppIdentity.Name + "Uninstall-";
        private const string MarkerName = AppIdentity.Name + ".install";

        public static bool IsMarked(string directory)
        {
            return File.Exists(Path.Combine(directory, MarkerName));
        }

        public static bool IsOurs(string directory)
        {
            return IsMarked(directory) || File.Exists(Path.Combine(directory, AppIdentity.ExecutableName));
        }

        public static bool HoldsOnlyProgramFiles(string directory)
        {
            var names = new HashSet<string>(ProgramNames(AppIdentity.ExecutableName, AppIdentity.PayloadFiles), StringComparer.OrdinalIgnoreCase);
            string programPrefix = Path.GetFileNameWithoutExtension(AppIdentity.ExecutableName) + ".";
            bool hasProgramFile = false;
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                string name = Path.GetFileName(entry);
                if (!names.Contains(name)) return false;
                if (name.StartsWith(programPrefix, StringComparison.OrdinalIgnoreCase)) hasProgramFile = true;
            }
            return hasProgramFile;
        }

        public static string Mark(string directory)
        {
            Directory.CreateDirectory(directory);
            string token = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(directory, MarkerName), AppIdentity.Name + " " + AppIdentity.Version + " " + token);
            return token;
        }

        public static bool HasMark(string directory, string token)
        {
            string marker = Path.Combine(directory, MarkerName);
            try
            {
                return File.Exists(marker) && File.ReadAllText(marker).Contains(token);
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public static bool RemoveProgramFiles(string directory, string executable, IEnumerable<string> files)
        {
            foreach (string name in ProgramNames(executable, files))
            {
                string path = Path.Combine(directory, name);
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            if (Directory.GetFileSystemEntries(directory).Length > 0) return false;
            Directory.Delete(directory);
            return true;
        }

        private static List<string> ProgramNames(string executable, IEnumerable<string> files)
        {
            var names = new List<string>(files);
            names.Add(MarkerName);
            names.Add(Path.GetFileNameWithoutExtension(executable) + ".old.exe");
            return names;
        }
    }
}
