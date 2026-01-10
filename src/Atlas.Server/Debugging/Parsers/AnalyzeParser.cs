using System.Text.RegularExpressions;

namespace Atlas.Server.Debugging.Parsers;

/// <summary>
/// Parses the output of !analyze -v command.
/// </summary>
public static class AnalyzeParser
{
    public static AnalyzeResult Parse(string output)
    {
        var result = new AnalyzeResult
        {
            RawOutput = output
        };

        // Extract bug check / exception code
        var bugCheckMatch = Regex.Match(output, @"BugCheck\s+([A-Fx0-9]+),\s*\{(.+?)\}", RegexOptions.IgnoreCase);
        if (bugCheckMatch.Success)
        {
            result.BugCheckCode = bugCheckMatch.Groups[1].Value;
            result.BugCheckParameters = bugCheckMatch.Groups[2].Value;
        }

        // Extract exception code (for user-mode dumps)
        var exceptionMatch = Regex.Match(output, @"ExceptionCode:\s*([A-Fx0-9]+)(?:\s*\((.+?)\))?", RegexOptions.IgnoreCase);
        if (exceptionMatch.Success)
        {
            result.ExceptionCode = exceptionMatch.Groups[1].Value;
            result.ExceptionDescription = exceptionMatch.Groups[2].Success ? exceptionMatch.Groups[2].Value : null;
        }

        // Alternative exception format
        var exceptionAltMatch = Regex.Match(output, @"EXCEPTION_CODE:\s*\(([A-Z_]+)\)\s*([A-Fx0-9]+)", RegexOptions.IgnoreCase);
        if (exceptionAltMatch.Success)
        {
            result.ExceptionDescription ??= exceptionAltMatch.Groups[1].Value;
            result.ExceptionCode ??= exceptionAltMatch.Groups[2].Value;
        }

        // Extract faulting module
        var moduleMatch = Regex.Match(output, @"(?:FAULTING_MODULE|MODULE_NAME):\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (moduleMatch.Success)
        {
            result.FaultingModule = moduleMatch.Groups[1].Value.Trim();
        }

        // Extract faulting IP/function
        var faultingIpMatch = Regex.Match(output, @"FAULTING_IP:\s*\r?\n(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (faultingIpMatch.Success)
        {
            result.FaultingInstruction = faultingIpMatch.Groups[1].Value.Trim();
        }

        // Extract symbol name
        var symbolMatch = Regex.Match(output, @"SYMBOL_NAME:\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (symbolMatch.Success)
        {
            result.FaultingSymbol = symbolMatch.Groups[1].Value.Trim();
        }

        // Extract image name
        var imageMatch = Regex.Match(output, @"IMAGE_NAME:\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (imageMatch.Success)
        {
            result.FaultingImage = imageMatch.Groups[1].Value.Trim();
        }

        // Extract process name
        var processMatch = Regex.Match(output, @"PROCESS_NAME:\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (processMatch.Success)
        {
            result.ProcessName = processMatch.Groups[1].Value.Trim();
        }

        // Extract failure bucket
        var bucketMatch = Regex.Match(output, @"FAILURE_BUCKET_ID:\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        if (bucketMatch.Success)
        {
            result.FailureBucket = bucketMatch.Groups[1].Value.Trim();
        }

        // Extract stack trace
        result.StackFrames = ParseStackTrace(output);

        // Determine crash type
        result.CrashType = DetermineCrashType(result);

        return result;
    }

    private static List<StackFrame> ParseStackTrace(string output)
    {
        var frames = new List<StackFrame>();

        // Look for STACK_TEXT or similar section
        var stackSection = Regex.Match(output, @"STACK_TEXT:\s*\r?\n((?:.+\r?\n)+?)(?:\r?\n\r?\n|SYMBOL_NAME)", RegexOptions.IgnoreCase);
        if (!stackSection.Success)
        {
            // Try alternate format
            stackSection = Regex.Match(output, @"Child-SP\s+RetAddr.*?\r?\n((?:[0-9a-f`]+.+\r?\n)+)", RegexOptions.IgnoreCase);
        }

        if (stackSection.Success)
        {
            var lines = stackSection.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var frameIndex = 0;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                // Parse stack frame: RetAddr Module!Function+Offset
                var frameMatch = Regex.Match(trimmedLine, @"([0-9a-f`]+)\s+([0-9a-f`]+)\s+(.+)", RegexOptions.IgnoreCase);
                if (frameMatch.Success)
                {
                    var symbolPart = frameMatch.Groups[3].Value.Trim();
                    var moduleFuncMatch = Regex.Match(symbolPart, @"(.+?)!(.+?)(?:\+(.+))?$");

                    frames.Add(new StackFrame
                    {
                        Index = frameIndex++,
                        ReturnAddress = frameMatch.Groups[2].Value,
                        Module = moduleFuncMatch.Success ? moduleFuncMatch.Groups[1].Value : null,
                        Function = moduleFuncMatch.Success ? moduleFuncMatch.Groups[2].Value : symbolPart,
                        Offset = moduleFuncMatch.Success && moduleFuncMatch.Groups[3].Success 
                            ? moduleFuncMatch.Groups[3].Value : null
                    });
                }
            }
        }

        return frames;
    }

    private static string DetermineCrashType(AnalyzeResult result)
    {
        if (!string.IsNullOrEmpty(result.ExceptionCode))
        {
            return result.ExceptionCode.ToUpperInvariant() switch
            {
                "C0000005" or "0XC0000005" => "Access Violation",
                "C00000FD" or "0XC00000FD" => "Stack Overflow",
                "C0000094" or "0XC0000094" => "Integer Divide by Zero",
                "C0000096" or "0XC0000096" => "Privileged Instruction",
                "80000003" or "0X80000003" => "Breakpoint",
                "E0434352" or "0XE0434352" => ".NET CLR Exception",
                "E06D7363" or "0XE06D7363" => "C++ Exception",
                _ => "Exception"
            };
        }

        if (!string.IsNullOrEmpty(result.BugCheckCode))
        {
            return "Kernel Bug Check (BSOD)";
        }

        return "Unknown";
    }
}

public class AnalyzeResult
{
    public string? BugCheckCode { get; set; }
    public string? BugCheckParameters { get; set; }
    public string? ExceptionCode { get; set; }
    public string? ExceptionDescription { get; set; }
    public string? FaultingModule { get; set; }
    public string? FaultingInstruction { get; set; }
    public string? FaultingSymbol { get; set; }
    public string? FaultingImage { get; set; }
    public string? ProcessName { get; set; }
    public string? FailureBucket { get; set; }
    public string CrashType { get; set; } = "Unknown";
    public List<StackFrame> StackFrames { get; set; } = new();
    public string RawOutput { get; set; } = "";
}

public class StackFrame
{
    public int Index { get; set; }
    public string? ReturnAddress { get; set; }
    public string? Module { get; set; }
    public string? Function { get; set; }
    public string? Offset { get; set; }
}
