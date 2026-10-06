using System;
using System.ComponentModel;
using System.Diagnostics;

namespace ClaudeGlow
{
    internal sealed class ProcessIdentity
    {
        public readonly int ProcessId;
        public readonly DateTime StartTimeUtc;

        public ProcessIdentity(int processId, DateTime startTimeUtc)
        {
            ProcessId = processId;
            StartTimeUtc = startTimeUtc;
        }

        public static ProcessIdentity TryCapture(int processId)
        {
            DateTime startTimeUtc;
            return TryReadStartTime(processId, out startTimeUtc) ? new ProcessIdentity(processId, startTimeUtc) : null;
        }

        public bool IsAlive()
        {
            DateTime startTimeUtc;
            return TryReadStartTime(ProcessId, out startTimeUtc) && startTimeUtc == StartTimeUtc;
        }

        private static bool TryReadStartTime(int processId, out DateTime startTimeUtc)
        {
            startTimeUtc = DateTime.MinValue;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return false;
                    startTimeUtc = process.StartTime.ToUniversalTime();
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
