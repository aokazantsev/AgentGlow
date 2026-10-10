using System;
using System.Collections.Generic;

namespace AgentGlow.Sources.Generic
{
    internal sealed class GenericSource : IAgentSource
    {
        private const string LogPrefix = "generic: ";
        private const int MaxSessions = 200;
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
        private static readonly TimeSpan DoneIdleAfter = TimeSpan.FromMinutes(1);

        private readonly Dictionary<string, GenericSession> sessions = new Dictionary<string, GenericSession>();

        public string Id
        {
            get { return "generic"; }
        }

        public string DisplayName
        {
            get { return "Другие приложения"; }
        }

        public string Route
        {
            get { return "/status"; }
        }

        public bool RequiresMarker
        {
            get { return true; }
        }

        public int MaxBodyBytes
        {
            get { return 64 * 1024; }
        }

        public IIntegration Integration
        {
            get { return null; }
        }

        public DateTime LatestEventUtc
        {
            get
            {
                DateTime latest = DateTime.MinValue;
                foreach (GenericSession session in sessions.Values)
                {
                    if (session.LastEventUtc > latest) latest = session.LastEventUtc;
                }
                return latest;
            }
        }

        public int LivenessIntervalMs
        {
            get { return SourceSet.DefaultLivenessIntervalMs; }
        }

        public bool NeedsLivenessTimer
        {
            get
            {
                foreach (GenericSession session in sessions.Values)
                {
                    if (session.Process != null) return true;
                }
                return false;
            }
        }

        public void OnReceive(EventRequest request)
        {
        }

        public bool Handle(EventRequest request, DateTime nowUtc)
        {
            GenericEvent parsed = GenericEvent.Parse(request.Body);
            if (parsed == null)
            {
                AppLog.Append(LogPrefix + "unparsed event, " + request.Body.Length + " chars");
                return false;
            }
            string key = parsed.App + "/" + parsed.Session;
            if (parsed.Status == GenericEvent.Ended)
            {
                bool removed = sessions.Remove(key);
                AppLog.Append(LogPrefix + parsed.App + " ended session=" + Tail(parsed.Session) + " -> " + TopStatus());
                return removed;
            }
            GlowStatus status;
            if (!parsed.TryGetStatus(out status))
            {
                AppLog.Append(LogPrefix + parsed.App + " unknown status '" + parsed.Status + "'");
                return false;
            }
            GenericSession session;
            bool created = !sessions.TryGetValue(key, out session);
            if (created)
            {
                DropOldestIfFull();
                session = new GenericSession { App = parsed.App, Id = parsed.Session, Status = GlowStatus.Idle, StatusSinceUtc = nowUtc, Project = parsed.App };
                sessions[key] = session;
            }
            session.LastEventUtc = nowUtc;
            if (parsed.Project != null) session.Project = parsed.Project;
            if (parsed.ProcessId > 0 && (session.Process == null || session.Process.ProcessId != parsed.ProcessId))
            {
                ProcessIdentity process = ProcessIdentity.TryCapture(parsed.ProcessId);
                if (process != null) session.Process = process;
            }
            bool changed = created || session.Status != status;
            if (session.Status != status)
            {
                session.Status = status;
                session.StatusSinceUtc = nowUtc;
            }
            AppLog.Append(LogPrefix + parsed.App + " " + parsed.Status + " session=" + Tail(parsed.Session) + " -> " + TopStatus());
            return changed;
        }

        public List<ThreadInfo> Threads()
        {
            var threads = new List<ThreadInfo>();
            foreach (GenericSession session in sessions.Values)
            {
                var thread = new ThreadInfo();
                thread.Source = session.App;
                thread.Project = session.Project;
                thread.IdTail = Tail(session.Id);
                thread.Status = session.Status;
                threads.Add(thread);
            }
            return threads;
        }

        public bool CheckLiveness(DateTime nowUtc)
        {
            var ended = new List<string>();
            foreach (KeyValuePair<string, GenericSession> pair in sessions)
            {
                if (pair.Value.Process != null && !pair.Value.Process.IsAlive()) ended.Add(pair.Key);
            }
            foreach (string key in ended)
            {
                sessions.Remove(key);
            }
            if (ended.Count > 0) AppLog.Append(LogPrefix + "process ended -> " + TopStatus());
            return ended.Count > 0;
        }

        public bool Housekeep(DateTime nowUtc, AppSettings settings)
        {
            bool changed = false;
            var expired = new List<string>();
            foreach (KeyValuePair<string, GenericSession> pair in sessions)
            {
                GenericSession session = pair.Value;
                if (nowUtc - session.LastEventUtc > SessionLifetime)
                {
                    expired.Add(pair.Key);
                    continue;
                }
                bool workingStale = session.Status == GlowStatus.Working && IsOlder(session.LastEventUtc, nowUtc, settings.WorkingTimeoutMinutes);
                bool doneStale = (session.Status == GlowStatus.Done || session.Status == GlowStatus.DoneIdle)
                    && IsOlder(session.StatusSinceUtc, nowUtc, settings.DoneTimeoutMinutes);
                if (workingStale || doneStale)
                {
                    session.Status = GlowStatus.Idle;
                    session.StatusSinceUtc = nowUtc;
                    changed = true;
                }
                else if (session.Status == GlowStatus.Done && nowUtc - session.StatusSinceUtc >= DoneIdleAfter)
                {
                    session.Status = GlowStatus.DoneIdle;
                    changed = true;
                }
            }
            foreach (string key in expired)
            {
                sessions.Remove(key);
            }
            if (changed || expired.Count > 0) AppLog.Append(LogPrefix + "timeouts -> " + TopStatus());
            return changed || expired.Count > 0;
        }

        public void Clear()
        {
            sessions.Clear();
        }

        public void Load()
        {
        }

        public void Save()
        {
        }

        private GlowStatus TopStatus()
        {
            GlowStatus top = GlowStatus.Idle;
            foreach (GenericSession session in sessions.Values)
            {
                if (session.Status > top) top = session.Status;
            }
            return top;
        }

        private void DropOldestIfFull()
        {
            if (sessions.Count < MaxSessions) return;
            string oldestKey = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, GenericSession> pair in sessions)
            {
                if (pair.Value.LastEventUtc < oldest)
                {
                    oldest = pair.Value.LastEventUtc;
                    oldestKey = pair.Key;
                }
            }
            if (oldestKey != null) sessions.Remove(oldestKey);
        }

        private static bool IsOlder(DateTime sinceUtc, DateTime nowUtc, int minutes)
        {
            return minutes > 0 && nowUtc - sinceUtc >= TimeSpan.FromMinutes(minutes);
        }

        private static string Tail(string value)
        {
            return value.Length > 4 ? value.Substring(value.Length - 4) : value;
        }
    }
}
