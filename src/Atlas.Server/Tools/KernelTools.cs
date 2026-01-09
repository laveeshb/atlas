using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Atlas.Server.Tools;

[McpServerToolType]
public static class KernelTools
{
    #region Native Interop

    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

    private enum SYSTEM_INFORMATION_CLASS
    {
        SystemModuleInformation = 11
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_PROCESS_MODULE_INFORMATION
    {
        public IntPtr Section;
        public IntPtr MappedBase;
        public IntPtr ImageBase;
        public uint ImageSize;
        public uint Flags;
        public ushort LoadOrderIndex;
        public ushort InitOrderIndex;
        public ushort LoadCount;
        public ushort OffsetToFileName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] FullPathName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_PROCESS_MODULES
    {
        public uint NumberOfModules;
        // Followed by RTL_PROCESS_MODULE_INFORMATION array
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        SYSTEM_INFORMATION_CLASS SystemInformationClass,
        IntPtr SystemInformation,
        uint SystemInformationLength,
        out uint ReturnLength);

    #endregion

    /// <summary>
    /// Lists all loaded kernel drivers with basic information.
    /// </summary>
    /// <param name="nameFilter">Optional filter by driver name (partial match, case-insensitive)</param>
    /// <param name="limit">Maximum number of results to return (default: 100)</param>
    /// <returns>Object containing driver list with name, path, size, base address, and load order</returns>
    [McpServerTool(Name = "list_drivers")]
    [Description("List all loaded kernel drivers with basic information including name, path, size, and base address")]
    public static object ListDrivers(
        [Description("Optional filter by driver name (partial match, case-insensitive)")]
        string? nameFilter = null,
        [Description("Maximum number of results to return (default: 100)")]
        int limit = 100)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var drivers = GetLoadedDrivers();

