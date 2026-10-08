using System;
using System.Collections.Generic;

namespace AgentGlow
{
    internal interface IAgentSource
    {
        string Id { get; }
        string DisplayName { get; }
        string Route { get; }
        bool RequiresMarker { get; }
        int MaxBodyBytes { get; }
        IIntegration Integration { get; }
        DateTime LatestEventUtc { get; }
        bool NeedsLivenessTimer { get; }
        void OnReceive(EventRequest request);
        bool Handle(EventRequest request, DateTime nowUtc);
        List<ThreadInfo> Threads();
        bool CheckLiveness(DateTime nowUtc);
        bool Housekeep(DateTime nowUtc, AppSettings settings);
        void Clear();
        void Load();
        void Save();
    }
}
