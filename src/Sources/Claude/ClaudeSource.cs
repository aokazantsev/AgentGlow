using System;
using System.Collections.Generic;

namespace AgentGlow.Sources.Claude
{
    internal sealed class ClaudeSource : IAgentSource
    {
        private const string LogPrefix = "claude: ";
        private const int PendingPermissionLivenessIntervalMs = 1000;

        private readonly SessionTracker tracker = new SessionTracker();
        private readonly IIntegration integration = new ClaudeIntegration();

        public string Id
        {
            get { return "claude"; }
        }

        public string DisplayName
        {
            get { return "Claude Code"; }
        }

        public string Route
        {
            get { return "/hook"; }
        }

        public bool RequiresMarker
        {
            get { return false; }
        }

        public int MaxBodyBytes
        {
            get { return 64 * 1024 * 1024; }
        }

        public IIntegration Integration
        {
            get { return integration; }
        }

        public DateTime LatestEventUtc
        {
            get { return tracker.LatestEventUtc; }
        }

        public bool NeedsLivenessTimer
        {
            get { return tracker.HasTrackedProcesses; }
        }

        public int LivenessIntervalMs
        {
            get { return tracker.HasPendingPermissions ? PendingPermissionLivenessIntervalMs : SourceSet.DefaultLivenessIntervalMs; }
        }

        public void OnReceive(EventRequest request)
        {
            request.Context = FindClaudeProcess(request.Client, request.Port);
        }

        public bool Handle(EventRequest request, DateTime nowUtc)
        {
            HookEvent hookEvent = HookEventParser.Parse(request.Body);
            if (hookEvent == null)
            {
                AppLog.Append(LogPrefix + "unparsed event, " + request.Body.Length + " chars");
                return false;
            }
            var process = request.Context as ProcessIdentity;
            bool changed = tracker.Apply(hookEvent, process, nowUtc);
            AppLog.Append(LogPrefix + hookEvent.EventName + " " + (hookEvent.NotificationType ?? hookEvent.ToolName ?? "")
                + (hookEvent.EventName == "Stop" ? "bg=" + hookEvent.BackgroundTaskCount : "")
                + " session=" + ShortId(hookEvent.SessionId)
                + " pid=" + (process == null ? "?" : process.ProcessId.ToString())
                + (hookEvent.AgentId == null ? "" : " agent=" + ShortId(hookEvent.AgentId))
                + " session:" + tracker.StatusOf(hookEvent.SessionId)
                + " -> " + tracker.TopStatus);
            Save();
            return changed;
        }

        public List<ThreadInfo> Threads()
        {
            var threads = new List<ThreadInfo>();
            foreach (KeyValuePair<string, SessionState> pair in tracker.Snapshot())
            {
                var thread = new ThreadInfo();
                thread.Source = DisplayName;
                thread.Project = pair.Value.Project;
                thread.IdTail = Tail(pair.Key);
                thread.Status = pair.Value.Status;
                threads.Add(thread);
            }
            return threads;
        }

        public bool CheckLiveness(DateTime nowUtc)
        {
            bool changed = tracker.RemoveEndedProcesses();
            if (changed) AppLog.Append(LogPrefix + "process ended -> " + tracker.TopStatus);
            if (tracker.PromoteApprovedPermissions(nowUtc))
            {
                AppLog.Append(LogPrefix + "permission approved, tool started -> " + tracker.TopStatus);
                changed = true;
            }
            if (changed) Save();
            return changed;
        }

        public bool Housekeep(DateTime nowUtc, AppSettings settings)
        {
            bool changed = tracker.Expire(nowUtc, settings.WorkingTimeoutMinutes, settings.DoneTimeoutMinutes);
            if (changed)
            {
                AppLog.Append(LogPrefix + "timeouts -> " + tracker.TopStatus);
                Save();
            }
            return changed;
        }

        public void Clear()
        {
            tracker.Clear();
        }

        public void Load()
        {
            tracker.Restore(SessionStore.Load());
        }

        public void Save()
        {
            SessionStore.Save(tracker.Snapshot());
        }

        private static ProcessIdentity FindClaudeProcess(System.Net.IPEndPoint clientEndPoint, int port)
        {
            int clientProcessId = TcpConnectionOwner.FindClientProcess(clientEndPoint, port);
            if (clientProcessId == 0) return null;
            int claudeProcessId = ProcessTree.FindClaudeAncestor(clientProcessId);
            return claudeProcessId == 0 ? null : ProcessIdentity.TryCapture(claudeProcessId);
        }

        private static string ShortId(string value)
        {
            return value.Length > 8 ? value.Substring(0, 8) : value;
        }

        private static string Tail(string value)
        {
            return value.Length > 4 ? value.Substring(value.Length - 4) : value;
        }
    }
}
