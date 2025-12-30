use crate::{DumpAnalysisResult, ExceptionInfo, ModuleInfo, SystemInfo, ThreadInfo};
use minidump::{Minidump, MinidumpModuleList, MinidumpThreadList};
use minidump_common::format::MINIDUMP_STREAM_TYPE;
use std::fs;
use std::path::Path;

const MINIDUMP_SIGNATURE: u32 = 0x504D444D; // 'MDMP'

pub fn analyze(path: &str) -> DumpAnalysisResult {
    let path = Path::new(path);
    
    // Get file size
    let file_size_mb = match fs::metadata(path) {
        Ok(meta) => Some(meta.len() / 1024 / 1024),
        Err(_) => None,
    };

    // Check file header first
    let header = match fs::read(path) {
        Ok(data) if data.len() >= 4 => data,
        Ok(_) => return error("File too small to be a valid dump"),
        Err(e) => return error(&format!("Failed to read file: {}", e)),
    };

    let signature = u32::from_le_bytes([header[0], header[1], header[2], header[3]]);
    
    // Determine dump type
    let (dump_type, dump_type_description) = match signature {
        MINIDUMP_SIGNATURE => ("minidump", "Windows Minidump - crash/diagnostic dump"),
        0x45474150 => ("kernel_dump", "Windows Kernel Memory Dump"),  // 'PAGE'
        0x504D5544 => ("full_dump", "Windows Full Memory Dump"),      // 'DUMP'
        _ => {
            return DumpAnalysisResult {
                success: true,
                dump_type: Some("unknown".to_string()),
                dump_type_description: Some(format!("Unknown format (signature: 0x{:08X})", signature)),
                error: None,
                file_size_mb,
                timestamp: None,
                exception: None,
                system: None,
                threads: None,
                modules: None,
                memory_regions: None,
            };
        }
    };

    // For minidumps, use the minidump crate for detailed parsing
    if signature == MINIDUMP_SIGNATURE {
        return analyze_minidump(path, file_size_mb);
    }

    // For kernel/full dumps, return basic info (detailed parsing requires more work)
    DumpAnalysisResult {
        success: true,
        dump_type: Some(dump_type.to_string()),
        dump_type_description: Some(dump_type_description.to_string()),
        error: None,
        file_size_mb,
        timestamp: None,
        exception: None,
        system: None,
        threads: None,
        modules: None,
        memory_regions: None,
    }
}

fn analyze_minidump(path: &Path, file_size_mb: Option<u64>) -> DumpAnalysisResult {
    let dump = match Minidump::read_path(path) {
        Ok(d) => d,
        Err(e) => return error(&format!("Failed to parse minidump: {}", e)),
    };

    // Extract exception info
    let exception = dump.get_stream::<minidump::MinidumpException>().ok().map(|exc| {
        let raw = exc.raw;
        let code = raw.exception_record.exception_code;
        ExceptionInfo {
            code: format!("0x{:08X}", code),
            code_hex: format!("{:08X}", code),
            description: exception_code_to_string(code),
            address: Some(format!("0x{:016X}", raw.exception_record.exception_address)),
            thread_id: Some(raw.thread_id),
        }
    });

    // Extract system info
    let system = dump.get_stream::<minidump::MinidumpSystemInfo>().ok().map(|sys| {
        SystemInfo {
            os_version: Some(format!(
                "Windows {}.{}.{}",
                sys.raw.major_version,
                sys.raw.minor_version,
                sys.raw.build_number
            )),
            cpu_arch: Some(cpu_arch_to_string(sys.raw.processor_architecture.into())),
            cpu_count: Some(sys.raw.number_of_processors as u32),
        }
    });

    // Extract thread list
    let threads = dump.get_stream::<MinidumpThreadList>().ok().map(|thread_list| {
        let crashing_thread_id = exception.as_ref().and_then(|e| e.thread_id);
        thread_list
            .threads
            .iter()
            .take(50) // Limit to 50 threads
            .map(|t| ThreadInfo {
                id: t.raw.thread_id,
                crashed: Some(t.raw.thread_id) == crashing_thread_id,
            })
            .collect()
    });

    // Extract module list
    let modules = dump.get_stream::<MinidumpModuleList>().ok().map(|module_list| {
        module_list
            .iter()
            .take(100) // Limit to 100 modules
            .map(|m| {
                let name = m.name.rsplit(['\\', '/']).next().unwrap_or(&m.name);
                ModuleInfo {
                    name: name.to_string(),
                    base_address: format!("0x{:016X}", m.raw.base_of_image),
                    size: m.raw.size_of_image as u64,
                    version: m.version.as_ref().map(|v| v.to_string()),
                }
            })
            .collect()
    });

    // Count memory regions if available
    let memory_regions = dump
        .get_stream::<minidump::MinidumpMemoryList>()
        .ok()
        .map(|mem| mem.iter().count() as u32);

    // Get timestamp
    let timestamp = dump
        .get_stream::<minidump::MinidumpMiscInfo>()
        .ok()
        .and_then(|misc| {
            misc.raw.process_create_time().map(|t| {
                chrono::DateTime::from_timestamp(t as i64, 0)
                    .map(|dt| dt.format("%Y-%m-%dT%H:%M:%SZ").to_string())
                    .unwrap_or_else(|| format!("timestamp: {}", t))
            })
        });

    DumpAnalysisResult {
        success: true,
        dump_type: Some("minidump".to_string()),
        dump_type_description: Some("Windows Minidump - crash/diagnostic dump".to_string()),
        error: None,
        file_size_mb,
        timestamp,
        exception,
        system,
        threads,
        modules,
        memory_regions,
    }
}

