using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeGlow
{
    internal static class SessionStore
    {
        private const char FieldSeparator = '\t';
        private const int FieldCount = 7;

        private static readonly string FilePath = UserDataPaths.File("sessions.txt");

        public static void Save(List<KeyValuePair<string, SessionState>> sessions)
        {
            var content = new StringBuilder();
            foreach (KeyValuePair<string, SessionState> pair in sessions)
            {
                SessionState session = pair.Value;
                content.Append(pair.Key).Append(FieldSeparator)
                    .Append(session.WorkStatus).Append(FieldSeparator)
                    .Append(session.Project.Replace(FieldSeparator, ' ')).Append(FieldSeparator)
                    .Append(session.Process == null ? 0 : session.Process.ProcessId).Append(FieldSeparator)
                    .Append(session.Process == null ? 0 : session.Process.StartTimeUtc.Ticks).Append(FieldSeparator)
                    .Append(session.LastEventUtc.Ticks).Append(FieldSeparator)
                    .Append(session.StatusSinceUtc.Ticks).Append(Environment.NewLine);
            }
            try
            {
                File.WriteAllText(FilePath, content.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static List<KeyValuePair<string, SessionState>> Load()
        {
            var sessions = new List<KeyValuePair<string, SessionState>>();
            if (!File.Exists(FilePath)) return sessions;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            catch (IOException)
            {
                return sessions;
            }
            foreach (string line in lines)
            {
                SessionState session;
                string[] fields = line.Split(FieldSeparator);
                if (fields.Length != FieldCount || !TryParse(fields, out session)) continue;
                if (session.Process != null && !session.Process.IsAlive()) continue;
                sessions.Add(new KeyValuePair<string, SessionState>(fields[0], session));
            }
            return sessions;
        }

        private static bool TryParse(string[] fields, out SessionState session)
        {
            session = null;
            GlowStatus status;
            int processId;
            long processStart;
            long lastEvent;
            long statusSince;
            if (!Enum.TryParse(fields[1], out status)) return false;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)) return false;
            if (!long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out processStart)) return false;
            if (!long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out lastEvent)) return false;
            if (!long.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out statusSince)) return false;
            session = new SessionState();
            bool attention = status == GlowStatus.Permission || status == GlowStatus.Question;
            session.WorkStatus = attention ? GlowStatus.Working : status;
            session.Project = fields[2];
            session.Process = processId == 0 ? null : new ProcessIdentity(processId, new DateTime(processStart, DateTimeKind.Utc));
            session.LastEventUtc = new DateTime(lastEvent, DateTimeKind.Utc);
            session.StatusSinceUtc = new DateTime(statusSince, DateTimeKind.Utc);
            return true;
        }
    }
}
