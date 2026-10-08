using System;
using System.Collections.Generic;

namespace AgentGlow
{
    internal sealed class SourceSet
    {
        private readonly List<IAgentSource> sources;

        public SourceSet(List<IAgentSource> sources)
        {
            this.sources = sources;
        }

        public List<IAgentSource> All
        {
            get { return sources; }
        }

        public IAgentSource ByRoute(string route)
        {
            foreach (IAgentSource source in sources)
            {
                if (string.Equals(source.Route, route, StringComparison.OrdinalIgnoreCase)) return source;
            }
            return null;
        }

        public GlowStatus TopStatus
        {
            get
            {
                GlowStatus top = GlowStatus.Idle;
                foreach (ThreadInfo thread in Threads())
                {
                    if (thread.Status > top) top = thread.Status;
                }
                return top;
            }
        }

        public bool Has(GlowStatus status)
        {
            foreach (ThreadInfo thread in Threads())
            {
                if (thread.Status == status) return true;
            }
            return false;
        }

        public List<ThreadInfo> Threads()
        {
            var threads = new List<ThreadInfo>();
            foreach (IAgentSource source in sources)
            {
                threads.AddRange(source.Threads());
            }
            return threads;
        }

        public List<ThreadInfo> ThreadsByStatus()
        {
            List<ThreadInfo> threads = Threads();
            threads.Sort(CompareByStatusDescending);
            return threads;
        }

        public DateTime LatestEventUtc
        {
            get
            {
                DateTime latest = DateTime.MinValue;
                foreach (IAgentSource source in sources)
                {
                    if (source.LatestEventUtc > latest) latest = source.LatestEventUtc;
                }
                return latest;
            }
        }

        public bool NeedsLivenessTimer
        {
            get
            {
                foreach (IAgentSource source in sources)
                {
                    if (source.NeedsLivenessTimer) return true;
                }
                return false;
            }
        }

        public bool CheckLiveness(DateTime nowUtc)
        {
            bool changed = false;
            foreach (IAgentSource source in sources)
            {
                if (source.CheckLiveness(nowUtc)) changed = true;
            }
            return changed;
        }

        public bool Housekeep(DateTime nowUtc, AppSettings settings)
        {
            bool changed = false;
            foreach (IAgentSource source in sources)
            {
                if (source.Housekeep(nowUtc, settings)) changed = true;
            }
            return changed;
        }

        public void Load()
        {
            foreach (IAgentSource source in sources)
            {
                source.Load();
            }
        }

        public void Save()
        {
            foreach (IAgentSource source in sources)
            {
                source.Save();
            }
        }

        public void Clear()
        {
            foreach (IAgentSource source in sources)
            {
                source.Clear();
            }
            Save();
        }

        private static int CompareByStatusDescending(ThreadInfo left, ThreadInfo right)
        {
            int byStatus = right.Status.CompareTo(left.Status);
            return byStatus != 0 ? byStatus : string.Compare(left.Project, right.Project, StringComparison.OrdinalIgnoreCase);
        }
    }
}
