using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging.Parsers;

/// <summary>
/// Parses the output of lm (list modules) command.
/// </summary>
public static class ModuleParser
{
    public static ModuleListResult Parse(string output)
    {
        var result = new ModuleListResult
        {
            RawOutput = output
        };

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip header lines
            if (trimmedLine.StartsWith("start") || string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Format: start    end        module name
            // 00007ff6`12340000 00007ff6`12350000   MyApp      (deferred)
            var match = Regex.Match(trimmedLine, 
                @"([0-9a-f`]+)\s+([0-9a-f`]+)\s+(\S+)(?:\s+(.+))?", 
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var startAddr = match.Groups[1].Value.Replace("`", "");
                var endAddr = match.Groups[2].Value.Replace("`", "");

                long start = 0, end = 0;
                try
                {
                    start = Convert.ToInt64(startAddr, 16);
                    end = Convert.ToInt64(endAddr, 16);
                }
                catch { }

                result.Modules.Add(new ModuleInfo
                {
                    StartAddress = "0x" + startAddr,
                    EndAddress = "0x" + endAddr,
                    Name = match.Groups[3].Value,
                    Status = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null,
                    Size = end - start
                });
            }
        }

        // Sort by start address
        result.Modules = result.Modules
            .OrderBy(m => m.StartAddress)
            .ToList();

        return result;
    }
}

public class ModuleListResult
{
    public List<ModuleInfo> Modules { get; set; } = new();
    public string RawOutput { get; set; } = "";
}

public class ModuleInfo
{
    public string StartAddress { get; set; } = "";
    public string EndAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Status { get; set; }
    public long Size { get; set; }
}
