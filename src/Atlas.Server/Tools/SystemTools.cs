using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "get_system_info")]
    [Description("Get basic system information")]
    public static object GetSystemInfo()
    {
        return new
        {
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.ToString(),
            processorCount = Environment.ProcessorCount,
            is64Bit = Environment.Is64BitOperatingSystem,
            systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss"),
            dotnetVersion = RuntimeInformation.FrameworkDescription,
            userName = Environment.UserName
        };
    }
}
