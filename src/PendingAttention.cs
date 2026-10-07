using System;
using System.Collections.Generic;

namespace ClaudeGlow
{
    internal sealed class PendingAttention
    {
        public readonly GlowStatus Status;
        public readonly DateTime SinceUtc;
        public readonly Dictionary<string, string> CallActors;
        public bool OthersActive;

        public PendingAttention(GlowStatus status, DateTime sinceUtc)
            : this(status, sinceUtc, null)
        {
        }

        public PendingAttention(GlowStatus status, DateTime sinceUtc, Dictionary<string, string> callActors)
        {
            Status = status;
            SinceUtc = sinceUtc;
            CallActors = callActors;
        }

        public bool BelongsTo(string key, string actor)
        {
            return CallActors == null ? key == actor : CallActors.ContainsValue(actor);
        }
    }
}
