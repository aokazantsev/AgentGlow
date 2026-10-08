namespace AgentGlow.Sources.OpenCode
{
    internal sealed class OpenCodeIntegration : IIntegration
    {
        public string Id
        {
            get { return "opencode"; }
        }

        public string OptionText
        {
            get { return "OpenCode: подключить плагин"; }
        }

        public string OptionHint
        {
            get { return "Файл agentglow.js кладётся в ~\\.config\\opencode\\plugins, настройки OpenCode не меняются. Плагин пересылает только состояние сессий. После установки перезапусти OpenCode."; }
        }

        public string Location
        {
            get { return OpenCodePlugin.PluginPath; }
        }

        public bool IsInstalled(int port)
        {
            return OpenCodePlugin.IsInstalled(port);
        }

        public void Install(int port)
        {
            OpenCodePlugin.Install(port);
        }

        public bool Uninstall()
        {
            return OpenCodePlugin.Uninstall();
        }
    }
}
