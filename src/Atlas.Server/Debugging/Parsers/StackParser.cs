using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging.Parsers;

/// <summary>
/// Parses the output of stack trace commands (!clrstack, k, etc.)
/// </summary>
public static class StackParser
{
    /// <summary>
    /// Parse !clrstack output (managed stack)
    /// </summary>
    public static StackTraceResult ParseClrStack(string output)
    {
        var result = new StackTraceResult
        {
            RawOutput = output,
            StackType = "Managed (.NET)"
        };

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inStack = false;
        var frameIndex = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Detect OS Thread Id header
            var threadMatch = Regex.Match(trimmedLine, @"OS Thread Id:\s*0x([0-9a-f]+)", RegexOptions.IgnoreCase);
            if (threadMatch.Success)
            {
                result.ThreadId = threadMatch.Groups[1].Value;
                continue;
            }

            // Detect column header
            if (trimmedLine.Contains("SP") && trimmedLine.Contains("IP") && trimmedLine.Contains("Function"))
            {
                inStack = true;
                continue;
            }

            if (!inStack) continue;

            // Parse frame: SP               IP               Function
            // 000000123456789 0000001234567ab MyApp.Program.Main()
            var frameMatch = Regex.Match(trimmedLine, @"([0-9a-f]+)\s+([0-9a-f]+)\s+(.+)", RegexOptions.IgnoreCase);
            if (frameMatch.Success)
            {
                var function = frameMatch.Groups[3].Value.Trim();
                
                // Try to extract method signature parts
                var methodMatch = Regex.Match(function, @"(.+?)\.([^.]+)\((.*?)\)");
                
                result.Frames.Add(new StackTraceFrame
                {
                    Index = frameIndex++,
                    StackPointer = "0x" + frameMatch.Groups[1].Value,
                    InstructionPointer = "0x" + frameMatch.Groups[2].Value,
                    FullName = function,
                    TypeName = methodMatch.Success ? methodMatch.Groups[1].Value : null,
                    MethodName = methodMatch.Success ? methodMatch.Groups[2].Value : function,
                    Parameters = methodMatch.Success ? methodMatch.Groups[3].Value : null
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Parse k/kp/kv output (native stack)
    /// </summary>
    public static StackTraceResult ParseNativeStack(string output)
    {
        var result = new StackTraceResult
        {
            RawOutput = output,
            StackType = "Native"
        };

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var frameIndex = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip header
            if (trimmedLine.Contains("Child-SP") || trimmedLine.Contains("RetAddr") || 
                string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Parse: Child-SP          RetAddr           Call Site
            // 00000000`12345678 00000000`abcdefgh module!function+0x42
            var frameMatch = Regex.Match(trimmedLine, 
                @"([0-9a-f`]+)\s+([0-9a-f`]+)\s+(.+)", 
                RegexOptions.IgnoreCase);

            if (frameMatch.Success)
            {
                var callSite = frameMatch.Groups[3].Value.Trim();
                
                // Parse module!function+offset
                var symbolMatch = Regex.Match(callSite, @"(.+?)!(.+?)(?:\+(.+))?$");

                result.Frames.Add(new StackTraceFrame
                {
                    Index = frameIndex++,
                    StackPointer = "0x" + frameMatch.Groups[1].Value.Replace("`", ""),
                    ReturnAddress = "0x" + frameMatch.Groups[2].Value.Replace("`", ""),
                    FullName = callSite,
                    Module = symbolMatch.Success ? symbolMatch.Groups[1].Value : null,
                    MethodName = symbolMatch.Success ? symbolMatch.Groups[2].Value : callSite,
                    Offset = symbolMatch.Success && symbolMatch.Groups[3].Success 
                        ? symbolMatch.Groups[3].Value : null
                });
            }
        }

        return result;
    }
}

public class StackTraceResult
{
    public string? ThreadId { get; set; }
    public string StackType { get; set; } = "Unknown";
    public List<StackTraceFrame> Frames { get; set; } = new();
    public string RawOutput { get; set; } = "";
}

public class StackTraceFrame
{
    public int Index { get; set; }
    public string? StackPointer { get; set; }
    public string? InstructionPointer { get; set; }
    public string? ReturnAddress { get; set; }
    public string FullName { get; set; } = "";
    public string? Module { get; set; }
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    public string? Parameters { get; set; }
    public string? Offset { get; set; }
}
