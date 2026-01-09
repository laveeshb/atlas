// Test helper app that allocates memory over time
// Usage: MemoryGrowthApp.exe [totalMB] [intervalMs]
// Signals readiness by writing "READY" to stdout
// Writes "ALLOCATED {n}MB" after each allocation

var totalMB = args.Length > 0 ? int.Parse(args[0]) : 50;
var intervalMs = args.Length > 1 ? int.Parse(args[1]) : 100;

var allocations = new List<byte[]>();
var random = new Random(42); // Fixed seed for reproducibility

Console.WriteLine("READY");
Console.Out.Flush();

var allocated = 0;
while (allocated < totalMB)
{
    // Allocate 1MB chunks with random data to prevent optimization
    var chunk = new byte[1024 * 1024];
    random.NextBytes(chunk);
    allocations.Add(chunk);
    allocated++;
    
    Console.WriteLine($"ALLOCATED {allocated}MB");
    Console.Out.Flush();
    
    Thread.Sleep(intervalMs);
}

// Keep alive until killed - signal we're done growing
Console.WriteLine("GROWTH_COMPLETE");
Console.Out.Flush();

// Wait for signal to exit (read from stdin) or timeout
Console.ReadLine();
