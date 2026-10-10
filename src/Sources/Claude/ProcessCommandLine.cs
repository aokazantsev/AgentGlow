using System;
using System.Runtime.InteropServices;

namespace AgentGlow.Sources.Claude
{
    internal static class ProcessCommandLine
    {
        private const uint QueryLimitedInformation = 0x1000;
        private const int CommandLineInformationClass = 60;
        private const int InfoLengthMismatch = unchecked((int)0xC0000004);
        private const int MaxBufferBytes = 1024 * 1024;

        public static string TryRead(int processId)
        {
            IntPtr handle = OpenProcess(QueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero) return null;
            try
            {
                int size;
                int status = NtQueryInformationProcess(handle, CommandLineInformationClass, IntPtr.Zero, 0, out size);
                if (status != InfoLengthMismatch || size <= 0 || size > MaxBufferBytes) return null;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    status = NtQueryInformationProcess(handle, CommandLineInformationClass, buffer, size, out size);
                    if (status != 0) return null;
                    int lengthBytes = Marshal.ReadInt16(buffer) & 0xFFFF;
                    IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, lengthBytes / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
