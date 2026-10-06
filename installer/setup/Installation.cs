using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ClaudeGlow
{
    internal sealed class Installation
    {
        public const string PayloadResource = "ClaudeGlow.Payload.zip";
        private const int DefaultHookPort = 47651;
        private static readonly string[] MigratedFiles = { "settings.txt", "sessions.txt" };

        private readonly bool installHooks;
        private readonly bool enableAutostart;
        private readonly Action<int, string> report;
        private readonly List<string> notes = new List<string>();

        public Installation(bool installHooks, bool enableAutostart, Action<int, string> report)
        {
            this.installHooks = installHooks;
            this.enableAutostart = enableAutostart;
            this.report = report;
        }

        public List<string> Notes
        {
            get { return notes; }
        }

        public void Run()
        {
            string target = AppIdentity.DefaultInstallDirectory;
            string previousExecutable = RunningApp.ExecutablePath() ?? AutostartEntry.CurrentPath();

            report(0, "Останавливаю запущенный " + AppIdentity.Name + "…");
            if (!RunningApp.Stop()) throw new InvalidOperationException(AppIdentity.Name + " не закрывается. Закрой его в трее и повтори установку.");

            report(5, "Перенос настроек прежней версии…");
            MigrateFrom(previousExecutable, target);

            report(10, "Распаковка файлов…");
            long size = ExtractPayload(target);
            string executable = Path.Combine(target, AppIdentity.ExecutableName);

            report(85, "Запись в «Приложения» Windows…");
            UninstallRegistration.Register(target, size);

            if (enableAutostart) AutostartEntry.Set(executable);
            else AutostartEntry.Remove();

            if (installHooks)
            {
                report(92, "Хуки Claude Code…");
                int port = ConfiguredHookPort();
                if (ClaudeHooks.AreInstalled(port))
                {
                    notes.Add("Хуки Claude Code уже были прописаны — " + ClaudeHooks.SettingsPath + " не тронут.");
                }
                else
                {
                    ClaudeHooks.Install(port);
                    notes.Add("Хуки прописаны в " + ClaudeHooks.SettingsPath + " (копия прежнего файла — рядом, .claudeglow.bak). Новые сессии Claude Code начнут слать события; уже открытые — после перезапуска.");
                }
            }

            report(97, "Запуск…");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { WorkingDirectory = target, UseShellExecute = false });
            report(100, "Готово.");
        }

        private static int ConfiguredHookPort()
        {
            string path = UserDataPaths.File("settings.txt");
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int port;
                    if (line.StartsWith("hookPort=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(9).Trim(), out port)) return port;
                }
            }
            return DefaultHookPort;
        }

        private void MigrateFrom(string previousExecutable, string target)
        {
            if (string.IsNullOrEmpty(previousExecutable)) return;
            string previousDirectory = Path.GetDirectoryName(previousExecutable);
            if (string.IsNullOrEmpty(previousDirectory) || !Directory.Exists(previousDirectory)) return;
            if (string.Equals(Path.GetFullPath(previousDirectory).TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            UserDataPaths.Ensure();
            bool moved = false;
            foreach (string name in MigratedFiles)
            {
                string source = Path.Combine(previousDirectory, name);
                string destination = UserDataPaths.File(name);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                File.Copy(source, destination);
                moved = true;
            }
            if (moved) notes.Add("Настройки перенесены из " + previousDirectory + ". Прежнюю папку можно удалить вручную.");
        }

        private long ExtractPayload(string target)
        {
            Directory.CreateDirectory(target);
            string root = target.TrimEnd('\\') + "\\";
            long totalBytes = 0;
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (payload == null) throw new InvalidOperationException("В установщике нет архива программы — он собран неправильно.");
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    int index = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        index++;
                        string relative = entry.FullName.Replace('/', '\\');
                        string destination = Path.GetFullPath(Path.Combine(target, relative));
                        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                        totalBytes += entry.Length;
                        report(10 + index * 70 / archive.Entries.Count, "Распаковка: " + relative);
                    }
                }
            }
            return totalBytes;
        }
    }
}
