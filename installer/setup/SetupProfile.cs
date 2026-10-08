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
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null) continue;
                options.Add(new SetupOption
                {
                    Key = SourceKeyPrefix + entry.Id,
                    Text = entry.Integration.OptionText,
                    Hint = entry.Integration.OptionHint,
                    Checked = true
                });
            }
            options.Add(new SetupOption
            {
                Key = CrashReportConsent.OptionKey,
                Text = "Отправлять автору отчёты о сбоях",
                Hint = "В отчёт попадает журнал программы, а в нём — пути к настройкам Claude Code и плагину OpenCode с именем "
                    + "пользователя Windows и названия RGB-устройств. Не включай, если это запрещают правила твоей компании.",
                DetailsTitle = "Что уходит в отчёте",
                Details = "Отчёт уходит один раз — при падении программы — на aokazantsev.ru (сервер в России), "
                    + "без повторных попыток:\n"
                    + "• версия программы, Windows и .NET;\n"
                    + "• текст ошибки;\n"
                    + "• последние 100 КБ журнала %LOCALAPPDATA%\\AgentGlow\\log.txt: названия событий и инструментов агентов, "
                    + "короткие номера сессий и процессов, имена папок проектов, пути к настройкам Claude Code и плагину OpenCode, "
                    + "названия RGB-устройств;\n"
                    + "• IP-адрес, с которого пришёл отчёт.\n"
                    + "Тексты запросов, ответы и код агентов в журнал не попадают. Отчёты видит только автор, хранятся последние 50 МБ. "
                    + "Изменить выбор — переустановить программу.",
                Checked = CrashReportConsent.IsGiven
            });
            return options;
        }

        public static void BeforeExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            if (request.Has(ResetSettingsKey))
            {
                string settingsPath = Path.Combine(AppIdentity.DataDirectory, "settings.txt");
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
                SetupLog.Append("settings reset");
            }
            CrashReportConsent.Set(request.Has(CrashReportConsent.OptionKey));
            SetupLog.Append("crash reports: " + request.Has(CrashReportConsent.OptionKey));
        }

        public static void AfterExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            if (OpenRgbService.IsInstalled())
            {
                report(82, "Задача перезапуска OpenRGB…");
                string problem = OpenRgbService.InstallRestartTask(Path.Combine(request.TargetDirectory, AppIdentity.ExecutableName));
                SetupLog.Append("openrgb restart task: " + (problem ?? "installed"));
                if (problem != null) notes.Add("Задачу перезапуска зависшего OpenRGB создать не удалось (" + problem + "): AgentGlow будет просить права администратора.");
            }
            AppSettings settings = AppSettings.Load();
            settings.EnabledSources = EnabledSources(request);
            if (!settings.TrySave()) notes.Add("Не удалось записать settings.txt — источники событий остались прежними.");
            SetupLog.Append("sources: " + string.Join(",", settings.EnabledSources.ToArray()));
            report(85, "Интеграции…");
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null) continue;
                ApplyIntegration(entry, request.Has(SourceKeyPrefix + entry.Id), settings.EventPort, notes);
            }
        }

        public static void Launch(string executable)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + executable + "\"") { UseShellExecute = false });
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
