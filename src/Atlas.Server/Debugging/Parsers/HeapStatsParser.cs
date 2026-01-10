using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging.Parsers;

/// <summary>
/// Parses the output of !dumpheap -stat command.
/// </summary>
public static class HeapStatsParser
{
    public static HeapStatsResult Parse(string output)
    {
        var result = new HeapStatsResult
        {
            RawOutput = output
        };

        // Parse the statistics table
        // Format: MT    Count    TotalSize Class Name
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inStatsSection = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Detect start of stats section
            if (trimmedLine.Contains("MT") && trimmedLine.Contains("Count") && trimmedLine.Contains("TotalSize"))
            {
                inStatsSection = true;
                continue;
            }

            // Detect end / total line
            if (trimmedLine.StartsWith("Total ") || trimmedLine.Contains("objects"))
            {
                var totalMatch = Regex.Match(trimmedLine, @"Total\s+(\d+)\s+objects", RegexOptions.IgnoreCase);
                if (totalMatch.Success)
                {
                    result.TotalObjects = long.Parse(totalMatch.Groups[1].Value);
                }
                continue;
            }

            if (!inStatsSection) continue;

            // Parse stat line: 00007ff123456789    1234    56789 System.String
            var match = Regex.Match(trimmedLine, @"([0-9a-f]+)\s+(\d+)\s+(\d+)\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                result.TypeStats.Add(new TypeStat
                {
                    MethodTable = match.Groups[1].Value,
                    Count = long.Parse(match.Groups[2].Value),
                    TotalSize = long.Parse(match.Groups[3].Value),
                    TypeName = match.Groups[4].Value.Trim()
                });
            }
        }

        // Calculate totals
        result.TotalObjects = result.TypeStats.Sum(t => t.Count);
        result.TotalBytes = result.TypeStats.Sum(t => t.TotalSize);

        // Sort by total size descending
        result.TypeStats = result.TypeStats
            .OrderByDescending(t => t.TotalSize)
            .ToList();

        return result;
    }
}

public class HeapStatsResult
{
    public long TotalObjects { get; set; }
    public long TotalBytes { get; set; }
    public List<TypeStat> TypeStats { get; set; } = new();
    public string RawOutput { get; set; } = "";
}

public class TypeStat
{
    public string MethodTable { get; set; } = "";
    public long Count { get; set; }
    public long TotalSize { get; set; }
    public string TypeName { get; set; } = "";
}
