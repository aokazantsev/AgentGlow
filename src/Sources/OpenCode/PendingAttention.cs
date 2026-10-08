using System;

namespace AgentGlow.Sources.OpenCode
{
    internal sealed class PendingAttention
    {
        public readonly GlowStatus Status;
        public readonly string SessionId;
        public readonly DateTime SinceUtc;

        public PendingAttention(GlowStatus status, string sessionId, DateTime sinceUtc)
        {
            Status = status;
            SessionId = sessionId;
            SinceUtc = sinceUtc;
        }
    }
}
