using System;
using System.IO;

namespace AgentGlow
{
    internal static class InstallTarget
    {
        public static string DefaultDirectory()
        {
            string installed = UninstallRegistration.InstalledLocation();
            if (installed != null && Directory.Exists(installed) && InstallFolder.IsOurs(installed)) return installed;
            return AppIdentity.DefaultInstallDirectory;
        }

        public static string Validate(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return "Укажи папку установки.";
            if (!IsAbsolute(directory.Trim())) return "Укажи полный путь к папке, например " + AppIdentity.DefaultInstallDirectory + ".";
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(directory);
            }
            catch (Exception)
            {
                return "Путь к папке некорректный.";
            }
            if (string.Equals(fullPath.TrimEnd('\\'), Path.GetPathRoot(fullPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return "Нельзя ставить в корень диска — выбери папку, например " + AppIdentity.DefaultInstallDirectory + ".";
            }
            string existing = fullPath.TrimEnd('\\');
            while (existing != null && !Directory.Exists(existing))
            {
                if (File.Exists(existing)) return "В пути есть файл " + existing + ", а не папка — укажи папку установки.";
                existing = Path.GetDirectoryName(existing);
            }
            if (existing == null) return "Диск или сетевая папка " + Path.GetPathRoot(fullPath) + " не найдены или недоступны установщику с правами администратора — выбери локальную папку.";
            if (!Directory.Exists(fullPath)) return null;
            try
            {
                if (Directory.GetFileSystemEntries(fullPath).Length == 0) return null;
                if (InstallFolder.IsMarked(fullPath) || InstallFolder.HoldsOnlyProgramFiles(fullPath) || IsRegistered(fullPath) && InstallFolder.IsOurs(fullPath)) return null;
            }
            catch (IOException error)
            {
                return "Нет доступа к папке: " + error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                return "Нет доступа к папке: " + error.Message;
            }
            return "Папка не пустая и не похожа на папку установки " + AppIdentity.Name + ": установка перезапишет одноимённые файлы. Выбери пустую или новую папку.";
        }

        private static bool IsAbsolute(string path)
        {
            if (path.StartsWith("\\\\")) return true;
            return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        }

        private static bool IsRegistered(string fullPath)
        {
            string installed = UninstallRegistration.InstalledLocation();
            if (installed == null) return false;
            try
            {
                return string.Equals(Path.GetFullPath(installed).TrimEnd('\\'), fullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
