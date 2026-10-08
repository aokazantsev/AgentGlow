using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgentGlow.Sources.Claude
{
    internal static class ProcessTree
    {
        private const uint SnapshotProcesses = 0x00000002;
        private const int MaxDepth = 16;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private static readonly string[] ClaudeExecutables = { "claude.exe", "node.exe" };
        private static readonly string[] ShellExecutables = { "bash.exe", "sh.exe", "powershell.exe", "pwsh.exe", "cmd.exe" };

        public static int FindClaudeAncestor(int processId)
        {
            Dictionary<int, ProcessEntry> entries = Snapshot();
            int current = processId;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                ProcessEntry entry;
                if (!entries.TryGetValue(current, out entry)) return 0;
                if (IsClaude(entry.ExeFile)) return current;
                if (entry.ParentProcessId == 0 || entry.ParentProcessId == current) return 0;
                current = entry.ParentProcessId;
            }
            return 0;
        }

        public static bool HasChildStartedAfter(int processId, DateTime sinceUtc)
        {
            foreach (KeyValuePair<int, ProcessEntry> pair in Snapshot())
            {
                if (pair.Value.ParentProcessId != processId || !IsShell(pair.Value.ExeFile)) continue;
                ProcessIdentity child = ProcessIdentity.TryCapture(pair.Key);
                if (child != null && child.StartTimeUtc > sinceUtc) return true;
            }
            return false;
        }

        private static bool IsClaude(string exeFile)
        {
            return IsOneOf(exeFile, ClaudeExecutables);
        }

        private static bool IsShell(string exeFile)
        {
            return IsOneOf(exeFile, ShellExecutables);
        }

        private static bool IsOneOf(string exeFile, string[] names)
        {
            foreach (string name in names)
            {
                if (string.Equals(exeFile, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static Dictionary<int, ProcessEntry> Snapshot()
        {
            var entries = new Dictionary<int, ProcessEntry>();
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandle) return entries;
            try
            {
                var entry = new ProcessEntry32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32));
                bool hasEntry = Process32First(snapshot, ref entry);
                while (hasEntry)
                {
                    entries[(int)entry.th32ProcessID] = new ProcessEntry((int)entry.th32ParentProcessID, entry.szExeFile);
                    hasEntry = Process32Next(snapshot, ref entry);
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return entries;
        }

        private struct ProcessEntry
        {
            public readonly int ParentProcessId;
            public readonly string ExeFile;

            public ProcessEntry(int parentProcessId, string exeFile)
            {
                ParentProcessId = parentProcessId;
                ExeFile = exeFile;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
