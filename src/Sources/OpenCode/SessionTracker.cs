using System;
using System.Collections.Generic;
using System.IO;

namespace AgentGlow.Sources.OpenCode
{
    internal sealed class SessionTracker
    {
        private const int MaxParentDepth = 16;
        private const string CrashPlaceholderPrefix = "\0crash/";
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
        private static readonly TimeSpan DoneIdleAfter = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan RetryErrorGrace = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RetryErrorDefault = TimeSpan.FromSeconds(60);
        private const long MaxEpochMs = 4102444800000L;
        private static readonly DateTime EpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Dictionary<string, SessionState> sessions = new Dictionary<string, SessionState>();
        private readonly Dictionary<string, string> parents = new Dictionary<string, string>();

        public GlowStatus TopStatus
        {
            get
            {
                GlowStatus top = GlowStatus.Idle;
                foreach (SessionState session in sessions.Values)
                {
                    if (session.Status > top) top = session.Status;
                }
                return top;
            }
        }

        public List<SessionState> SessionsByStatus()
        {
            var list = new List<SessionState>(sessions.Values);
            list.Sort(CompareByStatusDescending);
            return list;
        }

        public DateTime LatestEventUtc
        {
            get
            {
                DateTime latest = DateTime.MinValue;
                foreach (SessionState session in sessions.Values)
                {
                    if (session.LastEventUtc > latest) latest = session.LastEventUtc;
                }
                return latest;
            }
        }

        public string StatusOf(string sessionId)
        {
            SessionState session;
            return sessions.TryGetValue(RootOf(sessionId), out session) ? session.Status.ToString() : "removed";
        }

        public bool Has(GlowStatus status)
        {
            foreach (SessionState session in sessions.Values)
            {
                if (session.Status == status) return true;
            }
            return false;
        }

        public List<KeyValuePair<string, SessionState>> Snapshot()
        {
            return new List<KeyValuePair<string, SessionState>>(sessions);
        }

        public void Restore(List<KeyValuePair<string, SessionState>> saved)
        {
            foreach (KeyValuePair<string, SessionState> pair in saved)
            {
                sessions[pair.Key] = pair.Value;
            }
        }

        public void Clear()
        {
            sessions.Clear();
            parents.Clear();
        }

        public bool Apply(GlowEvent glowEvent, DateTime nowUtc)
        {
            if (glowEvent.SessionId == null) return false;
            if (glowEvent.Kind == GlowEventKinds.SessionDeleted) return Remove(glowEvent.SessionId, nowUtc);
            if (glowEvent.ParentId != null && glowEvent.ParentId != glowEvent.SessionId) parents[glowEvent.SessionId] = glowEvent.ParentId;
            string rootId = RootOf(glowEvent.SessionId);
            bool isChild = rootId != glowEvent.SessionId;
            bool created = !sessions.ContainsKey(rootId);
            SessionState session = GetOrCreate(rootId, nowUtc);
            session.LastEventUtc = nowUtc;
            if (!isChild && !string.IsNullOrEmpty(glowEvent.Directory)) session.Project = ProjectName(glowEvent.Directory);
            if (glowEvent.ProcessId > 0 && (session.Process == null || session.Process.ProcessId != glowEvent.ProcessId))
            {
                ProcessIdentity process = ProcessIdentity.TryCapture(glowEvent.ProcessId);
                if (process != null) session.Process = process;
            }
            if (session.CrashedProcessId != 0 && glowEvent.ProcessId != session.CrashedProcessId)
            {
                session.CrashedProcessId = 0;
                if (session.WorkStatus == GlowStatus.Error && !session.ErrorUntilUtc.HasValue) SetWorkStatus(session, GlowStatus.Idle, nowUtc);
            }
            string before = Signature(session);
            ApplyKind(session, glowEvent, isChild, nowUtc);
            return created || Signature(session) != before;
        }

        private void ApplyKind(SessionState session, GlowEvent glowEvent, bool isChild, DateTime nowUtc)
        {
            string sessionId = glowEvent.SessionId;
            switch (glowEvent.Kind)
            {
                case GlowEventKinds.Busy:
                    ResumeAfterRetry(session, sessionId, nowUtc);
                    if (!isChild) BecomeWorking(session, nowUtc);
                    break;
                case GlowEventKinds.Idle:
                    if (isChild)
                    {
                        ClearPendingOf(session, sessionId);
                        ResumeAfterRetry(session, sessionId, nowUtc);
                    }
                    else
                    {
                        BecomeDone(session, nowUtc);
                    }
                    break;
                case GlowEventKinds.Retry:
                    BecomeRetrying(session, sessionId, glowEvent.RetryAtMs, nowUtc);
                    break;
                case GlowEventKinds.Error:
                    if (glowEvent.ErrorName == GlowEventKinds.AbortedError)
                    {
                        if (isChild) ClearPendingOf(session, sessionId);
                        else session.Pending.Clear();
                        ResumeAfterRetry(session, sessionId, nowUtc);
                    }
                    else if (!isChild)
                    {
                        session.Pending.Clear();
                        session.Retrying.Clear();
                        SetWorkStatus(session, GlowStatus.Error, nowUtc);
                        session.ErrorUntilUtc = null;
                    }
                    break;
                case GlowEventKinds.PermissionAsked:
                    session.Pending[RequestKey(glowEvent, "permission")] = new PendingAttention(GlowStatus.Permission, sessionId, nowUtc);
                    break;
                case GlowEventKinds.QuestionAsked:
                    session.Pending[RequestKey(glowEvent, "question")] = new PendingAttention(GlowStatus.Question, sessionId, nowUtc);
                    break;
                case GlowEventKinds.PermissionReplied:
                    ResolveRequest(session, RequestKey(glowEvent, "permission"));
                    break;
                case GlowEventKinds.QuestionReplied:
                case GlowEventKinds.QuestionRejected:
                    ResolveRequest(session, RequestKey(glowEvent, "question"));
                    break;
            }
        }

