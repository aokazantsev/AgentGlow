using System;
using System.Collections.Generic;

namespace ClaudeGlow
{
    internal sealed class SessionState
    {
        public string Project;
        public ProcessIdentity Process;
        public GlowStatus WorkStatus;
        public DateTime LastEventUtc;
        public DateTime StatusSinceUtc;
        public int BackgroundTaskCount;
        public bool WaitsForBackgroundTasks;
        public string LastAgentId;
        public readonly Dictionary<string, PendingAttention> Pending = new Dictionary<string, PendingAttention>();

        public GlowStatus Status
        {
            get
            {
                GlowStatus status = WorkStatus;
                foreach (PendingAttention attention in Pending.Values)
                {
                    if (attention.Status > status) status = attention.Status;
                }
                return status;
            }
        }
    }
}
