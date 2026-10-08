namespace AgentGlow.Sources.Claude
{
    internal sealed class ClaudeIntegration : IIntegration
    {
        public string Id
        {
            get { return "claude"; }
        }

        public string OptionText
        {
            get { return "Claude Code: прописать хуки"; }
        }

        public string OptionHint
        {
            get { return "В ~\\.claude\\settings.json добавляются только хуки AgentGlow, копия прежнего файла остаётся рядом. Уже открытые сессии подхватят хуки после перезапуска."; }
        }

        public string Location
        {
            get { return ClaudeHooks.SettingsPath; }
        }

        public bool IsInstalled(int port)
        {
            return ClaudeHooks.AreInstalled(port);
        }

        public void Install(int port)
        {
            ClaudeHooks.Install(port);
        }

        public bool Uninstall()
        {
            return ClaudeHooks.Uninstall();
        }
    }
}
