using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgentGlow
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
            SystemTools.LoadDllsOnlyFromSystem();
            Application.EnableVisualStyles();
            if (args.Length >= 2 && args[0] == RemoveCommand) return Remove(args[1]);
            return Confirm(Path.GetDirectoryName(Application.ExecutablePath));
        }

        private static int Confirm(string installDirectory)
        {
            if (SetupRunning())
            {
                MessageBox.Show("Открыт установщик " + AppIdentity.Name + ". Закрой его или дождись окончания установки и повтори удаление.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }
            if (RunningApp.IsUninstalling())
            {
                MessageBox.Show("Удаление " + AppIdentity.Name + " уже идёт. Дождись сообщения о результате.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }
            DialogResult answer = MessageBox.Show(
                "Удалить " + AppIdentity.Name + "?" + Environment.NewLine + Environment.NewLine
                + "Удалятся программа, её настройки и данные, автозапуск и запись в «Приложениях». "
                + UninstallProfile.ConfirmDetails,
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return 1;
            try
            {
                string worker = Path.Combine(Path.GetTempPath(), InstallFolder.UninstallWorkerPrefix + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, worker, true);
                Process.Start(new ProcessStartInfo(worker, RemoveCommand + " \"" + installDirectory + "\"")
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.SystemDirectory
                });
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("Удаление не запустилось: " + error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        private static int Remove(string installDirectory)
        {
            Directory.SetCurrentDirectory(Environment.SystemDirectory);
            var problems = new List<string>();
            if (!RunningApp.Stop()) problems.Add(AppIdentity.Name + " не закрылся");
            UninstallProfile.Remove(problems);
            string autostartProblem = Autostart.Disable();
            if (autostartProblem != null) problems.Add(autostartProblem);
            UninstallRegistration.Unregister();
            string dataProblem = DeleteWithRetries(AppIdentity.DataDirectory);
            if (dataProblem != null) problems.Add("папка данных " + dataProblem);
            string folderProblem = CleanInstallDirectory(installDirectory);
            if (folderProblem != null) problems.Add("папка программы " + folderProblem);
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

        private static string CleanInstallDirectory(string directory)
        {
            if (!Directory.Exists(directory) || !InstallFolder.IsMarked(directory)) return null;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (InstallFolder.RemoveProgramFiles(directory, AppIdentity.ExecutableName, AppIdentity.PayloadFiles)) return null;
                    return "не удалена: в ней остались чужие файлы, файлы " + AppIdentity.Name + " убраны";
                }
                catch (DirectoryNotFoundException)
                {
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
                catch (DirectoryNotFoundException)
                {
                    return null;
                }
                catch (IOException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                    ClearReadOnly(directory);
                }
                catch (UnauthorizedAccessException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                    ClearReadOnly(directory);
                }
                Thread.Sleep(DeleteRetryMs);
            }
        }

        private static void ClearReadOnly(string directory)
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                ClearReadOnlyAttribute(directory);
                foreach (string file in Directory.GetFiles(directory)) ClearReadOnlyAttribute(file);
                foreach (string child in Directory.GetDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) ClearReadOnly(child);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ClearReadOnlyAttribute(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return;
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ScheduleSelfDelete()
        {
            if (!Path.GetFileName(Application.ExecutablePath).StartsWith(InstallFolder.UninstallWorkerPrefix, StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(SystemTools.Cmd, "/c " + SystemTools.Ping + " -n 3 127.0.0.1 >nul & del /f /q \"" + Application.ExecutablePath + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.SystemDirectory
            });
        }

        private static bool SetupRunning()
        {
            Mutex setup;
            if (!Mutex.TryOpenExisting(InstallFolder.SetupMutex, out setup)) return false;
            setup.Dispose();
            return true;
        }
    }
}
