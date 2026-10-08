using System;

namespace AgentGlow.Sources.Generic
{
    internal sealed class GenericSession
    {
        public string App;
        public string Id;
        public string Project;
        public GlowStatus Status;
        public DateTime LastEventUtc;
        public DateTime StatusSinceUtc;
        public ProcessIdentity Process;
    }
}
