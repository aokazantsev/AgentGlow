using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ClaudeGlow
{
    internal static class HookEventParser
    {
        private const int SerializerRecursionLimit = 1000;

        public static HookEvent Parse(string json)
        {
            Dictionary<string, object> root = TryDeserialize(json);
            var hookEvent = new HookEvent();
            hookEvent.SessionId = Field(root, json, "session_id");
            hookEvent.EventName = Field(root, json, "hook_event_name");
            hookEvent.NotificationType = Field(root, json, "notification_type");
            hookEvent.Message = Field(root, json, "message");
            hookEvent.ToolName = Field(root, json, "tool_name");
            hookEvent.Cwd = Field(root, json, "cwd");
            hookEvent.AgentId = Field(root, json, "agent_id");
            hookEvent.BackgroundTaskCount = ListLength(root, "background_tasks");
            if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.EventName)) return null;
            return hookEvent;
        }

        private static Dictionary<string, object> TryDeserialize(string json)
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = SerializerRecursionLimit;
            try
            {
                return serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static int ListLength(Dictionary<string, object> root, string name)
        {
            object value;
            if (root == null || !root.TryGetValue(name, out value)) return 0;
            var list = value as System.Collections.ICollection;
            return list == null ? 0 : list.Count;
        }

        private static string Field(Dictionary<string, object> root, string json, string name)
        {
            if (root != null)
            {
                object value;
                return root.TryGetValue(name, out value) ? value as string : null;
            }
            Match match = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return null;
            try
            {
                return Regex.Unescape(match.Groups[1].Value);
            }
            catch (ArgumentException)
            {
                return match.Groups[1].Value;
            }
        }
    }
}