            if (!string.IsNullOrEmpty(nameFilter))
            {
                drivers = drivers
                    .Where(d => d.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var results = drivers
                .OrderBy(d => d.LoadOrder)
                .Take(limit)
                .Select(d => new
                {
                    name = d.Name,
                    path = d.FullPath,
                    baseAddress = $"0x{d.ImageBase:X}",
                    size = d.ImageSize,
                    sizeFormatted = FormatSize(d.ImageSize),
                    loadOrder = d.LoadOrder
                })
                .ToList();

            return new
            {
                totalLoaded = drivers.Count,
                returned = results.Count,
                drivers = results
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Gets detailed information about a specific kernel driver.
    /// </summary>
    /// <param name="driverName">Name of the driver to inspect (e.g., 'ntfs.sys' or 'ntfs')</param>
    /// <returns>Object containing driver details including version info and digital signature status</returns>
    [McpServerTool(Name = "get_driver_info")]
    [Description("Get detailed information about a specific kernel driver including version, publisher, and digital signature status")]
    public static object GetDriverInfo(
        [Description("Name of the driver to inspect (e.g., 'ntfs.sys' or 'ntfs')")]
        string driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return new { error = "Driver name is required" };
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new { error = "This tool is only available on Windows" };
        }

        try
        {
            var drivers = GetLoadedDrivers();

            // Find by exact name or partial match
            var driver = drivers.FirstOrDefault(d =>
                d.Name.Equals(driverName, StringComparison.OrdinalIgnoreCase) ||
                d.Name.Equals(driverName + ".sys", StringComparison.OrdinalIgnoreCase)) ??
                drivers.FirstOrDefault(d =>
                    d.Name.Contains(driverName, StringComparison.OrdinalIgnoreCase));

            if (driver == null)
            {
                return new { error = $"Driver '{driverName}' not found. Use list_drivers to see loaded drivers." };
            }

            // Get file version info
            var versionInfo = GetVersionInfo(driver.FullPath);

            // Get signature info
            var signatureInfo = GetSignatureInfo(driver.FullPath);

            return new
            {
                name = driver.Name,
                path = driver.FullPath,
                baseAddress = $"0x{driver.ImageBase:X}",
                size = driver.ImageSize,
                sizeFormatted = FormatSize(driver.ImageSize),
                loadOrder = driver.LoadOrder,
                version = versionInfo,
                signature = signatureInfo
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    #region Helper Methods

    private record DriverInfo(
        string Name,
        string FullPath,
        ulong ImageBase,
        uint ImageSize,
        int LoadOrder);

    private static List<DriverInfo> GetLoadedDrivers()
    {
        var drivers = new List<DriverInfo>();
        uint returnLength = 0;

        // First call to get required buffer size
        NtQuerySystemInformation(
            SYSTEM_INFORMATION_CLASS.SystemModuleInformation,
            IntPtr.Zero,
            0,
            out returnLength);

        if (returnLength == 0)
        {
            throw new InvalidOperationException("Failed to query system module information size");
        }

        // Allocate buffer with some extra space
        var bufferSize = returnLength + 4096;
        var buffer = Marshal.AllocHGlobal((int)bufferSize);

        try
        {
            var status = NtQuerySystemInformation(
                SYSTEM_INFORMATION_CLASS.SystemModuleInformation,
                buffer,
                bufferSize,
                out returnLength);

            if (status != 0 && status != STATUS_INFO_LENGTH_MISMATCH)
            {
                throw new InvalidOperationException($"NtQuerySystemInformation failed with status 0x{status:X}");
            }

            // Read the number of modules
            var numberOfModules = (uint)Marshal.ReadInt32(buffer);

            // Calculate offset to first module entry
            var moduleOffset = IntPtr.Size; // Skip NumberOfModules field (pointer-aligned)

            for (int i = 0; i < numberOfModules; i++)
            {
                var modulePtr = IntPtr.Add(buffer, moduleOffset + (i * Marshal.SizeOf<RTL_PROCESS_MODULE_INFORMATION>()));
                var module = Marshal.PtrToStructure<RTL_PROCESS_MODULE_INFORMATION>(modulePtr);

                // Extract the file name from the path - find first null terminator
                var nullIndex = Array.IndexOf(module.FullPathName, (byte)0);
                var pathLength = nullIndex >= 0 ? nullIndex : module.FullPathName.Length;
                var fullPath = Encoding.ASCII.GetString(module.FullPathName, 0, pathLength);

                // Convert NT path to DOS path
                var dosPath = ConvertNtPathToDosPath(fullPath);

                // Extract filename - handle null terminator properly
                var fileNameOffset = Math.Min(module.OffsetToFileName, pathLength);
                var fileName = fileNameOffset < pathLength
                    ? Encoding.ASCII.GetString(module.FullPathName, fileNameOffset, pathLength - fileNameOffset)
                    : Path.GetFileName(dosPath);

                drivers.Add(new DriverInfo(
                    fileName,
                    dosPath,
                    (ulong)module.ImageBase,
                    module.ImageSize,
                    module.LoadOrderIndex));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return drivers;
    }

    private static string ConvertNtPathToDosPath(string ntPath)
    {
        if (ntPath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return systemRoot + ntPath.Substring(11);
        }

        if (ntPath.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            return ntPath.Substring(4);
        }

        if (ntPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return ntPath.Substring(4);
        }

        return ntPath;
    }

    private static object GetVersionInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new { available = false, reason = "File not found" };
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);

            return new
            {
                available = true,
                fileVersion = versionInfo.FileVersion ?? "Unknown",
                productVersion = versionInfo.ProductVersion ?? "Unknown",
                productName = versionInfo.ProductName ?? "Unknown",
                companyName = versionInfo.CompanyName ?? "Unknown",
                description = versionInfo.FileDescription ?? "Unknown",
                originalFilename = versionInfo.OriginalFilename ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            return new { available = false, reason = ex.Message };
        }
    }

    private static object GetSignatureInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new { signed = false, reason = "File not found" };
            }

            // Use X509Certificate to check for Authenticode signature
            try
            {
                var cert = X509Certificate.CreateFromSignedFile(filePath);
                using var cert2 = new X509Certificate2(cert);

                return new
                {
                    signed = true,
                    subject = cert2.Subject,
                    issuer = cert2.Issuer,
                    validFrom = cert2.NotBefore.ToString("yyyy-MM-dd"),
                    validTo = cert2.NotAfter.ToString("yyyy-MM-dd"),
                    thumbprint = cert2.Thumbprint,
                    isValid = cert2.NotAfter > DateTime.Now && cert2.NotBefore < DateTime.Now
                };
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return new { signed = false, reason = "No valid signature found" };
            }
        }
        catch (Exception ex)
        {
            return new { signed = false, reason = ex.Message };
        }
    }

    private static string FormatSize(uint bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    #endregion
}
