using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace ClaudeGlow
{
    internal static class JsonText
    {
        private const string Indent = "  ";

        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var parsed = serializer.DeserializeObject(text) as Dictionary<string, object>;
            if (parsed == null) throw new FormatException("в корне не объект");
            return parsed;
        }

        public static string Write(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value, int depth)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }
            var text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }
            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                WriteObject(builder, map, depth);
                return;
            }
            var list = value as IList;
            if (list != null)
            {
                WriteArray(builder, list, depth);
                return;
            }
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object> map, int depth)
        {
            if (map.Count == 0)
            {
                builder.Append("{}");
                return;
            }
            builder.Append("{\n");
            int index = 0;
            foreach (KeyValuePair<string, object> pair in map)
            {
                AppendIndent(builder, depth + 1);
                WriteString(builder, pair.Key);
                builder.Append(": ");
                WriteValue(builder, pair.Value, depth + 1);
                builder.Append(++index < map.Count ? ",\n" : "\n");
            }
            AppendIndent(builder, depth);
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IList list, int depth)
        {
            if (list.Count == 0)
            {
                builder.Append("[]");
                return;
            }
            builder.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                AppendIndent(builder, depth + 1);
                WriteValue(builder, list[i], depth + 1);
                builder.Append(i + 1 < list.Count ? ",\n" : "\n");
            }
            AppendIndent(builder, depth);
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                if (c == '"') builder.Append("\\\"");
                else if (c == '\\') builder.Append("\\\\");
                else if (c == '\n') builder.Append("\\n");
                else if (c == '\r') builder.Append("\\r");
                else if (c == '\t') builder.Append("\\t");
                else if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                else builder.Append(c);
            }
            builder.Append('"');
        }

        private static void AppendIndent(StringBuilder builder, int depth)
        {
            for (int i = 0; i < depth; i++) builder.Append(Indent);
        }
    }
}
