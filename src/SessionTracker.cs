using System;
using System.Collections.Generic;
using System.IO;

namespace ClaudeGlow
{
    internal sealed class SessionTracker
    {
        private const string AskUserQuestionTool = "AskUserQuestion";
        private const string MainThread = "";
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
        private static readonly TimeSpan DoneIdleAfter = TimeSpan.FromMinutes(1);

        private readonly Dictionary<string, SessionState> sessions = new Dictionary<string, SessionState>();

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
            return sessions.TryGetValue(sessionId, out session) ? session.Status.ToString() : "removed";
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
        }

        public bool Apply(HookEvent hookEvent, ProcessIdentity process, DateTime nowUtc)
        {
            if (hookEvent.EventName == "SessionEnd") return sessions.Remove(hookEvent.SessionId);
            bool created = !sessions.ContainsKey(hookEvent.SessionId);
            SessionState session = GetOrCreate(hookEvent.SessionId, nowUtc);
            session.LastEventUtc = nowUtc;
            if (!string.IsNullOrEmpty(hookEvent.Cwd)) session.Project = ProjectName(hookEvent.Cwd);
            if (process != null) session.Process = process;
            if (hookEvent.EventName == "Stop") session.BackgroundTaskCount = hookEvent.BackgroundTaskCount;
            string before = Signature(session);
            ApplyAttention(session, hookEvent, nowUtc);
            TrackActivity(session, hookEvent);
            GlowStatus next = NextWorkStatus(session.WorkStatus, hookEvent);
            if (next != session.WorkStatus)
            {
                session.WorkStatus = next;
                session.StatusSinceUtc = nowUtc;
            }
            return created || Signature(session) != before;
        }

        private static void ApplyAttention(SessionState session, HookEvent hookEvent, DateTime nowUtc)
        {
            string actor = AttentionActor(session, hookEvent);
            switch (hookEvent.EventName)
            {
                case "UserPromptSubmit":
                case "StopFailure":
                    session.Pending.Clear();
                    break;
                case "Stop":
                    if (hookEvent.BackgroundTaskCount > 0) session.Pending.Remove(MainThread);
                    else session.Pending.Clear();
                    break;
                case "PostToolUse":
                case "PostToolUseFailure":
                case "PermissionDenied":
                    session.Pending.Remove(actor);
                    break;
                case "PreToolUse":
                    if (hookEvent.ToolName == AskUserQuestionTool) session.Pending[actor] = new PendingAttention(GlowStatus.Question, nowUtc);
                    break;
                case "Notification":
                    GlowStatus attention = NotificationAttention(hookEvent);
                    if (attention != GlowStatus.Idle) session.Pending[actor] = new PendingAttention(attention, nowUtc);
                    break;
            }
        }

        private static string AttentionActor(SessionState session, HookEvent hookEvent)
        {
            if (hookEvent.AgentId != null) return hookEvent.AgentId;
            bool fromBackgroundAgent = hookEvent.EventName == "Notification"
                && session.WaitsForBackgroundTasks
                && session.LastAgentId != null;
            return fromBackgroundAgent ? session.LastAgentId : MainThread;
        }

        private static void TrackActivity(SessionState session, HookEvent hookEvent)
        {
            if (hookEvent.AgentId != null) session.LastAgentId = hookEvent.AgentId;
            else if (hookEvent.EventName == "Stop") session.WaitsForBackgroundTasks = hookEvent.BackgroundTaskCount > 0;
            else if (hookEvent.EventName != "Notification") session.WaitsForBackgroundTasks = false;
        }

