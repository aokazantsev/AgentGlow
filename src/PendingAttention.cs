using System;
using System.Collections.Generic;

namespace ClaudeGlow
{
    internal sealed class PendingAttention
    {
        public readonly GlowStatus Status;
        public readonly DateTime SinceUtc;
        public readonly Dictionary<string, string> ToolUseActors;
        public bool OthersActive;

        public PendingAttention(GlowStatus status, DateTime sinceUtc)
            : this(status, sinceUtc, null)
        {
        }

        public PendingAttention(GlowStatus status, DateTime sinceUtc, Dictionary<string, string> toolUseActors)
        {
            Status = status;
            SinceUtc = sinceUtc;
            ToolUseActors = toolUseActors;
        }

        public bool BelongsTo(string key, string actor)
        {
            return ToolUseActors == null ? key == actor : ToolUseActors.ContainsValue(actor);
        }
    }
}
