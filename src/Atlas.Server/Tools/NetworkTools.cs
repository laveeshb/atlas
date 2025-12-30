using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class NetworkTools
{
    [McpServerTool(Name = "list_network_connections")]
    [Description("List active TCP connections with owning process information")]
    public static object ListNetworkConnections()
    {
        var connections = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Select(c => new
            {
                localAddress = c.LocalEndPoint.Address.ToString(),
                localPort = c.LocalEndPoint.Port,
                remoteAddress = c.RemoteEndPoint.Address.ToString(),
                remotePort = c.RemoteEndPoint.Port,
                state = c.State.ToString()
            })
            .ToList();

        return new { count = connections.Count, connections };
    }

    [McpServerTool(Name = "list_tcp_listeners")]
    [Description("List all TCP ports being listened on")]
    public static object ListTcpListeners()
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(l => new
            {
                address = l.Address.ToString(),
                port = l.Port
            })
            .OrderBy(l => l.port)
            .ToList();

        return new { count = listeners.Count, listeners };
    }
}
