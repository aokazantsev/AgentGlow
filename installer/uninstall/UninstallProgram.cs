using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeGlow
{
    internal static class UninstallProgram
    {
        private const string RemoveCommand = "/remove";
        private const int DeleteAttempts = 20;
        private const int DeleteRetryMs = 500;
        private const string Title = "Удаление " + AppIdentity.Name;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            if (args.Length >= 2 && args[0] == RemoveCommand) return Remove(args[1]);
            return Confirm(Path.GetDirectoryName(Application.ExecutablePath));
        }

        private static int Confirm(string installDirectory)
        {
            DialogResult answer = MessageBox.Show(
                "Удалить " + AppIdentity.Name + "?" + Environment.NewLine + Environment.NewLine
                + "Удалятся программа, настройки, журнал, автозапуск, запись в «Приложениях» и хуки ClaudeGlow из "
                + ClaudeHooks.SettingsPath + " (остальные хуки и настройки Claude Code не трогаются).",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return 1;
            string worker = Path.Combine(Path.GetTempPath(), AppIdentity.Name + "Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, worker, true);
            Process.Start(new ProcessStartInfo(worker, RemoveCommand + " \"" + installDirectory + "\"") { UseShellExecute = false });
            return 0;
        }

        private static int Remove(string installDirectory)
        {
            var problems = new List<string>();
            if (!RunningApp.Stop()) problems.Add(AppIdentity.Name + " не закрылся");
            try
            {
                ClaudeHooks.Uninstall();
            }
            catch (Exception error)
            {
                problems.Add("хуки в " + ClaudeHooks.SettingsPath + " не сняты: " + error.Message);
            }
            AutostartEntry.Remove();
            UninstallRegistration.Unregister();
            string dataProblem = DeleteWithRetries(UserDataPaths.Directory);
            if (dataProblem != null) problems.Add("папка настроек " + dataProblem);
            if (File.Exists(Path.Combine(installDirectory, AppIdentity.ExecutableName)))
            {
                string folderProblem = DeleteWithRetries(installDirectory);
                if (folderProblem != null) problems.Add("папка программы " + folderProblem);
            }
            if (problems.Count == 0)
            {
                MessageBox.Show(AppIdentity.Name + " удалён.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Удаление закончено не полностью:" + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", problems),
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ScheduleSelfDelete();
            return problems.Count == 0 ? 0 : 2;
        }

        private static string DeleteWithRetries(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, true);
                    return null;
                }
                catch (IOException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                }
                catch (UnauthorizedAccessException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                }
                Thread.Sleep(DeleteRetryMs);
            }
        }

        private static void ScheduleSelfDelete()
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Application.ExecutablePath + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
