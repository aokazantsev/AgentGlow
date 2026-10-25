using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AgentGlow
{
    internal static class SetupProfile
    {
        private const string SourceKeyPrefix = "source.";
        private const string ResetSettingsKey = "resetSettings";
        private const string OpenRgbPath = @"C:\Program Files\OpenRGB\OpenRGB.exe";

        private static AppSettings pendingSettings;

        public const string Intro =
            "Подсветка RGB-устройств показывает, что делают ИИ-агенты: работают, закончили, ждут ответа или "
            + "разрешения. Глянул на подсветку — понял, нужно ли разворачивать окно.";

        public static string Notice()
        {
            if (IsOpenRgbInstalled()) return null;
            return "OpenRGB не найден. AgentGlow управляет подсветкой через него — поставь OpenRGB с https://openrgb.org "
                + "и включи в нём SDK-сервер.";
        }

        public static List<SetupField> Fields()
        {
            return new List<SetupField>();
        }

        public static List<SetupOption> Options()
        {
            var options = new List<SetupOption>
            {
                new SetupOption
                {
                    Key = ResetSettingsKey,
                    Text = "Сбросить настройки",
                    Hint = "Эффекты и цвета статусов, устройства, таймауты, порт событий и автозапуск OpenRGB "
                        + "вернутся к стандартным. Статусы тредов и журнал сохранятся. Помогает, если сбой вызван настройками.",
                    Checked = false
                }
            };
            AppSettings current = AppSettings.FileExists ? AppSettings.Load() : null;
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null) continue;
                options.Add(new SetupOption
                {
                    Key = SourceKeyPrefix + entry.Id,
                    Text = entry.Integration.OptionText,
                    Hint = entry.Integration.OptionHint,
                    Checked = current == null || current.ReadError != null || current.IsSourceEnabled(entry.Id)
                });
            }
            options.Add(new SetupOption
            {
                Key = CrashReportConsent.OptionKey,
                Text = "Отправлять автору отчёты о сбоях",
                Hint = "В отчёт попадает журнал программы, а в нём — имя пользователя и домена Windows, пути к программе и "
                    + "настройкам Claude Code и названия RGB-устройств. Не включай, если это запрещают правила твоей компании.",
                DetailsTitle = "Что уходит в отчёте",
                Details = "Отчёт уходит один раз — при падении программы — на aokazantsev.ru (сервер в России), "
                    + "без повторных попыток:\n"
                    + "• версия программы, Windows и .NET;\n"
                    + "• время и текст ошибки;\n"
                    + "• последние 100 КБ журнала %LOCALAPPDATA%\\AgentGlow\\log.txt: имя пользователя и домена Windows, названия событий и инструментов агентов, "
                    + "короткие номера сессий и процессов, названия приложений и статусы, присланные на /status, пути к программе, "
                    + "её данным, настройкам Claude Code и скачанному обновлению, тексты ошибок программы, названия RGB-устройств;\n"
                    + "• IP-адрес, с которого пришёл отчёт.\n"
                    + "Тексты запросов, ответы и код агентов в журнал не попадают. Отчёты видит только автор, хранятся последние 50 МБ. "
                    + "Изменить выбор — переустановить программу.",
                Checked = CrashReportConsent.IsGiven
            });
            options.Add(new SetupOption
            {
                Key = UsageConsent.OptionKey,
                Text = "Отправлять автору анонимную статистику использования",
                Hint = "Помогает понять, сколько людей пользуется программой и какие функции нужны. Без имён, путей и адресов. "
                    + "Выключить можно и потом — в настройках программы.",
                DetailsTitle = "Что уходит в статистике",
                Details = "Раз в час, пока программа запущена, на aokazantsev.ru (сервер в России) уходят счётчики за текущие и неотправленные прошлые сутки (до недели):\n"
                    + "• случайный номер установки — он не связан с тобой и компьютером;\n"
                    + "• версия программы и Windows;\n"
                    + "• сколько раз за сутки программа запускалась, сама находила новую версию и сама перезапускала OpenRGB, "
                    + "сколько раз ты пользовался пунктами меню, настройками, уведомлениями и обновлением;\n"
                    + "• сколько событий за сутки пришло от Claude Code и от других приложений — только число, без содержимого;\n"
                    + "• текущие настройки: какие источники включены, автозапуск, проверка обновлений, отчёты о сбоях, пауза, сколько RGB-устройств видно и сколько держат "
                    + "постоянный свет, эффект статуса «Нет активных сессий», менялись ли эффекты, стоит ли OpenRGB службой.\n"
                    + "Имена пользователя и компьютера, пути, названия проектов, сессий и устройств, тексты агентов и адреса в статистику "
                    + "не попадают, IP-адрес сервер не хранит. Статистика хранится 400 суток, дальше — только итоги по дням.",
                Checked = UsageConsent.InstallerDefault
            });
            return options;
        }

        public static void BeforeExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            bool reset = request.Has(ResetSettingsKey);
            SetupLog.Append("reset settings: " + reset);
            if (!reset) report(5, "Читаю настройки…");
            AppSettings settings = reset ? new AppSettings() : AppSettings.LoadPatiently(SetupLog.Append);
            if (settings.ReadError != null) throw new InvalidOperationException("Не удалось прочитать настройки " + AppSettings.FilePath + ": " + settings.ReadError.TrimEnd('.') + ". " + settings.ReadAdvice);
            pendingSettings = settings;
            CrashReportConsent.Set(request.Has(CrashReportConsent.OptionKey));
            SetupLog.Append("crash reports: " + request.Has(CrashReportConsent.OptionKey));
            UsageConsent.Set(request.Has(UsageConsent.OptionKey));
            SetupLog.Append("usage statistics: " + request.Has(UsageConsent.OptionKey));
        }

        public static void AfterExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            report(80, "Настройки…");
            AppSettings settings = pendingSettings;
            settings.EnabledSources = EnabledSources(request);
            string saveProblem = settings.Save();
            if (saveProblem != null) throw new InvalidOperationException("Не удалось записать настройки в " + AppSettings.FilePath + ": " + saveProblem.TrimEnd('.') + ".");
            SetupLog.Append("sources: " + string.Join(",", settings.EnabledSources.ToArray()));
            if (OpenRgbService.IsInstalled())
            {
                report(82, "Задача перезапуска OpenRGB…");
                string problem = OpenRgbService.InstallRestartTask(Path.Combine(request.TargetDirectory, AppIdentity.ExecutableName));
                SetupLog.Append("openrgb restart task: " + (problem ?? "installed"));
                if (problem != null) notes.Add("Задачу перезапуска зависшего OpenRGB создать не удалось (" + problem + "): AgentGlow будет просить права администратора.");
            }
            else
            {
                report(82, "Задача перезапуска OpenRGB…");
                string problem = OpenRgbService.RemoveRestartTask();
                SetupLog.Append("openrgb restart task without service: " + (problem ?? "absent or removed"));
            }
            report(85, "Интеграции…");
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null) continue;
                ApplyIntegration(entry, request.Has(SourceKeyPrefix + entry.Id), settings.EventPort, notes);
            }
        }

        public static void AfterLaunch(InstallRequest request, Action<int, string> report, List<string> notes)
        {
        }

        public static void Launch(string executable)
        {
            Process.Start(new ProcessStartInfo(SystemTools.Explorer, "\"" + executable + "\"") { UseShellExecute = false });
        }

        private static List<string> EnabledSources(InstallRequest request)
        {
            var ids = new List<string>();
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null || request.Has(SourceKeyPrefix + entry.Id)) ids.Add(entry.Id);
            }
            return ids;
        }

        private static void ApplyIntegration(SourceCatalog.Entry entry, bool wanted, int port, List<string> notes)
        {
            IIntegration integration = entry.Integration;
            try
            {
                if (!wanted)
                {
                    if (integration.Uninstall()) notes.Add(entry.DisplayName + ": интеграция снята (" + integration.Location + ").");
                    SetupLog.Append(entry.Id + " integration: not wanted");
                    return;
                }
                if (integration.IsInstalled(port))
                {
                    notes.Add(entry.DisplayName + ": интеграция уже подключена — " + integration.Location + " не тронут.");
                    SetupLog.Append(entry.Id + " integration: already installed");
                    return;
                }
                integration.Install(port);
                notes.Add(entry.DisplayName + ": подключено (" + integration.Location + "). Перезапусти " + entry.DisplayName + ", чтобы подхватить.");
                SetupLog.Append(entry.Id + " integration: installed");
            }
            catch (Exception error)
            {
                SetupLog.Append(entry.Id + " integration failed: " + error);
                notes.Add(entry.DisplayName + ": подключить не удалось (" + error.Message + "). Это можно сделать из меню трея.");
            }
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
