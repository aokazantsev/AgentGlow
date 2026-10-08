using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace AgentGlow.Sources.Generic
{
    internal sealed class GenericEvent
    {
        public const string Ended = "ended";

        private const int MaxTextLength = 120;
        private const int SerializerRecursionLimit = 20;

        public string App;
        public string Session;
        public string Status;
        public string Project;
        public int ProcessId;

        public static GenericEvent Parse(string json)
        {
            Dictionary<string, object> root = TryDeserialize(json);
            if (root == null) return null;
            var parsed = new GenericEvent();
            parsed.App = Text(root, "app");
            parsed.Session = Text(root, "session");
            parsed.Status = Text(root, "status");
            string project = Text(root, "project");
            parsed.Project = project == null ? null : ProjectName(project);
            parsed.ProcessId = Number(root, "pid");
            if (parsed.App == null || parsed.Session == null || parsed.Status == null) return null;
            parsed.Status = parsed.Status.ToLowerInvariant();
            return parsed;
        }

        public bool TryGetStatus(out GlowStatus status)
        {
            switch (Status)
            {
                case "working":
                    status = GlowStatus.Working;
                    return true;
                case "done":
                    status = GlowStatus.Done;
                    return true;
                case "question":
                    status = GlowStatus.Question;
                    return true;
                case "permission":
                    status = GlowStatus.Permission;
                    return true;
                case "error":
                    status = GlowStatus.Error;
                    return true;
                default:
                    status = GlowStatus.Idle;
                    return false;
            }
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
            if (string.IsNullOrWhiteSpace(text)) return null;
            var clean = new System.Text.StringBuilder();
            foreach (char symbol in text.Trim())
            {
                clean.Append(char.IsControl(symbol) ? ' ' : symbol);
            }
            text = clean.ToString();
            return text.Length > MaxTextLength ? text.Substring(0, MaxTextLength) : text;
        }

        private static int Number(Dictionary<string, object> root, string name)
        {
            object value;
            if (!root.TryGetValue(name, out value) || value == null) return 0;
            try
            {
                long number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return number > 0 && number <= int.MaxValue ? (int)number : 0;
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

        private static string ProjectName(string directory)
        {
            string trimmed = directory.TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }
}
