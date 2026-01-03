using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class NetworkTools
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    private static string GetTcpState(uint state) => state switch
    {
        1 => "Closed",
        2 => "Listen",
        3 => "SynSent",
        4 => "SynReceived",
        5 => "Established",
        6 => "FinWait1",
        7 => "FinWait2",
        8 => "CloseWait",
        9 => "Closing",
        10 => "LastAck",
        11 => "TimeWait",
        12 => "DeleteTcb",
        _ => $"Unknown({state})"
    };

    private static string FormatIpAddress(uint addr)
    {
        return new IPAddress(addr).ToString();
    }

    private static ushort ConvertPort(uint port)
    {
        return (ushort)IPAddress.NetworkToHostOrder((short)port);
    }

    private static List<MIB_TCPROW_OWNER_PID> GetTcpTableWithOwnerPid(int tableClass)
    {
        var rows = new List<MIB_TCPROW_OWNER_PID>();
        int bufferSize = 0;

        // First call to get required buffer size
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, tableClass, 0);

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, tableClass, 0);
            if (result != 0)
                return rows;

            // First 4 bytes is the count
            int rowCount = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = tcpTablePtr + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rows.Add(row);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return rows;
    }

    [McpServerTool(Name = "list_network_connections")]
    [Description("List active TCP connections with owning process information")]
    public static object ListNetworkConnections()
    {
        try
        {
            var rows = GetTcpTableWithOwnerPid(TCP_TABLE_OWNER_PID_ALL);

            if (rows.Count == 0)
            {
                return new
                {
                    count = 0,
                    connections = new List<object>(),
                    note = "No active TCP connections found, or insufficient permissions to enumerate."
                };
            }

            var connections = rows
                .Where(r => r.dwState != 2) // Exclude listeners
                .Select(r => new
                {
                    localAddress = FormatIpAddress(r.dwLocalAddr),
                    localPort = ConvertPort(r.dwLocalPort),
                    remoteAddress = FormatIpAddress(r.dwRemoteAddr),
                    remotePort = ConvertPort(r.dwRemotePort),
                    state = GetTcpState(r.dwState),
                    owningPid = (int)r.dwOwningPid
                })
                .ToList();

            return new { count = connections.Count, connections };
        }
        catch (Exception ex)
        {
            return new
            {
                error = "Failed to list network connections",
                details = ex.Message,
                suggestion = "This operation requires access to the TCP/IP stack. Try running as Administrator."
            };
        }
    }

    [McpServerTool(Name = "list_tcp_listeners")]
    [Description("List all TCP ports being listened on with owning process information")]
    public static object ListTcpListeners()
    {
        try
        {
            var rows = GetTcpTableWithOwnerPid(TCP_TABLE_OWNER_PID_LISTENER);

            if (rows.Count == 0)
            {
                return new
                {
                    count = 0,
                    listeners = new List<object>(),
                    note = "No TCP listeners found, or insufficient permissions to enumerate."
                };
            }

            var listeners = rows
                .Select(r => new
                {
                    address = FormatIpAddress(r.dwLocalAddr),
                    port = ConvertPort(r.dwLocalPort),
                    owningPid = (int)r.dwOwningPid
                })
                .OrderBy(l => l.port)
                .ToList();

            return new { count = listeners.Count, listeners };
        }
        catch (Exception ex)
        {
            return new
            {
                error = "Failed to list TCP listeners",
                details = ex.Message,
                suggestion = "This operation requires access to the TCP/IP stack. Try running as Administrator."
            };
        }
    }
}
