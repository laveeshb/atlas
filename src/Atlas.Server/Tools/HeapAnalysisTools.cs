using Microsoft.Diagnostics.Runtime;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class HeapAnalysisTools
{
    [McpServerTool(Name = "dump_heap_stats")]
    [Description("Get object count and size statistics by type (like WinDbg !dumpheap -stat)")]
    public static object DumpHeapStats(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Minimum object count to include in results (default 10)")] int minCount = 10,
        [Description("Max types to return (default 50)")] int limit = 50)
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

            var stats = new Dictionary<string, (long count, long size)>();
            long totalObjects = 0;
            long totalSize = 0;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid) continue;

                var typeName = obj.Type?.Name ?? "<unknown>";
                var size = (long)obj.Size;

                if (!stats.ContainsKey(typeName))
                    stats[typeName] = (0, 0);

                var current = stats[typeName];
                stats[typeName] = (current.count + 1, current.size + size);
                totalObjects++;
                totalSize += size;
            }

            var typeStats = stats
                .Where(s => s.Value.count >= minCount)
                .OrderByDescending(s => s.Value.size)
                .Take(limit)
                .Select(s => new
                {
                    type = s.Key,
                    count = s.Value.count,
                    totalSizeBytes = s.Value.size,
                    totalSizeMB = Math.Round(s.Value.size / 1024.0 / 1024.0, 2),
                    avgSizeBytes = s.Value.count > 0 ? s.Value.size / s.Value.count : 0
                })
                .ToList();

            return new
            {
                totalObjects,
                totalSizeBytes = totalSize,
                totalSizeMB = Math.Round(totalSize / 1024.0 / 1024.0, 2),
                typeCount = stats.Count,
                types = typeStats
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Analysis failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "find_objects")]
    [Description("Find objects by type name (like WinDbg !dumpheap -type)")]
    public static object FindObjects(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Type name to search for (partial match)")] string typeName,
        [Description("Max objects to return (default 20)")] int limit = 20)
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

            var matches = new List<object>();
            long totalMatches = 0;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid) continue;

                var objTypeName = obj.Type?.Name ?? "";
                if (!objTypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                totalMatches++;

                if (matches.Count < limit)
                {
                    matches.Add(new
                    {
                        address = $"0x{obj.Address:X}",
                        type = objTypeName,
                        size = obj.Size
                    });
                }
            }

            return new
            {
                query = typeName,
                totalMatches,
                returnedCount = matches.Count,
                objects = matches
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Search failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "dump_object")]
    [Description("Inspect object fields at a specific address (like WinDbg !do)")]
    public static object DumpObject(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Object address (hex string like 0x00007ff8a1234567)")] string address)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            var objAddress = ParseAddress(address);
            if (objAddress == 0)
                return new { error = $"Invalid address format: {address}" };

            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            var obj = heap.GetObject(objAddress);
            if (!obj.IsValid)
                return new { error = $"No valid object at address {address}" };

            var type = obj.Type;
            if (type == null)
                return new { error = $"Could not determine type for object at {address}" };

            var fields = new List<object>();
            foreach (var field in type.Fields)
            {
                string fieldValueStr;

                try
                {
                    if (field.IsObjectReference)
                    {
                        var refObj = obj.ReadObjectField(field.Name ?? "");
                        if (refObj.IsNull)
                            fieldValueStr = "null";
                        else if (refObj.Type?.Name == "System.String")
                            fieldValueStr = $"\"{refObj.AsString()}\"";
                        else
                            fieldValueStr = $"0x{refObj.Address:X} ({refObj.Type?.Name ?? "?"})";
                    }
                    else if (field.IsPrimitive)
                    {
                        // Read primitive field value based on type
                        fieldValueStr = ReadPrimitiveField(obj, field);
                    }
                    else if (field.IsValueType)
                    {
                        fieldValueStr = $"[ValueType: {field.Type?.Name ?? "?"}]";
                    }
                    else
                    {
                        fieldValueStr = "<unknown>";
                    }
                }
                catch
                {
                    fieldValueStr = "<error reading>";
                }

                fields.Add(new
                {
                    name = field.Name,
                    type = field.Type?.Name ?? "?",
                    offset = field.Offset,
                    value = fieldValueStr
                });
            }

            return new
            {
                address = $"0x{obj.Address:X}",
                type = type.Name,
                baseType = type.BaseType?.Name,
                size = obj.Size,
                isArray = type.IsArray,
                arrayLength = type.IsArray ? obj.AsArray().Length : (int?)null,
                fields
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Dump failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "find_strings")]
    [Description("Find string objects, optionally containing specific text")]
    public static object FindStrings(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Optional: text to search for within strings")] string? containing = null,
        [Description("Minimum string length (default 10)")] int minLength = 10,
        [Description("Max strings to return (default 50)")] int limit = 50)
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

            var strings = new List<object>();
            long totalStrings = 0;
            long totalMatches = 0;
            long totalStringSize = 0;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid || obj.Type?.Name != "System.String") continue;

                totalStrings++;
                var str = obj.AsString();
                if (str == null) continue;

                totalStringSize += (long)obj.Size;

                if (str.Length < minLength) continue;

                if (!string.IsNullOrEmpty(containing) &&
                    !str.Contains(containing, StringComparison.OrdinalIgnoreCase))
                    continue;

                totalMatches++;

                if (strings.Count < limit)
                {
                    strings.Add(new
                    {
                        address = $"0x{obj.Address:X}",
                        length = str.Length,
                        size = obj.Size,
                        value = str.Length > 200 ? str[..200] + "..." : str
                    });
                }
            }

            return new
            {
                totalStrings,
                totalStringsSizeMB = Math.Round(totalStringSize / 1024.0 / 1024.0, 2),
                searchFilter = containing,
                minLength,
                matchCount = totalMatches,
                returnedCount = strings.Count,
                strings
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Search failed: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "gc_roots")]
    [Description("Find GC roots keeping an object alive (like WinDbg !gcroot)")]
    public static object GcRoots(
        [Description("Full path to the .dmp file")] string filePath,
        [Description("Object address (hex string like 0x00007ff8a1234567)")] string address)
    {
        if (!File.Exists(filePath))
            return new { error = $"File not found: {filePath}" };

        try
        {
            var objAddress = ParseAddress(address);
            if (objAddress == 0)
                return new { error = $"Invalid address format: {address}" };

            using var dataTarget = DataTarget.LoadDump(filePath);
            var clrVersion = dataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return new { error = "No CLR found in dump - not a .NET process dump" };

            using var runtime = clrVersion.CreateRuntime();
            var heap = runtime.Heap;

            var obj = heap.GetObject(objAddress);
            if (!obj.IsValid)
                return new { error = $"No valid object at address {address}" };

            var roots = new List<object>();

            // Find direct roots pointing to this object
            foreach (var root in heap.EnumerateRoots())
            {
                if (root.Object == objAddress)
                {
                    roots.Add(new
                    {
                        rootKind = root.RootKind.ToString(),
                        address = $"0x{root.Address:X}",
                        objectAddress = $"0x{root.Object:X}",
                        isPinned = root.IsPinned,
                        isInterior = root.IsInterior
                    });
                }
            }

            // Find objects that reference this object
            var referencingObjects = new List<object>();
            foreach (var heapObj in heap.EnumerateObjects())
            {
                if (!heapObj.IsValid || heapObj.Type == null) continue;

                foreach (var refObj in heapObj.EnumerateReferences())
                {
                    if (refObj.Address == objAddress)
                    {
                        referencingObjects.Add(new
                        {
                            address = $"0x{heapObj.Address:X}",
                            type = heapObj.Type.Name
                        });

                        if (referencingObjects.Count >= 20) break;
                    }
                }
                if (referencingObjects.Count >= 20) break;
            }

            return new
            {
                targetObject = new
                {
                    address = $"0x{obj.Address:X}",
                    type = obj.Type?.Name ?? "<unknown>",
                    size = obj.Size
                },
                directRootCount = roots.Count,
                directRoots = roots,
                referencingObjectCount = referencingObjects.Count,
                referencingObjects,
                note = "Use referencing objects to trace back to GC roots"
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Root analysis failed: {ex.Message}" };
        }
    }

    private static string ReadPrimitiveField(ClrObject obj, ClrInstanceField field)
    {
        try
        {
            var typeName = field.Type?.Name ?? "";
            return typeName switch
            {
                "System.Int32" => obj.ReadField<int>(field.Name ?? "").ToString(),
                "System.Int64" => obj.ReadField<long>(field.Name ?? "").ToString(),
                "System.Int16" => obj.ReadField<short>(field.Name ?? "").ToString(),
                "System.Byte" => obj.ReadField<byte>(field.Name ?? "").ToString(),
                "System.Boolean" => obj.ReadField<bool>(field.Name ?? "").ToString(),
                "System.Double" => obj.ReadField<double>(field.Name ?? "").ToString(),
                "System.Single" => obj.ReadField<float>(field.Name ?? "").ToString(),
                "System.Char" => obj.ReadField<char>(field.Name ?? "").ToString(),
                "System.UInt32" => obj.ReadField<uint>(field.Name ?? "").ToString(),
                "System.UInt64" => obj.ReadField<ulong>(field.Name ?? "").ToString(),
                "System.IntPtr" => $"0x{obj.ReadField<nint>(field.Name ?? ""):X}",
                "System.UIntPtr" => $"0x{obj.ReadField<nuint>(field.Name ?? ""):X}",
                _ => $"[{typeName}]"
            };
        }
        catch
        {
            return "<error>";
        }
    }

    private static ulong ParseAddress(string address)
    {
        var addr = address.Trim();
        if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            addr = addr[2..];

        if (ulong.TryParse(addr, System.Globalization.NumberStyles.HexNumber, null, out var result))
            return result;

        return 0;
    }
}
