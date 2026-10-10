using System;
using System.Text;

namespace AgentGlow.Sources.Claude
{
    internal static class CommandFingerprint
    {
        private const int MaxLength = 200;
        private const string ShellCommandMarker = "&& eval ";

        public static string OfCommand(string command)
        {
            if (command == null) return null;
            string fingerprint = Of(command);
            return fingerprint.Length == 0 ? null : fingerprint;
        }

        public static bool ShellRuns(string shellCommandLine, string commandFingerprint)
        {
            if (shellCommandLine == null || commandFingerprint == null) return false;
            int marker = shellCommandLine.IndexOf(ShellCommandMarker, StringComparison.Ordinal);
            if (marker < 0) return false;
            return Of(shellCommandLine.Substring(marker + ShellCommandMarker.Length)).StartsWith(commandFingerprint, StringComparison.Ordinal);
        }

        private static string Of(string text)
        {
            var fingerprint = new StringBuilder(MaxLength);
            foreach (char symbol in text)
            {
                if (fingerprint.Length == MaxLength) break;
                if (char.IsLetterOrDigit(symbol)) fingerprint.Append(char.ToLowerInvariant(symbol));
            }
            return fingerprint.ToString();
        }
    }
}