fn error(msg: &str) -> DumpAnalysisResult {
    DumpAnalysisResult {
        success: false,
        dump_type: None,
        dump_type_description: None,
        error: Some(msg.to_string()),
        file_size_mb: None,
        timestamp: None,
        exception: None,
        system: None,
        threads: None,
        modules: None,
        memory_regions: None,
    }
}

fn exception_code_to_string(code: u32) -> String {
    match code {
        0xC0000005 => "EXCEPTION_ACCESS_VIOLATION - Read/write to inaccessible memory".to_string(),
        0xC0000017 => "STATUS_NO_MEMORY - Out of memory".to_string(),
        0xC000001D => "EXCEPTION_ILLEGAL_INSTRUCTION - Invalid CPU instruction".to_string(),
        0xC0000025 => "EXCEPTION_NONCONTINUABLE_EXCEPTION".to_string(),
        0xC0000026 => "EXCEPTION_INVALID_DISPOSITION".to_string(),
        0xC000008C => "EXCEPTION_ARRAY_BOUNDS_EXCEEDED".to_string(),
        0xC000008D => "EXCEPTION_FLT_DENORMAL_OPERAND".to_string(),
        0xC000008E => "EXCEPTION_FLT_DIVIDE_BY_ZERO".to_string(),
        0xC0000090 => "EXCEPTION_FLT_INVALID_OPERATION".to_string(),
        0xC0000091 => "EXCEPTION_FLT_OVERFLOW".to_string(),
        0xC0000092 => "EXCEPTION_FLT_STACK_CHECK".to_string(),
        0xC0000093 => "EXCEPTION_FLT_UNDERFLOW".to_string(),
        0xC0000094 => "EXCEPTION_INT_DIVIDE_BY_ZERO".to_string(),
        0xC0000095 => "EXCEPTION_INT_OVERFLOW".to_string(),
        0xC0000096 => "EXCEPTION_PRIV_INSTRUCTION - Privileged instruction".to_string(),
        0xC00000FD => "EXCEPTION_STACK_OVERFLOW - Stack overflow".to_string(),
        0xC0000135 => "STATUS_DLL_NOT_FOUND - DLL not found".to_string(),
        0xC0000142 => "STATUS_DLL_INIT_FAILED - DLL initialization failed".to_string(),
        0xE0434352 => "CLR Exception (.NET)".to_string(),
        0xE06D7363 => "C++ Exception (MSVC)".to_string(),
        0x40010006 => "STATUS_BREAKPOINT - Debugger breakpoint".to_string(),
        0x80000003 => "EXCEPTION_BREAKPOINT".to_string(),
        0x80000004 => "EXCEPTION_SINGLE_STEP".to_string(),
        _ => format!("Unknown exception code 0x{:08X}", code),
    }
}

fn cpu_arch_to_string(arch: u32) -> String {
    match arch {
        0 => "x86 (32-bit)".to_string(),
        5 => "ARM".to_string(),
        6 => "IA-64 (Itanium)".to_string(),
        9 => "x64 (64-bit)".to_string(),
        12 => "ARM64".to_string(),
        _ => format!("Unknown ({})", arch),
    }
}
