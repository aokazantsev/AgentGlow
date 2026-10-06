using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ClaudeGlow
{
    internal static class SetupProfile
    {
        private const string HooksKey = "hooks";
        private const int DefaultHookPort = 47651;
        private const string OpenRgbPath = @"C:\Program Files\OpenRGB\OpenRGB.exe";

        public const string Intro =
            "Подсветка RGB-устройств показывает, что делает Claude Code: работает, закончил, ждёт ответа или "
            + "разрешения. Глянул на подсветку — понял, нужно ли разворачивать окно.";

        public static string Notice()
        {
            if (IsOpenRgbInstalled()) return null;
            return "OpenRGB не найден. ClaudeGlow управляет подсветкой через него — поставь OpenRGB с https://openrgb.org "
                + "и включи в нём SDK-сервер.";
        }

        public static List<SetupField> Fields()
        {
            return new List<SetupField>();
        }

        public static List<SetupOption> Options()
        {
            return new List<SetupOption>
            {
                new SetupOption
                {
                    Key = HooksKey,
                    Text = "Прописать хуки в настройки Claude Code",
                    Hint = "В ~\\.claude\\settings.json добавляются только хуки ClaudeGlow, копия прежнего файла остаётся рядом.",
                    Checked = true
                },
                new SetupOption
                {
                    Key = CrashReportConsent.OptionKey,
                    Text = "Отправлять автору отчёты о сбоях",
                    Hint = "В отчёт попадает журнал программы, а в нём — путь к настройкам Claude Code с именем пользователя Windows "
                        + "и названия RGB-устройств. Не включай, если это запрещают правила твоей компании.",
                    DetailsTitle = "Что уходит в отчёте",
                    Details = "Отчёт уходит один раз — при падении программы — на aokazantsev.ru (сервер в России), "
                        + "без повторных попыток:\n"
                        + "• версия программы, Windows и .NET;\n"
                        + "• текст ошибки;\n"
                        + "• последние 100 КБ журнала %LOCALAPPDATA%\\ClaudeGlow\\log.txt: названия событий и инструментов Claude Code, "
                        + "короткие номера сессий и процессов, путь к настройкам Claude Code, названия RGB-устройств;\n"
                        + "• IP-адрес, с которого пришёл отчёт.\n"
                        + "Тексты запросов, ответы и код Claude в журнал не попадают. Отчёты видит только автор, хранятся последние 50 МБ. "
                        + "Изменить выбор — переустановить программу.",
                    Checked = CrashReportConsent.IsGiven
                }
            };
        }

        public static void BeforeExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            CrashReportConsent.Set(request.Has(CrashReportConsent.OptionKey));
            SetupLog.Append("crash reports: " + request.Has(CrashReportConsent.OptionKey));
        }

        public static void AfterExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            RemoveLegacyInstall(request.TargetDirectory, notes);
            if (!request.Has(HooksKey)) return;
            report(85, "Хуки Claude Code…");
            int port = ConfiguredHookPort();
            if (ClaudeHooks.AreInstalled(port))
            {
                notes.Add("Хуки Claude Code уже были прописаны — " + ClaudeHooks.SettingsPath + " не тронут.");
                return;
            }
            ClaudeHooks.Install(port);
            notes.Add("Хуки прописаны в " + ClaudeHooks.SettingsPath + ". Уже открытые сессии Claude Code подхватят их после перезапуска.");
        }

        public static void Launch(string executable)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + executable + "\"") { UseShellExecute = false });
        }

        private static void RemoveLegacyInstall(string target, List<string> notes)
        {
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\" + AppIdentity.Name);
            if (!Directory.Exists(legacy)) return;
            if (string.Equals(Path.GetFullPath(legacy).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(Path.Combine(legacy, AppIdentity.ExecutableName))) return;
            try
            {
                Directory.Delete(legacy, true);
                notes.Add("Прежняя установка из " + legacy + " удалена.");
            }
            catch (IOException)
            {
                notes.Add("Прежнюю папку " + legacy + " удалить не получилось — удали её вручную.");
            }
            catch (UnauthorizedAccessException)
            {
                notes.Add("Прежнюю папку " + legacy + " удалить не получилось — удали её вручную.");
            }
        }

        private static int ConfiguredHookPort()
        {
            string path = Path.Combine(AppIdentity.DataDirectory, "settings.txt");
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

        private static bool IsOpenRgbInstalled()
        {
            if (File.Exists(OpenRgbPath)) return true;
            using (RegistryKey service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\OpenRGB"))
            {
                return service != null;
            }
        }
    }
}
