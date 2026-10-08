namespace AgentGlow.Sources.OpenCode
{
    internal sealed class GlowEvent
    {
        public string Kind;
        public string SessionId;
        public string ParentId;
        public string Directory;
        public string RequestId;
        public string ErrorName;
        public int ProcessId;
        public long RetryAtMs;
        public int PluginVersion;
    }
}
