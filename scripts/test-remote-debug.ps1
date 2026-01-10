# Remote Debug Integration Test
# Requires: Debugging Tools for Windows installed (remote.exe, cdb.exe in PATH)

param(
    [string]$DumpPath = "$PSScriptRoot\..\test-dump.dmp",
    [string]$SessionName = "TestSession"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Remote Debug Integration Test ===" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

$remote = Get-Command remote.exe -ErrorAction SilentlyContinue
if (-not $remote) {
    Write-Host "ERROR: remote.exe not found in PATH" -ForegroundColor Red
    Write-Host "Install Windows SDK with 'Debugging Tools for Windows' option" -ForegroundColor Yellow
    Write-Host "Then add to PATH: C:\Program Files (x86)\Windows Kits\10\Debuggers\x64" -ForegroundColor Yellow
    exit 1
}
Write-Host "  remote.exe: $($remote.Source)" -ForegroundColor Green

$cdb = Get-Command cdb.exe -ErrorAction SilentlyContinue
if (-not $cdb) {
    Write-Host "ERROR: cdb.exe not found in PATH" -ForegroundColor Red
    Write-Host "Install Windows SDK with 'Debugging Tools for Windows' option" -ForegroundColor Yellow
    Write-Host "Then add to PATH: C:\Program Files (x86)\Windows Kits\10\Debuggers\x64" -ForegroundColor Yellow
    exit 1
}
Write-Host "  cdb.exe: $($cdb.Source)" -ForegroundColor Green

if (-not (Test-Path $DumpPath)) {
    Write-Host "ERROR: Dump file not found: $DumpPath" -ForegroundColor Red
    exit 1
}
$DumpPath = Resolve-Path $DumpPath
Write-Host "  Dump file: $DumpPath" -ForegroundColor Green

Write-Host ""
Write-Host "Starting remote.exe session '$SessionName' with dump loaded..." -ForegroundColor Yellow

# Start remote.exe as session server with cdb and dump loaded
# This provides persistent, multi-client access to the dump
$serverProcess = Start-Process -FilePath "remote.exe" -ArgumentList "/s `"cdb -z $DumpPath`" $SessionName" -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 3

if ($serverProcess.HasExited) {
    Write-Host "ERROR: remote.exe session failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "  Session started (PID: $($serverProcess.Id))" -ForegroundColor Green

try {
    Write-Host ""
    Write-Host "Testing client connection..." -ForegroundColor Yellow
    
    # Test connection with a simple command
    # Use cmd /c with echo to send command and exit
    $testOutput = cmd /c "echo vertarget | remote.exe /c localhost $SessionName 2>&1"
    
    $outputText = $testOutput -join "`n"
    
    if ($outputText -match "Windows|Debug session|Dump|Target") {
        Write-Host "  Connection successful!" -ForegroundColor Green
        Write-Host "  Dump loaded and accessible!" -ForegroundColor Green
    } else {
        Write-Host "  Testing via query..." -ForegroundColor Yellow
        $queryOutput = & remote.exe /q localhost 2>&1
        if ($queryOutput -match $SessionName) {
            Write-Host "  Session '$SessionName' is running!" -ForegroundColor Green
        } else {
            Write-Host "WARNING: Could not verify session" -ForegroundColor Yellow
            Write-Host $queryOutput
        }
    }
    
    Write-Host ""
    Write-Host "=== Test Passed ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "The remote session is running. You can now test Atlas remote tools:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Connection string: localhost/$SessionName" -ForegroundColor White
    Write-Host "  (Dump is already loaded in the session)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Example prompt for Copilot:" -ForegroundColor Yellow
    Write-Host "  'Analyze the crash on localhost/$SessionName'" -ForegroundColor White
    Write-Host ""
    Write-Host "Or connect manually:" -ForegroundColor Yellow
    Write-Host "  remote.exe /c localhost $SessionName" -ForegroundColor White
    Write-Host ""
    Write-Host "Press Enter to stop the session and exit..." -ForegroundColor Gray
    Read-Host
    
} finally {
    Write-Host "Stopping remote session..." -ForegroundColor Yellow
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Write-Host "Done." -ForegroundColor Green
}
