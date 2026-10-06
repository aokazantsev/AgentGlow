using System;
using System.Collections.Generic;

namespace ClaudeGlow
{
    internal static class UninstallProfile
    {
        public const string ConfirmDetails =
            "Хуки ClaudeGlow уберутся из настроек Claude Code, остальные хуки и настройки останутся.";

        public static void Remove(List<string> problems)
        {
            try
            {
                ClaudeHooks.Uninstall();
            }
            catch (Exception error)
            {
                problems.Add("хуки в " + ClaudeHooks.SettingsPath + " не сняты: " + error.Message);
            }
            string taskProblem = OpenRgbService.RemoveRestartTask();
            if (taskProblem != null) problems.Add("задача «" + OpenRgbService.RestartTaskName + "» не удалена: " + taskProblem);
        }
    }
}
