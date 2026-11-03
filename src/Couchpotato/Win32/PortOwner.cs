using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Couchpotato.Win32
{
    /// <summary>Which program is listening on a TCP port, to say who's in the way when the port is taken.</summary>
    static class PortOwner
    {
        /// <summary>The name and id of the process listening on <paramref name="port"/> (IPv4), or null if unknown.</summary>
        public static (string Name, int Pid)? Find(int port)
        {
            try
            {
                int size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /* AF_INET */, 3 /* TCP_TABLE_OWNER_PID_LISTENER */, 0);
                var table = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return null;
                    int rows = Marshal.ReadInt32(table);
                    for (int i = 0; i < rows; i++)
                    {
                        // MIB_TCPROW_OWNER_PID: state, local address, local port, remote address, remote port, owning pid.
                        var row = table + 4 + i * 24;
                        int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9); // network byte order
                        if (localPort != port) continue;
                        int pid = Marshal.ReadInt32(row, 20);
                        try
                        {
                            using (var process = Process.GetProcessById(pid)) return (process.ProcessName, pid);
                        }
                        catch (ArgumentException)
                        {
                            return ("process " + pid, pid);
                        }
                    }
                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(table);
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is InvalidOperationException)
            {
                return null;
            }
        }

        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    }
}
