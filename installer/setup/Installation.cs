using System;
using System.Collections.Generic;
using System.IO;

namespace AgentGlow
{
    internal sealed class Installation
    {
        private readonly InstallRequest request;
        private readonly Action<int, string> report;
        private readonly List<string> notes = new List<string>();
        private bool programStopped;

        public Installation(InstallRequest request, Action<int, string> report)
        {
            this.request = request;
            this.report = report;
        }

        public List<string> Notes
        {
            get { return notes; }
        }

        public bool LeftProgramStopped
        {
            get { return programStopped; }
        }

        public void Run()
        {
            string target = request.TargetDirectory;
            string executable = Path.Combine(target, AppIdentity.ExecutableName);
            SetupLog.Start();
            SetupLog.Append("target=" + target + ", autostart=" + request.EnablesAutostart + ", options=" + string.Join(",", new List<string>(request.CheckedOptions).ToArray()));

            if (RunningApp.IsUninstalling()) throw new InvalidOperationException("Сейчас идёт удаление " + AppIdentity.Name + ". Дождись его окончания и повтори установку.");
            report(0, "Останавливаю запущенный " + AppIdentity.Name + "…");
            if (!RunningApp.Stop()) throw new InvalidOperationException(AppIdentity.Name + " не закрывается. Закрой его в трее и повтори установку; если он запущен у другого пользователя Windows — закрой его там.");
            programStopped = true;

            SetupProfile.BeforeExtract(request, report, notes);
            report(10, "Распаковка файлов…");
            string previous = UninstallRegistration.InstalledLocation();
            string token = InstallFolder.Mark(target);
            long size = Payload.Extract(target, report, 10, 75);
            SetupProfile.AfterExtract(request, report, notes);

            report(90, "Запись в «Приложения» Windows…");
            UninstallRegistration.Register(target, size);
            SetupLog.Append("registered in Apps, size=" + size);

            report(94, "Автозапуск…");
            string autostartProblem = request.EnablesAutostart ? Autostart.Enable(executable) : Autostart.Disable();
            if (autostartProblem != null) notes.Add(autostartProblem);
            SetupLog.Append("autostart " + (request.EnablesAutostart ? "enable" : "disable") + ": problem=" + (autostartProblem ?? "none") + ", enabled now=" + Autostart.IsEnabled());

            RemovePreviousLocation(previous, target, token);
            report(97, "Запуск…");
            SetupProfile.Launch(executable);
            programStopped = false;
            SetupLog.Append("launched " + executable);
            SetupProfile.AfterLaunch(request, report, notes);
            report(100, "Готово.");
        }

        private void RemovePreviousLocation(string previous, string target, string token)
        {
            if (previous == null || !Directory.Exists(previous)) return;
            if (InstallFolder.HasMark(previous, token))
            {
                SetupLog.Append("previous location kept: " + previous);
                return;
            }
            if (!InstallFolder.IsOurs(previous))
            {
                SetupLog.Append("previous location kept, no program in it: " + previous);
                if (!IsEmpty(previous) && !Contains(previous, target)) notes.Add("Прежняя папка программы " + previous + " не тронута: в ней нет " + AppIdentity.ExecutableName + ". Если она больше не нужна, удали её вручную, а не запуском Uninstall.exe в ней: он удалит и новую установку.");
                return;
            }
            try
            {
                bool removed = InstallFolder.RemoveProgramFiles(previous, AppIdentity.ExecutableName, AppIdentity.PayloadFiles);
                SetupLog.Append("previous location cleaned: " + previous + ", removed=" + removed);
                if (!removed) notes.Add("Из прежней папки программы " + previous + " убраны файлы " + AppIdentity.Name + ", остальные файлы в ней не тронуты.");
            }
            catch (IOException error)
            {
                notes.Add("Прежняя папка программы " + previous + " удалена не полностью (" + error.Message + ") — удали из неё вручную оставшиеся файлы " + AppIdentity.Name + ", а не запуском Uninstall.exe в ней: он удалит и новую установку.");
            }
            catch (UnauthorizedAccessException error)
            {
                notes.Add("Прежняя папка программы " + previous + " удалена не полностью (" + error.Message + ") — удали из неё вручную оставшиеся файлы " + AppIdentity.Name + ", а не запуском Uninstall.exe в ней: он удалит и новую установку.");
            }
        }

        private static bool Contains(string folder, string path)
        {
            string prefix = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmpty(string directory)
        {
            try
            {
                return Directory.GetFileSystemEntries(directory).Length == 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
