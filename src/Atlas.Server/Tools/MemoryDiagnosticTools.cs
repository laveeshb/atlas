using Microsoft.Diagnostics.Runtime;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class MemoryDiagnosticTools
{
    [McpServerTool(Name = "compare_heaps")]
    [Description("Compare two dump files to identify memory growth - shows types with largest count/size changes")]
    public static object CompareHeaps(
        [Description("Path to the baseline (earlier) dump file")] string baselinePath,
        [Description("Path to the comparison (later) dump file")] string comparisonPath,
        [Description("Minimum size delta in bytes to report (default 1MB)")] long minSizeDelta = 1024 * 1024,
        [Description("Max types to return (default 30)")] int limit = 30)
    {
        if (!File.Exists(baselinePath))
            return new { error = $"Baseline file not found: {baselinePath}" };
        if (!File.Exists(comparisonPath))
            return new { error = $"Comparison file not found: {comparisonPath}" };

        try
        {
            var baselineStats = GetHeapStats(baselinePath);
            var comparisonStats = GetHeapStats(comparisonPath);

            if (baselineStats.error != null)
                return new { error = $"Baseline analysis failed: {baselineStats.error}" };
            if (comparisonStats.error != null)
                return new { error = $"Comparison analysis failed: {comparisonStats.error}" };

            var allTypes = baselineStats.stats.Keys.Union(comparisonStats.stats.Keys).ToList();

            var deltas = allTypes
                .Select(type =>
                {
                    baselineStats.stats.TryGetValue(type, out var baseline);
                    comparisonStats.stats.TryGetValue(type, out var comparison);

                    return new
                    {
                        type,
                        baselineCount = baseline.count,
                        comparisonCount = comparison.count,
                        countDelta = comparison.count - baseline.count,
                        baselineSizeBytes = baseline.size,
                        comparisonSizeBytes = comparison.size,
                        sizeDeltaBytes = comparison.size - baseline.size,
                        sizeDeltaMB = Math.Round((comparison.size - baseline.size) / 1024.0 / 1024.0, 2)
                    };
                })
                .Where(d => Math.Abs(d.sizeDeltaBytes) >= minSizeDelta)
                .OrderByDescending(d => d.sizeDeltaBytes)
                .Take(limit)
                .ToList();

            var totalBaseline = baselineStats.stats.Values.Sum(s => s.size);
            var totalComparison = comparisonStats.stats.Values.Sum(s => s.size);

            return new
            {
                baseline = new
                {
                    path = baselinePath,
                    totalSizeMB = Math.Round(totalBaseline / 1024.0 / 1024.0, 2),
                    typeCount = baselineStats.stats.Count
                },
                comparison = new
                {
                    path = comparisonPath,
                    totalSizeMB = Math.Round(totalComparison / 1024.0 / 1024.0, 2),
                    typeCount = comparisonStats.stats.Count
                },
                totalGrowthMB = Math.Round((totalComparison - totalBaseline) / 1024.0 / 1024.0, 2),
                typesWithGrowth = deltas.Count(d => d.sizeDeltaBytes > 0),
                typesWithShrinkage = deltas.Count(d => d.sizeDeltaBytes < 0),
                deltas
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Comparison failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "large_objects")]
    [Description("List objects on the Large Object Heap (>85KB) - common source of memory issues")]
    public static object LargeObjects(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Max objects to return (default 50)")] int limit = 50)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            if (!heap.CanWalkHeap)
                return new { error = "Heap is not in a walkable state" };

            var lohSegments = heap.Segments.Where(s => s.Kind == GCSegmentKind.Large).ToList();
            var objects = new List<object>();
            long totalLohSize = 0;
            long totalLohCount = 0;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid) continue;

                // Check if object is in LOH
                var segment = heap.GetSegmentByAddress(obj.Address);
                if (segment == null || segment.Kind != GCSegmentKind.Large) continue;

                totalLohCount++;
                totalLohSize += (long)obj.Size;

                if (objects.Count < limit)
                {
                    var typeName = obj.Type?.Name ?? "<unknown>";
                    string preview = "";

                    if (typeName == "System.String")
                    {
                        var str = obj.AsString();
                        preview = str?.Length > 100 ? str[..100] + "..." : str ?? "";
                    }
                    else if (typeName.EndsWith("[]"))
                    {
                        preview = $"Length: {obj.AsArray().Length}";
                    }

                    objects.Add(new
                    {
                        address = $"0x{obj.Address:X}",
                        type = typeName,
                        sizeBytes = obj.Size,
                        sizeMB = Math.Round((double)obj.Size / 1024 / 1024, 2),
                        preview
                    });
                }
            }

            // Sort by size descending
            objects = objects.OrderByDescending(o => ((dynamic)o).sizeBytes).ToList();

            return new
            {
                lohSegmentCount = lohSegments.Count,
                totalLohSizeMB = Math.Round(totalLohSize / 1024.0 / 1024.0, 2),
                totalLohObjects = totalLohCount,
                returnedCount = objects.Count,
                objects
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "finalizer_queue")]
    [Description("Show objects with finalizers - types that implement destructors/Dispose patterns")]
    public static object FinalizerQueue(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Max objects to return (default 50)")] int limit = 50)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            if (!heap.CanWalkHeap)
                return new { error = "Heap is not in a walkable state" };

            // Find objects whose types have finalizers
            var objects = new List<object>();
            var typeCounts = new Dictionary<string, (int count, long size)>();

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid || obj.Type == null) continue;

                // Check if type has a finalizer
                if (!obj.Type.IsFinalizable) continue;

                var typeName = obj.Type.Name ?? "<unknown>";

                if (!typeCounts.ContainsKey(typeName))
                    typeCounts[typeName] = (0, 0);
                var current = typeCounts[typeName];
                typeCounts[typeName] = (current.count + 1, current.size + (long)obj.Size);

                if (objects.Count < limit)
                {
                    objects.Add(new
                    {
                        address = $"0x{obj.Address:X}",
                        type = typeName,
                        size = obj.Size
                    });
                }
            }

            var typeStats = typeCounts
                .OrderByDescending(kvp => kvp.Value.size)
                .Take(20)
                .Select(kvp => new { type = kvp.Key, count = kvp.Value.count, totalSize = kvp.Value.size })
                .ToList();

            return new
            {
                finalizableObjectCount = typeCounts.Values.Sum(v => v.count),
                uniqueTypes = typeCounts.Count,
                typeBreakdown = typeStats,
                returnedCount = objects.Count,
                objects,
                note = "These are objects with finalizers. Large numbers may indicate missing Dispose() calls."
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "pinned_objects")]
    [Description("Find pinned objects that prevent GC compaction")]
    public static object PinnedObjects(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Max objects to return (default 50)")] int limit = 50)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            var pinnedRoots = heap.EnumerateRoots()
                .Where(r => r.IsPinned)
                .ToList();

            var objects = new List<object>();
            var typeCounts = new Dictionary<string, (int count, long size)>();
            long totalPinnedSize = 0;

            foreach (var root in pinnedRoots)
            {
                var obj = heap.GetObject(root.Object);
                if (!obj.IsValid) continue;

                var typeName = obj.Type?.Name ?? "<unknown>";
                var size = (long)obj.Size;
                totalPinnedSize += size;

                if (!typeCounts.ContainsKey(typeName))
                    typeCounts[typeName] = (0, 0);
                var current = typeCounts[typeName];
                typeCounts[typeName] = (current.count + 1, current.size + size);

                if (objects.Count < limit)
                {
                    objects.Add(new
                    {
                        address = $"0x{obj.Address:X}",
                        rootAddress = $"0x{root.Address:X}",
                        rootKind = root.RootKind.ToString(),
                        type = typeName,
                        size = obj.Size
                    });
                }
            }

            var typeStats = typeCounts
                .OrderByDescending(kvp => kvp.Value.size)
                .Take(20)
                .Select(kvp => new
                {
                    type = kvp.Key,
                    count = kvp.Value.count,
                    totalSizeBytes = kvp.Value.size
                })
                .ToList();

            return new
            {
                totalPinnedObjects = pinnedRoots.Count,
                totalPinnedSizeMB = Math.Round(totalPinnedSize / 1024.0 / 1024.0, 2),
                uniqueTypes = typeCounts.Count,
                typeBreakdown = typeStats,
                returnedCount = objects.Count,
                objects
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "duplicate_strings")]
    [Description("Find duplicate string content - common source of memory waste")]
    public static object DuplicateStrings(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Minimum string length to consider (default 20)")] int minLength = 20,
        [Description("Minimum duplicate count to report (default 5)")] int minCount = 5,
        [Description("Max results to return (default 30)")] int limit = 30)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            if (!heap.CanWalkHeap)
                return new { error = "Heap is not in a walkable state" };

            var stringCounts = new Dictionary<string, (int count, long totalSize)>();
            long totalStrings = 0;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid || obj.Type?.Name != "System.String") continue;

                totalStrings++;
                var str = obj.AsString();
                if (str == null || str.Length < minLength) continue;

                // Truncate very long strings for grouping
                var key = str.Length > 500 ? str[..500] : str;

                if (!stringCounts.ContainsKey(key))
                    stringCounts[key] = (0, 0);

                var current = stringCounts[key];
                stringCounts[key] = (current.count + 1, current.totalSize + (long)obj.Size);
            }

            var duplicates = stringCounts
                .Where(kvp => kvp.Value.count >= minCount)
                .OrderByDescending(kvp => kvp.Value.totalSize)
                .Take(limit)
                .Select(kvp => new
                {
                    value = kvp.Key.Length > 100 ? kvp.Key[..100] + "..." : kvp.Key,
                    fullLength = kvp.Key.Length,
                    count = kvp.Value.count,
                    wastedSizeBytes = kvp.Value.totalSize - (kvp.Value.totalSize / kvp.Value.count), // Size that could be saved
                    wastedSizeKB = Math.Round((kvp.Value.totalSize - (kvp.Value.totalSize / kvp.Value.count)) / 1024.0, 1),
                    totalSizeBytes = kvp.Value.totalSize
                })
                .ToList();

            var totalWasted = duplicates.Sum(d => d.wastedSizeBytes);

            return new
            {
                totalStringsAnalyzed = totalStrings,
                duplicateGroupsFound = stringCounts.Count(kvp => kvp.Value.count >= minCount),
                potentialSavingsMB = Math.Round(totalWasted / 1024.0 / 1024.0, 2),
                returnedCount = duplicates.Count,
                duplicates
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    private static (Dictionary<string, (long count, long size)> stats, string? error) GetHeapStats(string filePath)
    {
        var stats = new Dictionary<string, (long count, long size)>();

        try
        {
            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return (stats, "No CLR found in dump");

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            if (!heap.CanWalkHeap)
                return (stats, "Heap is not walkable");

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid) continue;

                var typeName = obj.Type?.Name ?? "<unknown>";
                var size = (long)obj.Size;

                if (!stats.ContainsKey(typeName))
                    stats[typeName] = (0, 0);

                var current = stats[typeName];
                stats[typeName] = (current.count + 1, current.size + size);
            }

            return (stats, null);
        }
        catch (Exception ex)
        {
            return (stats, ex.Message);
        }
    }
}
