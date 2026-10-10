using System;

namespace AgentGlow.Sources.Claude
{
    internal sealed class PermissionRequestRecord
    {
        public readonly string Actor;
        public readonly DateTime SinceUtc;
        public readonly string CommandFingerprint;

        public PermissionRequestRecord(string actor, DateTime sinceUtc, string commandFingerprint)
        {
            Actor = actor;
            SinceUtc = sinceUtc;
            CommandFingerprint = commandFingerprint;
        }
    }
}
