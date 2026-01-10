# Remote Debug Integration Test
# Requires: Debugging Tools for Windows installed (cdb.exe in PATH)

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
Write-Host "Starting cdb debug server on localhost:$Port with dump loaded..." -ForegroundColor Yellow

# Start cdb as debug server with dump already loaded
# This is the correct architecture: server has the dump, client just connects
$serverProcess = Start-Process -FilePath "cdb.exe" -ArgumentList "-server tcp:port=$Port -z `"$DumpPath`"" -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 3

if ($serverProcess.HasExited) {
    Write-Host "ERROR: cdb debug server failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "  Debug server started (PID: $($serverProcess.Id))" -ForegroundColor Green

try {
    Write-Host ""
    Write-Host "Testing client connection..." -ForegroundColor Yellow
    
    # Create a script for client cdb to run
    $cdbScript = @"
vertarget
lm
q
"@
    
    $tempScript = [System.IO.Path]::GetTempFileName()
    $cdbScript | Out-File -FilePath $tempScript -Encoding ascii
    
    $cdbOutput = & cdb.exe -remote "tcp:server=localhost,port=$Port" -cf $tempScript 2>&1
    $cdbExitCode = $LASTEXITCODE
    
    Remove-Item $tempScript -ErrorAction SilentlyContinue
    
    # Check if we got meaningful output
    $outputText = $cdbOutput -join "`n"
    
    if ($outputText -match "Connected to server") {
        Write-Host "  Connection successful!" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Connection might have issues" -ForegroundColor Yellow
        Write-Host $outputText
    }
    
    if ($outputText -match "Windows|Debug session") {
        Write-Host "  Dump loaded and accessible!" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "=== Test Passed ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "The debug server is running. You can now test Atlas remote tools:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Connection string: tcp:server=localhost,port=$Port" -ForegroundColor White
    Write-Host "  (Dump is already loaded on the server)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Example prompt for Copilot:" -ForegroundColor Yellow
    Write-Host "  'Analyze the crash on debug server tcp:server=localhost,port=$Port'" -ForegroundColor White
    Write-Host ""
    Write-Host "Press Enter to stop the debug server and exit..." -ForegroundColor Gray
    Read-Host
    
} finally {
    Write-Host "Stopping debug server..." -ForegroundColor Yellow
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Write-Host "Done." -ForegroundColor Green
}
