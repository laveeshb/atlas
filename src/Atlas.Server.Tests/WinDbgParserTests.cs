using Atlas.Server.Debugging.Parsers;

namespace Atlas.Server.Tests;

public class WinDbgParserTests
{
    #region AnalyzeParser Tests

    [Fact]
    public void AnalyzeParser_ParsesAccessViolation()
    {
        var output = @"
*******************************************************************************
*                                                                             *
*                        Exception Analysis                                   *
*                                                                             *
*******************************************************************************

EXCEPTION_CODE: (NTSTATUS) 0xc0000005 - The instruction at 0x%p referenced memory at 0x%p. The memory could not be %s.

FAULTING_IP: 
MyApp!ProcessData+42
00007ff6`12345678 488b01          mov     rax,qword ptr [rcx]

EXCEPTION_RECORD:  00000000001234 -- (.exr 0x1234)
ExceptionAddress: 00007ff612345678 (MyApp!ProcessData+0x42)
   ExceptionCode: c0000005 (Access violation)
  ExceptionFlags: 00000000

FAULTING_MODULE: 00007ff6`12340000 MyApp

PROCESS_NAME:  MyApp.exe

MODULE_NAME: MyApp

SYMBOL_NAME:  MyApp!ProcessData+42

IMAGE_NAME:  MyApp.exe

FAILURE_BUCKET_ID:  ACCESS_VIOLATION_c0000005_MyApp.exe!ProcessData

STACK_TEXT:
00000000`0012f000 00000000`0012f100 MyApp!ProcessData+0x42
00000000`0012f100 00000000`0012f200 MyApp!Main+0x128

SYMBOL_NAME:  MyApp!ProcessData+42
";

        var result = AnalyzeParser.Parse(output);

        Assert.Equal("c0000005", result.ExceptionCode?.ToLower());
        Assert.Equal("Access Violation", result.CrashType);
        Assert.Contains("MyApp", result.FaultingModule ?? "");
        Assert.Contains("ProcessData", result.FaultingSymbol ?? "");
        Assert.Equal("MyApp.exe", result.ProcessName);
        Assert.True(result.StackFrames.Count > 0);
    }

    [Fact]
    public void AnalyzeParser_ParsesDotNetException()
    {
        var output = @"
ExceptionCode: e0434352 (CLR exception)
ExceptionAddress: 00007ffc12345678

PROCESS_NAME:  dotnet.exe

MODULE_NAME: clr

SYMBOL_NAME:  clr!RaiseException+0x123

FAILURE_BUCKET_ID:  CLR_EXCEPTION_System.NullReferenceException
";

        var result = AnalyzeParser.Parse(output);

        Assert.Equal("e0434352", result.ExceptionCode?.ToLower());
        Assert.Equal(".NET CLR Exception", result.CrashType);
    }

    [Fact]
    public void AnalyzeParser_ParsesBugCheck()
    {
        var output = @"
BugCheck D1, {0, 2, 0, fffff80012345678}

SYMBOL_NAME:  baddriver!BadFunction+0x10

IMAGE_NAME:  baddriver.sys
";

        var result = AnalyzeParser.Parse(output);

        Assert.Equal("D1", result.BugCheckCode);
        Assert.Equal("Kernel Bug Check (BSOD)", result.CrashType);
    }

    [Fact]
    public void AnalyzeParser_HandlesEmptyOutput()
    {
        var result = AnalyzeParser.Parse("");

        Assert.Equal("Unknown", result.CrashType);
        Assert.Empty(result.StackFrames);
    }

    #endregion

    #region HeapStatsParser Tests

    [Fact]
    public void HeapStatsParser_ParsesStatOutput()
    {
        var output = @"
Statistics:
              MT    Count    TotalSize Class Name
00007ff812345678       10         1000 System.String
00007ff812345679      100        50000 System.Byte[]
00007ff81234567a     1000       200000 MyApp.Customer
Total 1110 objects
";

        var result = HeapStatsParser.Parse(output);

        Assert.Equal(3, result.TypeStats.Count);
        Assert.Equal(1110, result.TotalObjects);
        
        // Should be sorted by size descending
        Assert.Equal("MyApp.Customer", result.TypeStats[0].TypeName);
        Assert.Equal(1000, result.TypeStats[0].Count);
        Assert.Equal(200000, result.TypeStats[0].TotalSize);
    }

    [Fact]
    public void HeapStatsParser_HandlesEmptyOutput()
    {
        var result = HeapStatsParser.Parse("");

        Assert.Empty(result.TypeStats);
        Assert.Equal(0, result.TotalObjects);
    }

    #endregion

    #region ModuleParser Tests

    [Fact]
    public void ModuleParser_ParsesModuleList()
    {
        var output = @"
start             end                 module name
00007ff6`12340000 00007ff6`12350000   MyApp      (deferred)
00007ffc`00000000 00007ffc`00100000   ntdll      (pdb symbols)
00007ffc`10000000 00007ffc`10200000   kernel32   (deferred)
";

        var result = ModuleParser.Parse(output);

        Assert.Equal(3, result.Modules.Count);
        
        var myApp = result.Modules.FirstOrDefault(m => m.Name == "MyApp");
        Assert.NotNull(myApp);
        Assert.Equal("(deferred)", myApp.Status);
        Assert.True(myApp.Size > 0);
    }

    [Fact]
    public void ModuleParser_HandlesEmptyOutput()
    {
        var result = ModuleParser.Parse("");

        Assert.Empty(result.Modules);
    }

    #endregion

    #region StackParser Tests

    [Fact]
    public void StackParser_ParsesClrStack()
    {
        var output = @"
OS Thread Id: 0x1234 (0)
        SP               IP               Function
000000123456789a 0000001234567abc MyApp.Program.Main(System.String[])
000000123456789b 0000001234567abd MyApp.Processor.Process(MyApp.Data)
000000123456789c 0000001234567abe System.Threading.Thread.Start()
";

        var result = StackParser.ParseClrStack(output);

        Assert.Equal("1234", result.ThreadId);
        Assert.Equal("Managed (.NET)", result.StackType);
        Assert.Equal(3, result.Frames.Count);
        
        var mainFrame = result.Frames[0];
        Assert.Equal("MyApp.Program", mainFrame.TypeName);
        Assert.Equal("Main", mainFrame.MethodName);
    }

    [Fact]
    public void StackParser_ParsesNativeStack()
    {
        var output = @"
Child-SP          RetAddr           Call Site
00000000`0012f000 00000000`77654321 ntdll!NtWaitForSingleObject+0x14
00000000`0012f100 00000000`77654322 kernel32!WaitForSingleObjectEx+0x9c
00000000`0012f200 00000000`77654323 MyApp!ProcessData+0x42
";

        var result = StackParser.ParseNativeStack(output);

        Assert.Equal("Native", result.StackType);
        Assert.Equal(3, result.Frames.Count);
        
        var lastFrame = result.Frames[2];
        Assert.Equal("MyApp", lastFrame.Module);
        Assert.Equal("ProcessData", lastFrame.MethodName);
        Assert.Equal("0x42", lastFrame.Offset);
    }

    [Fact]
    public void StackParser_HandlesEmptyOutput()
    {
        var clrResult = StackParser.ParseClrStack("");
        var nativeResult = StackParser.ParseNativeStack("");

        Assert.Empty(clrResult.Frames);
        Assert.Empty(nativeResult.Frames);
    }

    #endregion
}
