namespace AgentGlow
{
    internal interface IIntegration
    {
        string Id { get; }
        string OptionText { get; }
        string OptionHint { get; }
        string Location { get; }
        bool IsInstalled(int port);
        void Install(int port);
        bool Uninstall();
    }
}
