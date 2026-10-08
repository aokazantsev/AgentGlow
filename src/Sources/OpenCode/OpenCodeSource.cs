using System;
using System.Collections.Generic;

namespace AgentGlow.Sources.OpenCode
{
    internal sealed class OpenCodeSource : IAgentSource
    {
        private const string LogPrefix = "opencode: ";

        private readonly SessionTracker tracker = new SessionTracker();
        private readonly IIntegration integration = new OpenCodeIntegration();

        public string Id
        {
            get { return "opencode"; }
        }

        public string DisplayName
        {
            get { return "OpenCode"; }
        }

        public string Route
        {
            get { return "/opencode"; }
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

        public void OnReceive(EventRequest request)
        {
        }

        public bool Handle(EventRequest request, DateTime nowUtc)
        {
            GlowEvent glowEvent = GlowEventParser.Parse(request.Body);
            if (glowEvent == null)
            {
                AppLog.Append(LogPrefix + "unparsed event, " + request.Body.Length + " chars");
                return false;
            }
            if (glowEvent.Kind == GlowEventKinds.Hello)
            {
                AppLog.Append(LogPrefix + "plugin connected, pid=" + glowEvent.ProcessId + ", plugin version=" + glowEvent.PluginVersion
                    + ", project=" + ProjectOf(glowEvent.Directory)
                    + (glowEvent.PluginVersion < OpenCodePlugin.CurrentVersion ? " (outdated)" : ""));
                return false;
            }
            bool changed = tracker.Apply(glowEvent, nowUtc);
            if (glowEvent.Kind != GlowEventKinds.Activity)
            {
                AppLog.Append(LogPrefix + glowEvent.Kind
                    + " session=" + ShortId(glowEvent.SessionId ?? "-")
                    + (glowEvent.ParentId == null ? "" : " parent=" + ShortId(glowEvent.ParentId))
                    + " pid=" + glowEvent.ProcessId
                    + (glowEvent.RequestId == null ? "" : " request=" + ShortId(glowEvent.RequestId))
                    + (glowEvent.ErrorName == null ? "" : " error=" + glowEvent.ErrorName)
                    + " session:" + tracker.StatusOf(glowEvent.SessionId ?? "")
                    + " -> " + tracker.TopStatus);
                Save();
            }
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
            if (changed)
            {
                AppLog.Append(LogPrefix + "process ended -> " + tracker.TopStatus);
                Save();
            }
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

        private static string ProjectOf(string directory)
        {
            if (directory == null) return "?";
            return System.IO.Path.GetFileName(directory.TrimEnd('\\', '/'));
        }

        private static string ShortId(string value)
        {
            return value.Length > 8 ? value.Substring(value.Length - 8) : value;
        }

        private static string Tail(string value)
        {
            return value.Length > 4 ? value.Substring(value.Length - 4) : value;
        }
    }
}
