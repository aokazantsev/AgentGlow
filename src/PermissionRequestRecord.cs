using System;

namespace ClaudeGlow
{
    internal sealed class PermissionRequestRecord
    {
        public readonly string Actor;
        public readonly DateTime SinceUtc;

        public PermissionRequestRecord(string actor, DateTime sinceUtc)
        {
            Actor = actor;
            SinceUtc = sinceUtc;
        }
    }
}
