using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgentGlow
{
    internal sealed class StatusHistory
    {
        private const long MaxBytes = 4 * 1024 * 1024;
        private const string Absent = "none";
        private const string Removed = "gone";

        private static readonly string FilePath = UserDataPaths.File("status-history.txt");
        private static readonly string PreviousFilePath = UserDataPaths.File("status-history.old.txt");

        private readonly Dictionary<string, GlowStatus> threads = new Dictionary<string, GlowStatus>();
        private string shown = Absent;

        public DateTimeOffset? Stamp;
        private DateTimeOffset lastWritten = DateTimeOffset.MinValue;

        public void Record(List<ThreadInfo> current, string currentShown, string cause)
        {
            var seen = new HashSet<string>();
            foreach (ThreadInfo thread in current)
            {
                if (string.IsNullOrEmpty(thread.Key) || !seen.Add(thread.Key)) continue;
                GlowStatus previous;
                bool known = threads.TryGetValue(thread.Key, out previous);
                if (known && previous == thread.Status) continue;
                threads[thread.Key] = thread.Status;
                Write("thread", thread.Key, known ? previous.ToString() : Absent, thread.Status.ToString(), cause);
            }
            foreach (string key in new List<string>(threads.Keys))
            {
                if (seen.Contains(key)) continue;
                Write("thread", key, threads[key].ToString(), Removed, cause);
                threads.Remove(key);
            }
            if (currentShown == shown) return;
            Write("shown", "", shown, currentShown, cause);
            shown = currentShown;
        }

        public void Event(string key, string cause)
        {
            Write("event", key, "", "", cause);
        }

        public void Mark(string text)
        {
            Write("mark", "", "", "", text);
        }

        private void Write(string kind, string key, string from, string to, string cause)
        {
            DateTimeOffset moment = Stamp ?? DateTimeOffset.Now;
            if (moment < lastWritten) moment = lastWritten;
            lastWritten = moment;
            AppLog.AppendTo(FilePath, PreviousFilePath, MaxBytes,
                moment.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture) + "\t" + kind + "\t" + key + "\t" + from + "\t" + to + "\t" + cause);
        }
    }
}
