using System;
using System.Net;
using System.Runtime.InteropServices;

namespace AgentGlow.Sources.Claude
{
    internal static class TcpConnectionOwner
    {
        private const int AddressFamilyInet = 2;
        private const int TableOwnerPidConnections = 4;
        private const uint NoError = 0;
        private const uint InsufficientBuffer = 122;
        private const int RowSize = 24;

        public static int FindClientProcess(IPEndPoint client, int serverPort)
        {
            int size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AddressFamilyInet, TableOwnerPidConnections, 0);
            if (result != InsufficientBuffer) return 0;
            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(table, ref size, false, AddressFamilyInet, TableOwnerPidConnections, 0);
                if (result != NoError) return 0;
                int rowCount = Marshal.ReadInt32(table);
                for (int i = 0; i < rowCount; i++)
                {
                    IntPtr row = IntPtr.Add(table, 4 + i * RowSize);
                    int localPort = NetworkPort(Marshal.ReadInt32(row, 8));
                    int remotePort = NetworkPort(Marshal.ReadInt32(row, 16));
                    if (localPort == client.Port && remotePort == serverPort) return Marshal.ReadInt32(row, 20);
                }
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        private static int NetworkPort(int raw)
        {
            return IPAddress.NetworkToHostOrder((short)(raw & 0xFFFF)) & 0xFFFF;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, uint reserved);
    }
}
