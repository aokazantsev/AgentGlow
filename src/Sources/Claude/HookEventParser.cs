using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AgentGlow.Sources.Claude
{
    internal static class HookEventParser
    {
        private const int SerializerRecursionLimit = 1000;
        private const string ShellTool = "Bash";
        internal const string TurnWrapUpTool = "mcp__ccd_turn__wrap_up";
        private const int MaxIdLength = 128;
        private const int MaxNameLength = 256;
        private const int MaxPathLength = 1024;
        private static readonly string[] AutomaticPromptPrefixes =
        {
            "<task-notification>",
            "<agent-message ",
            "<side-sessions-event>",
            "<ci-monitor-event>"
        };
        private const string ReminderStart = "<system-reminder>";
        private const string ReminderEnd = "</system-reminder>";

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
            hookEvent.CallKey = CallKey(root, hookEvent);
            hookEvent.ShellCommand = ShellCommand(root, hookEvent);
            hookEvent.AwaitsUserReply = AwaitsUserReply(root, hookEvent);
            hookEvent.IsAutomaticWake = hookEvent.EventName == "UserPromptSubmit" && IsAutomaticPrompt(Field(root, json, "prompt"));
            if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.EventName)) return null;
            if (!IsPlain(hookEvent.SessionId, MaxIdLength) || !IsPlain(hookEvent.EventName, MaxNameLength)) return null;
            if (hookEvent.AgentId != null && !IsPlain(hookEvent.AgentId, MaxIdLength)) return null;
            if (hookEvent.ToolName != null && !IsPlain(hookEvent.ToolName, MaxNameLength)) return null;
            if (hookEvent.Cwd != null && !IsPlain(hookEvent.Cwd, MaxPathLength)) hookEvent.Cwd = null;
            if (hookEvent.NotificationType != null && !IsPlain(hookEvent.NotificationType, MaxNameLength)) hookEvent.NotificationType = null;
            return hookEvent;
        }

        private static bool IsPlain(string value, int maxLength)
        {
            if (value.Length > maxLength) return false;
            foreach (char c in value)
            {
                if (char.IsControl(c)) return false;
            }
            return true;
        }

        private static string CallKey(Dictionary<string, object> root, HookEvent hookEvent)
        {
            object toolInput;
            if (root == null || hookEvent.ToolName == null || !root.TryGetValue("tool_input", out toolInput)) return null;
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = SerializerRecursionLimit;
            return (hookEvent.AgentId ?? "") + "\n" + hookEvent.ToolName + "\n" + serializer.Serialize(toolInput);
        }

        private static string ShellCommand(Dictionary<string, object> root, HookEvent hookEvent)
        {
            object command;
            Dictionary<string, object> input = ToolInput(root, hookEvent, ShellTool);
            return input != null && input.TryGetValue("command", out command) ? command as string : null;
        }

        private static bool AwaitsUserReply(Dictionary<string, object> root, HookEvent hookEvent)
        {
            object blocked;
            object ask;
            Dictionary<string, object> input = ToolInput(root, hookEvent, TurnWrapUpTool);
            if (input == null) return false;
            if (input.TryGetValue("blocked", out blocked) && IsTrue(blocked)) return true;
            return input.TryGetValue("ask", out ask) && !string.IsNullOrWhiteSpace(ask as string);
        }

        private static bool IsAutomaticPrompt(string prompt)
        {
            if (prompt == null) return false;
            if (StartsWithAutomaticPrefix(prompt, 0)) return true;
            if (!prompt.StartsWith(ReminderStart, StringComparison.Ordinal)) return false;
            int end = prompt.IndexOf(ReminderEnd, StringComparison.Ordinal);
            while (end >= 0)
            {
                int position = end + ReminderEnd.Length;
                while (position < prompt.Length && char.IsWhiteSpace(prompt[position])) position++;
                if (StartsWithAutomaticPrefix(prompt, position)) return true;
                end = prompt.IndexOf(ReminderEnd, position, StringComparison.Ordinal);
            }
            return false;
        }

        private static bool StartsWithAutomaticPrefix(string prompt, int start)
        {
            foreach (string prefix in AutomaticPromptPrefixes)
            {
                if (string.CompareOrdinal(prompt, start, prefix, 0, prefix.Length) == 0) return true;
            }
            return false;
        }

        private static Dictionary<string, object> ToolInput(Dictionary<string, object> root, HookEvent hookEvent, string toolName)
        {
            object toolInput;
            if (root == null || hookEvent.ToolName != toolName || !root.TryGetValue("tool_input", out toolInput)) return null;
            return toolInput as Dictionary<string, object>;
        }

        private static bool IsTrue(object value)
        {
            if (value is bool) return (bool)value;
            var text = value as string;
            return text != null && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
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
