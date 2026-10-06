using System;

namespace ClaudeGlow
{
    internal sealed class PendingAttention
    {
        public readonly GlowStatus Status;
        public readonly DateTime SinceUtc;

        public PendingAttention(GlowStatus status, DateTime sinceUtc)
        {
            Status = status;
            SinceUtc = sinceUtc;
        }
    }
}
