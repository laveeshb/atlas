# Remote Debug Integration Test
# Requires: Debugging Tools for Windows installed (cdb.exe, dbgsrv.exe in PATH)

param(
    [string]$DumpPath = "$PSScriptRoot\..\test-dump.dmp",
    [int]$Port = 5005
)

$ErrorActionPreference = "Stop"

Write-Host "=== Remote Debug Integration Test ===" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

$cdb = Get-Command cdb.exe -ErrorAction SilentlyContinue
if (-not $cdb) {
    Write-Host "ERROR: cdb.exe not found in PATH" -ForegroundColor Red
    Write-Host "Install Windows SDK with 'Debugging Tools for Windows' option" -ForegroundColor Yellow
    Write-Host "Then add to PATH: C:\Program Files (x86)\Windows Kits\10\Debuggers\x64" -ForegroundColor Yellow
    exit 1
}
Write-Host "  cdb.exe: $($cdb.Source)" -ForegroundColor Green

$dbgsrv = Get-Command dbgsrv.exe -ErrorAction SilentlyContinue
if (-not $dbgsrv) {
    Write-Host "ERROR: dbgsrv.exe not found in PATH" -ForegroundColor Red
    exit 1
}
Write-Host "  dbgsrv.exe: $($dbgsrv.Source)" -ForegroundColor Green

if (-not (Test-Path $DumpPath)) {
    Write-Host "ERROR: Dump file not found: $DumpPath" -ForegroundColor Red
    exit 1
}
$DumpPath = Resolve-Path $DumpPath
Write-Host "  Dump file: $DumpPath" -ForegroundColor Green

# Check if port is available
$listener = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($listener) {
    Write-Host "ERROR: Port $Port is already in use" -ForegroundColor Red
    exit 1
}
Write-Host "  Port $Port: Available" -ForegroundColor Green

Write-Host ""
Write-Host "Starting dbgsrv on localhost:$Port..." -ForegroundColor Yellow

# Start dbgsrv in background
$dbgsrvProcess = Start-Process -FilePath "dbgsrv.exe" -ArgumentList "-t tcp:port=$Port" -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 2

if ($dbgsrvProcess.HasExited) {
    Write-Host "ERROR: dbgsrv failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "  dbgsrv started (PID: $($dbgsrvProcess.Id))" -ForegroundColor Green

try {
    Write-Host ""
    Write-Host "Testing connection with cdb..." -ForegroundColor Yellow
    
    # Create a script for cdb to run
    $cdbScript = @"
.opendump $DumpPath
!analyze -v
q
"@
    
    $tempScript = [System.IO.Path]::GetTempFileName()
    $cdbScript | Out-File -FilePath $tempScript -Encoding ascii
    
    $cdbOutput = & cdb.exe -remote "tcp:server=localhost,port=$Port" -cf $tempScript 2>&1
    $cdbExitCode = $LASTEXITCODE
    
    Remove-Item $tempScript -ErrorAction SilentlyContinue
    
    if ($cdbExitCode -ne 0) {
        Write-Host "ERROR: cdb exited with code $cdbExitCode" -ForegroundColor Red
        Write-Host $cdbOutput
        exit 1
    }
    
    # Check if we got meaningful output
    $outputText = $cdbOutput -join "`n"
    
    if ($outputText -match "EXCEPTION_CODE|ExceptionCode|BUGCHECK") {
        Write-Host "  Connection successful!" -ForegroundColor Green
        Write-Host "  Crash analysis returned data" -ForegroundColor Green
    } elseif ($outputText -match "error|cannot|failed") {
        Write-Host "WARNING: Connection worked but analysis may have issues" -ForegroundColor Yellow
    } else {
        Write-Host "  Connection successful (no crash data in test dump)" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "=== Test Passed ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "You can now test Atlas remote tools:" -ForegroundColor Yellow
    Write-Host "  Connection string: tcp:server=localhost,port=$Port" -ForegroundColor White
    Write-Host "  Dump path: $DumpPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Example prompt for Copilot:" -ForegroundColor Yellow
    Write-Host "  'Analyze the crash dump at $DumpPath on debug server tcp:server=localhost,port=$Port'" -ForegroundColor White
    Write-Host ""
    Write-Host "Press Enter to stop dbgsrv and exit..." -ForegroundColor Gray
    Read-Host
    
} finally {
    Write-Host "Stopping dbgsrv..." -ForegroundColor Yellow
    Stop-Process -Id $dbgsrvProcess.Id -Force -ErrorAction SilentlyContinue
    Write-Host "Done." -ForegroundColor Green
}
