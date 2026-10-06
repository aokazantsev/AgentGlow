using System;
using System.Collections.Generic;
using System.IO;

namespace ClaudeGlow
{
    internal sealed class Installation
    {
        private readonly InstallRequest request;
        private readonly Action<int, string> report;
        private readonly List<string> notes = new List<string>();

        public Installation(InstallRequest request, Action<int, string> report)
        {
            this.request = request;
            this.report = report;
        }

        public List<string> Notes
        {
            get { return notes; }
        }

        public void Run()
        {
            string target = request.TargetDirectory;
            string executable = Path.Combine(target, AppIdentity.ExecutableName);

            if (RunningApp.IsUninstalling()) throw new InvalidOperationException("Сейчас идёт удаление " + AppIdentity.Name + ". Дождись его окончания и повтори установку.");
            report(0, "Останавливаю запущенный " + AppIdentity.Name + "…");
            if (!RunningApp.Stop()) throw new InvalidOperationException(AppIdentity.Name + " не закрывается. Закрой его в трее и повтори установку.");

            SetupProfile.BeforeExtract(request, report, notes);
            report(10, "Распаковка файлов…");
            long size = Payload.Extract(target, report, 10, 75);
            SetupProfile.AfterExtract(request, report, notes);

            report(90, "Запись в «Приложения» Windows…");
            UninstallRegistration.Register(target, size);

            report(94, "Автозапуск…");
            string autostartProblem = request.EnablesAutostart ? Autostart.Enable(executable) : Autostart.Disable();
            if (autostartProblem != null) notes.Add(autostartProblem);

            report(97, "Запуск…");
            SetupProfile.Launch(executable);
            report(100, "Готово.");
        }
    }
}