        private static string Signature(SessionState session)
        {
            return session.WorkStatus + "|" + session.Status + "|" + session.Pending.Count;
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

        public bool HasPendingPermissions
        {
            get
            {
                foreach (SessionState session in sessions.Values)
                {
                    if (session.Process != null && HasPendingPermission(session)) return true;
                }
                return false;
            }
        }

        public bool PromoteApprovedPermissions(DateTime nowUtc)
        {
            bool changed = false;
            foreach (SessionState session in sessions.Values)
            {
                if (session.Process == null) continue;
                var approved = new List<string>();
                foreach (KeyValuePair<string, PendingAttention> pair in session.Pending)
                {
                    if (pair.Value.Status != GlowStatus.Permission) continue;
                    if (ProcessTree.HasChildStartedAfter(session.Process.ProcessId, pair.Value.SinceUtc)) approved.Add(pair.Key);
                }
                foreach (string actor in approved)
                {
                    session.Pending.Remove(actor);
                }
                if (approved.Count == 0) continue;
                if (session.WorkStatus != GlowStatus.Error) session.WorkStatus = GlowStatus.Working;
                session.StatusSinceUtc = nowUtc;
                changed = true;
            }
            return changed;
        }

        private static bool HasPendingPermission(SessionState session)
        {
            foreach (PendingAttention attention in session.Pending.Values)
            {
                if (attention.Status == GlowStatus.Permission) return true;
            }
            return false;
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
                bool workingStale = session.WorkStatus == GlowStatus.Working
                    && session.BackgroundTaskCount == 0
                    && session.Pending.Count == 0
                    && IsOlder(session.LastEventUtc, nowUtc, workingTimeoutMinutes);
                bool doneStale = (session.WorkStatus == GlowStatus.Done || session.WorkStatus == GlowStatus.DoneIdle)
                    && IsOlder(session.StatusSinceUtc, nowUtc, doneTimeoutMinutes);
                if (workingStale || doneStale)
                {
                    session.WorkStatus = GlowStatus.Idle;
                    session.StatusSinceUtc = nowUtc;
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

        private static GlowStatus NextWorkStatus(GlowStatus current, HookEvent hookEvent)
        {
            switch (hookEvent.EventName)
            {
                case "UserPromptSubmit":
                    return GlowStatus.Working;
                case "PostToolUse":
                case "PostToolUseFailure":
                case "PermissionDenied":
                case "PreToolUse":
                    return current == GlowStatus.Error ? current : GlowStatus.Working;
                case "Stop":
                    if (current == GlowStatus.Error) return current;
                    return hookEvent.BackgroundTaskCount > 0 ? GlowStatus.Working : GlowStatus.Done;
                case "StopFailure":
                    return GlowStatus.Error;
                case "Notification":
                    bool idle = NotificationType(hookEvent) == "idle_prompt";
                    return idle && current == GlowStatus.Done ? GlowStatus.DoneIdle : current;
                default:
                    return current;
            }
        }

        private static GlowStatus NotificationAttention(HookEvent hookEvent)
        {
            string type = NotificationType(hookEvent);
            if (type == "permission_prompt") return GlowStatus.Permission;
            return type == "elicitation_dialog" ? GlowStatus.Question : GlowStatus.Idle;
        }

        private static string NotificationType(HookEvent hookEvent)
        {
            return hookEvent.NotificationType ?? GuessNotificationType(hookEvent.Message);
        }

        private static string GuessNotificationType(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            if (message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) return "permission_prompt";
            if (message.IndexOf("waiting for your input", StringComparison.OrdinalIgnoreCase) >= 0) return "idle_prompt";
            return null;
        }

        private SessionState GetOrCreate(string sessionId, DateTime nowUtc)
        {
            SessionState session;
            if (sessions.TryGetValue(sessionId, out session)) return session;
            session = new SessionState();
            session.WorkStatus = GlowStatus.Idle;
            session.StatusSinceUtc = nowUtc;
            session.Project = "?";
            sessions[sessionId] = session;
            return session;
        }

        private static bool IsOlder(DateTime sinceUtc, DateTime nowUtc, int minutes)
        {
            return minutes > 0 && nowUtc - sinceUtc >= TimeSpan.FromMinutes(minutes);
        }

        private static string ProjectName(string cwd)
        {
            string trimmed = cwd.TrimEnd('\\', '/');
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
