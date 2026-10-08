using System;
using System.Collections.Generic;

namespace AgentGlow
{
    internal static class UninstallProfile
    {
        public const string ConfirmDetails =
            "Хуки Claude Code и плагин OpenCode, которые поставил AgentGlow, будут сняты; чужие хуки, плагины и остальные настройки останутся.";

        public static void Remove(List<string> problems)
        {
            foreach (SourceCatalog.Entry entry in SourceCatalog.All)
            {
                if (entry.Integration == null) continue;
                try
                {
                    entry.Integration.Uninstall();
                }
                catch (Exception error)
                {
                    problems.Add(entry.DisplayName + ": интеграция в " + entry.Integration.Location + " не снята: " + error.Message);
                }
            }
            string taskProblem = OpenRgbService.RemoveRestartTask();
            if (taskProblem != null) problems.Add("задача «" + OpenRgbService.RestartTaskName + "» не удалена: " + taskProblem);
        }
    }
}
