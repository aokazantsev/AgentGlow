using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace AgentGlow.Sources.OpenCode
{
    internal static class GlowEventParser
    {
        private const int SerializerRecursionLimit = 100;

        public static GlowEvent Parse(string json)
        {
            Dictionary<string, object> root = TryDeserialize(json);
            if (root == null) return null;
            var glowEvent = new GlowEvent();
            glowEvent.Kind = Text(root, "kind");
            glowEvent.SessionId = Text(root, "sessionId");
            glowEvent.ParentId = Text(root, "parentId");
            glowEvent.Directory = Text(root, "directory");
            glowEvent.RequestId = Text(root, "requestId");
            glowEvent.ErrorName = Text(root, "errorName");
            glowEvent.ProcessId = (int)Number(root, "pid");
            glowEvent.RetryAtMs = Number(root, "retryAt");
            glowEvent.PluginVersion = (int)Number(root, "pluginVersion");
            if (string.IsNullOrEmpty(glowEvent.Kind)) return null;
            return glowEvent;
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

        private static string Text(Dictionary<string, object> root, string name)
        {
            object value;
            string text = root.TryGetValue(name, out value) ? value as string : null;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static long Number(Dictionary<string, object> root, string name)
        {
            object value;
            if (!root.TryGetValue(name, out value) || value == null) return 0;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return 0;
            }
            catch (InvalidCastException)
            {
                return 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }
    }
}
