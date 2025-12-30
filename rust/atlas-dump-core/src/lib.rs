use serde::Serialize;
use std::ffi::{CStr, CString};
use std::os::raw::c_char;

mod parser;

#[derive(Serialize)]
pub struct DumpAnalysisResult {
    pub success: bool,
    pub dump_type: Option<String>,
    pub dump_type_description: Option<String>,
    pub error: Option<String>,
    pub file_size_mb: Option<u64>,
    pub timestamp: Option<String>,
    pub exception: Option<ExceptionInfo>,
    pub system: Option<SystemInfo>,
    pub threads: Option<Vec<ThreadInfo>>,
    pub modules: Option<Vec<ModuleInfo>>,
    pub memory_regions: Option<u32>,
}

#[derive(Serialize)]
pub struct ExceptionInfo {
    pub code: String,
    pub code_hex: String,
    pub description: String,
    pub address: Option<String>,
    pub thread_id: Option<u32>,
}

#[derive(Serialize)]
pub struct SystemInfo {
    pub os_version: Option<String>,
    pub cpu_arch: Option<String>,
    pub cpu_count: Option<u32>,
}

#[derive(Serialize)]
pub struct ThreadInfo {
    pub id: u32,
    pub crashed: bool,
}

#[derive(Serialize)]
pub struct ModuleInfo {
    pub name: String,
    pub base_address: String,
    pub size: u64,
    pub version: Option<String>,
}

/// Analyze a dump file and return JSON result
/// 
/// # Safety
/// `path` must be a valid null-terminated C string
#[no_mangle]
pub unsafe extern "C" fn analyze_dump(path: *const c_char) -> *mut c_char {
    let path_str = match CStr::from_ptr(path).to_str() {
        Ok(s) => s,
        Err(_) => {
            return error_result("Invalid path encoding");
        }
    };

    let result = parser::analyze(path_str);
    let json = serde_json::to_string(&result).unwrap_or_else(|_| {
        r#"{"success":false,"error":"JSON serialization failed"}"#.to_string()
    });

    CString::new(json).unwrap().into_raw()
}

/// Free a result string returned by analyze_dump
/// 
/// # Safety
/// `ptr` must have been returned by `analyze_dump`
#[no_mangle]
pub unsafe extern "C" fn free_result(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(CString::from_raw(ptr));
    }
}

fn error_result(msg: &str) -> *mut c_char {
    let result = DumpAnalysisResult {
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
    };
    let json = serde_json::to_string(&result).unwrap();
    CString::new(json).unwrap().into_raw()
}