        private void ResolveRequest(SessionState session, string key)
        {
            if (session.Pending.Remove(key)) return;
            foreach (SessionState other in sessions.Values)
            {
                if (other.Pending.Remove(key)) return;
            }
        }

        private static string RequestKey(GlowEvent glowEvent, string family)
        {
            return glowEvent.RequestId ?? (family + "/" + glowEvent.SessionId);
        }

        private static void ClearPendingOf(SessionState session, string sessionId)
        {
            var resolved = new List<string>();
            foreach (KeyValuePair<string, PendingAttention> pair in session.Pending)
            {
                if (pair.Value.SessionId == sessionId) resolved.Add(pair.Key);
            }
            foreach (string key in resolved)
            {
                session.Pending.Remove(key);
            }
        }

        private static void BecomeWorking(SessionState session, DateTime nowUtc)
        {
            bool retrying = session.WorkStatus == GlowStatus.Error && session.ErrorUntilUtc.HasValue && session.ErrorUntilUtc.Value > nowUtc;
            if (retrying) return;
            session.ErrorUntilUtc = null;
            SetWorkStatus(session, GlowStatus.Working, nowUtc);
        }

        private static void BecomeDone(SessionState session, DateTime nowUtc)
        {
            session.Pending.Clear();
            session.Retrying.Clear();
            bool hardError = session.WorkStatus == GlowStatus.Error && !session.ErrorUntilUtc.HasValue;
            if (hardError) return;
            bool wasBusy = session.WorkStatus == GlowStatus.Working || session.WorkStatus == GlowStatus.Error;
            session.ErrorUntilUtc = null;
            if (wasBusy) SetWorkStatus(session, GlowStatus.Done, nowUtc);
        }

        private static void BecomeRetrying(SessionState session, string sessionId, long retryAtMs, DateTime nowUtc)
        {
            DateTime until = nowUtc + RetryErrorDefault;
            if (retryAtMs > 0)
            {
                DateTime retryAt = EpochUtc.AddMilliseconds(Math.Min(retryAtMs, MaxEpochMs));
                until = (retryAt > nowUtc ? retryAt : nowUtc) + RetryErrorGrace;
            }
            if (session.ErrorUntilUtc.HasValue && session.ErrorUntilUtc.Value > until) until = session.ErrorUntilUtc.Value;
            session.Retrying.Add(sessionId);
            SetWorkStatus(session, GlowStatus.Error, nowUtc);
            session.ErrorUntilUtc = until;
        }

        private static void ResumeAfterRetry(SessionState session, string sessionId, DateTime nowUtc)
        {
            if (!session.Retrying.Remove(sessionId)) return;
            if (session.Retrying.Count > 0) return;
            if (session.WorkStatus != GlowStatus.Error || !session.ErrorUntilUtc.HasValue) return;
            session.ErrorUntilUtc = null;
            SetWorkStatus(session, GlowStatus.Working, nowUtc);
        }

        private static void SetWorkStatus(SessionState session, GlowStatus status, DateTime nowUtc)
        {
            if (session.WorkStatus == status) return;
            session.WorkStatus = status;
            session.StatusSinceUtc = nowUtc;
        }

        private bool Remove(string sessionId, DateTime nowUtc)
        {
            bool removed = sessions.Remove(sessionId);
            parents.Remove(sessionId);
            foreach (SessionState session in sessions.Values)
            {
                string before = Signature(session);
                ClearPendingOf(session, sessionId);
                ResumeAfterRetry(session, sessionId, nowUtc);
                if (Signature(session) != before) removed = true;
            }
            return removed;
        }

        private string RootOf(string sessionId)
        {
            string current = sessionId;
            for (int depth = 0; depth < MaxParentDepth; depth++)
            {
                string parent;
                if (!parents.TryGetValue(current, out parent)) return current;
                current = parent;
            }
            return current;
        }

        private static string Signature(SessionState session)
        {
            return session.WorkStatus + "|" + session.Status + "|" + session.Pending.Count + "|" + session.Project;
        }

        public bool HasTrackedProcesses
        {
            get
            {
                foreach (SessionState session in sessions.Values)
                {
                    if (session.Process != null) return true;
                }
                return false;
            }
        }

