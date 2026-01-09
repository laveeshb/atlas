// Test helper app that crashes with a specific exception
// Usage: CrashingApp.exe [crashType]
// crashType: "null" (NullReferenceException), "invalidop" (InvalidOperationException), 
//            "nested" (nested exceptions), "stackoverflow" (StackOverflowException)
// Signals readiness by writing "READY" to stdout before crashing

var crashType = args.Length > 0 ? args[0].ToLower() : "null";

// Allocate some objects so heap analysis has something to find
var data = new List<DataHolder>();
for (int i = 0; i < 100; i++)
{
    data.Add(new DataHolder { Id = i, Name = $"Item_{i}", Timestamp = DateTime.UtcNow });
}

Console.WriteLine("READY");
Console.Out.Flush();

// Small delay to ensure dump can be captured
Thread.Sleep(500);

try
{
    switch (crashType)
    {
        case "null":
            CauseNullReference();
            break;
        case "invalidop":
            CauseInvalidOperation();
            break;
        case "nested":
            CauseNestedException();
            break;
        case "stackoverflow":
            CauseStackOverflow(0);
            break;
        default:
            CauseNullReference();
            break;
    }
}
catch (Exception ex)
{
    // Re-throw to ensure it's an unhandled exception
    Console.WriteLine($"CRASHING: {ex.GetType().Name}");
    Console.Out.Flush();
    throw;
}

static void CauseNullReference()
{
    string? nullString = null;
    _ = nullString!.Length; // This will throw NullReferenceException
}

static void CauseInvalidOperation()
{
    var list = new List<int> { 1, 2, 3 };
    using var enumerator = list.GetEnumerator();
    enumerator.MoveNext();
    list.Add(4); // Modify during enumeration
    enumerator.MoveNext(); // This throws InvalidOperationException
}

static void CauseNestedException()
{
    try
    {
        try
        {
            throw new ArgumentException("Inner-most exception: bad argument");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Middle exception: invalid state", ex);
        }
    }
    catch (Exception ex)
    {
        throw new ApplicationException("Outer exception: application error", ex);
    }
}

static void CauseStackOverflow(int depth)
{
    // This will cause StackOverflowException
    CauseStackOverflow(depth + 1);
}

// Data class to populate the heap
public class DataHolder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public byte[]? Payload { get; set; }
}
