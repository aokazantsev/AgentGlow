using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace AgentGlow.Sources.OpenCode
{
    internal sealed class ServerProcessWatch : IDisposable
    {
        private const int ConsoleCtrlExit = unchecked((int)0xC000013A);

        private readonly Dictionary<int, Process> watched = new Dictionary<int, Process>();

        public int Count
        {
            get { return watched.Count; }
        }

        public void Watch(int processId)
        {
            if (processId <= 0 || watched.ContainsKey(processId)) return;
            try
            {
                Process process = Process.GetProcessById(processId);
                IntPtr handle = process.Handle;
                if (handle == IntPtr.Zero || process.HasExited)
                {
                    process.Dispose();
                    return;
                }
                watched[processId] = process;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        public List<KeyValuePair<int, int>> TakeExited()
        {
            var exited = new List<KeyValuePair<int, int>>();
            foreach (KeyValuePair<int, Process> pair in new List<KeyValuePair<int, Process>>(watched))
            {
                int exitCode;
                if (!TryGetExitCode(pair.Value, out exitCode)) continue;
                exited.Add(new KeyValuePair<int, int>(pair.Key, exitCode));
                pair.Value.Dispose();
                watched.Remove(pair.Key);
            }
            return exited;
        }

        public static bool IsCrash(int exitCode)
        {
            return exitCode < 0 && exitCode != ConsoleCtrlExit;
        }

        public void Dispose()
        {
            foreach (Process process in watched.Values)
            {
                process.Dispose();
            }
            watched.Clear();
        }

        private static bool TryGetExitCode(Process process, out int exitCode)
        {
            exitCode = 0;
            try
            {
                if (!process.HasExited) return false;
                exitCode = process.ExitCode;
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (Win32Exception)
            {
                return true;
            }
        }
    }
}