        public int MarkCrashed(int processId, string processLabel, DateTime nowUtc)
        {
            int affected = 0;
            foreach (SessionState session in sessions.Values)
            {
                if (session.Process == null || session.Process.ProcessId != processId) continue;
                MarkSessionCrashed(session, processId, nowUtc);
                affected++;
            }
            if (affected > 0) return affected;
            SessionState placeholder = GetOrCreate(CrashPlaceholderPrefix + processId, nowUtc);
            placeholder.Project = processLabel;
            MarkSessionCrashed(placeholder, processId, nowUtc);
            return 0;
        }

        private static void MarkSessionCrashed(SessionState session, int processId, DateTime nowUtc)
        {
            session.Pending.Clear();
            session.Retrying.Clear();
            session.Process = null;
            session.CrashedProcessId = processId;
            session.ErrorUntilUtc = null;
            session.LastEventUtc = nowUtc;
            SetWorkStatus(session, GlowStatus.Error, nowUtc);
        }

        public bool ForgetCrashedExcept(int processId)
        {
            var forgotten = new List<string>();
            foreach (KeyValuePair<string, SessionState> pair in sessions)
            {
                if (pair.Value.CrashedProcessId != 0 && pair.Value.CrashedProcessId != processId) forgotten.Add(pair.Key);
            }
            foreach (string id in forgotten)
            {
                sessions.Remove(id);
            }
            return forgotten.Count > 0;
        }

        public bool RemoveProcess(int processId)
        {
            var ended = new List<string>();
            foreach (KeyValuePair<string, SessionState> pair in sessions)
            {
                if (pair.Value.Process != null && pair.Value.Process.ProcessId == processId) ended.Add(pair.Key);
            }
            foreach (string id in ended)
            {
                sessions.Remove(id);
            }
            return ended.Count > 0;
        }

        public bool RemoveEndedProcesses()
        {
            var ended = new List<string>();
            foreach (KeyValuePair<string, SessionState> pair in sessions)
            {
                if (pair.Value.Process != null && !pair.Value.Process.IsAlive()) ended.Add(pair.Key);
            }
            foreach (string id in ended)
            {
                sessions.Remove(id);
            }
            return ended.Count > 0;
        }

        public bool Expire(DateTime nowUtc, int workingTimeoutMinutes, int doneTimeoutMinutes)
        {
            bool changed = false;
            var expired = new List<string>();
            foreach (KeyValuePair<string, SessionState> pair in sessions)
            {
                SessionState session = pair.Value;
                if (nowUtc - session.LastEventUtc > SessionLifetime)
                {
                    expired.Add(pair.Key);
                    continue;
                }
                bool retryOver = session.WorkStatus == GlowStatus.Error
                    && session.ErrorUntilUtc.HasValue
                    && session.ErrorUntilUtc.Value <= nowUtc;
                bool workingStale = session.WorkStatus == GlowStatus.Working
                    && session.Pending.Count == 0
                    && IsOlder(session.LastEventUtc, nowUtc, workingTimeoutMinutes);
                bool doneStale = (session.WorkStatus == GlowStatus.Done || session.WorkStatus == GlowStatus.DoneIdle)
                    && IsOlder(session.StatusSinceUtc, nowUtc, doneTimeoutMinutes);
                if (retryOver)
                {
                    session.ErrorUntilUtc = null;
                    session.Retrying.Clear();
                    SetWorkStatus(session, GlowStatus.Working, nowUtc);
                    changed = true;
                }
                else if (workingStale || doneStale)
                {
                    SetWorkStatus(session, GlowStatus.Idle, nowUtc);
                    changed = true;
                }
                else if (session.WorkStatus == GlowStatus.Done && nowUtc - session.StatusSinceUtc >= DoneIdleAfter)
                {
                    session.WorkStatus = GlowStatus.DoneIdle;
                    changed = true;
                }
            }
            foreach (string id in expired)
            {
                sessions.Remove(id);
            }
            return changed || expired.Count > 0;
        }

        private SessionState GetOrCreate(string sessionId, DateTime nowUtc)
        {
            SessionState session;
            if (sessions.TryGetValue(sessionId, out session)) return session;
            session = new SessionState();
            session.WorkStatus = GlowStatus.Idle;
            session.StatusSinceUtc = nowUtc;
            session.LastEventUtc = nowUtc;
            session.Project = "?";
            sessions[sessionId] = session;
            return session;
        }

        private static bool IsOlder(DateTime sinceUtc, DateTime nowUtc, int minutes)
        {
            return minutes > 0 && nowUtc - sinceUtc >= TimeSpan.FromMinutes(minutes);
        }

        private static string ProjectName(string directory)
        {
            string trimmed = directory.TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }

        private static int CompareByStatusDescending(SessionState left, SessionState right)
        {
            int byStatus = right.Status.CompareTo(left.Status);
            return byStatus != 0 ? byStatus : string.Compare(left.Project, right.Project, StringComparison.OrdinalIgnoreCase);
        }
    }
}
