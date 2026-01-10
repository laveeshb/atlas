# Integration test for remote debug tools
# Tests against a running remote.exe session

param(
    [string]$ConnectionString = "localhost/TestSession"
)

$ErrorActionPreference = "Stop"

# Add debugging tools to PATH
$env:Path = "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64;$env:Path"

Write-Host "=== Remote Debug Integration Test ===" -ForegroundColor Cyan
Write-Host "Connection: $ConnectionString" -ForegroundColor Gray
Write-Host ""

$parts = $ConnectionString -split '/'
$server = $parts[0]
$session = $parts[1]

# Helper function to run a command without quitting
function Invoke-DebugCommand {
    param([string]$Command)
    
    # Start remote.exe client
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "remote.exe"
    $psi.Arguments = "/c $server $session"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    
    $proc = [System.Diagnostics.Process]::Start($psi)
    
    # Wait for initial connection
    Start-Sleep -Milliseconds 500
    
    # Send command
    $proc.StandardInput.WriteLine($Command)
    $proc.StandardInput.Flush()
    
    # Wait for output
    Start-Sleep -Milliseconds 1000
    
    # Read available output
    $output = ""
    while (!$proc.StandardOutput.EndOfStream) {
        $line = $proc.StandardOutput.ReadLine()
        $output += "$line`n"
        if ($line -match "^\d+:\d+>") { break }
    }
    
    # Kill client without sending 'q' - keeps server alive!
    $proc.Kill()
    
    return $output
}

# Test 1: Connect and run vertarget
Write-Host "Test 1: Connect and run 'vertarget'..." -ForegroundColor Yellow

$output = Invoke-DebugCommand "vertarget"
Write-Host ($output | Select-Object -First 15) -ForegroundColor Gray

if ($output -match "Windows|Dump|Debug") {
    Write-Host "  PASSED - Connected successfully" -ForegroundColor Green
} else {
    Write-Host "  FAILED - Could not connect" -ForegroundColor Red
}

# Verify session still running
Write-Host ""
Write-Host "Checking session is still alive..." -ForegroundColor Yellow
$query = remote.exe /q localhost 2>&1
if ($query -match "TestSession") {
    Write-Host "  Session still running!" -ForegroundColor Green
} else {
    Write-Host "  Session died!" -ForegroundColor Red
    exit 1
}

# Test 2: Run lm (list modules)
Write-Host ""
Write-Host "Test 2: Run 'lm' (list modules)..." -ForegroundColor Yellow

$output = Invoke-DebugCommand "lm"
$moduleLines = ($output -split "`n" | Where-Object { $_ -match "^\s*[0-9a-f]" })
Write-Host "  Found $($moduleLines.Count) modules" -ForegroundColor Gray

if ($moduleLines.Count -gt 0) {
    Write-Host "  PASSED" -ForegroundColor Green
    Write-Host ($moduleLines | Select-Object -First 5) -ForegroundColor Gray
}

# Verify session STILL running
Write-Host ""
Write-Host "Final check - session still alive..." -ForegroundColor Yellow
$query = remote.exe /q localhost 2>&1
if ($query -match "TestSession") {
    Write-Host "  Session still running - SUCCESS!" -ForegroundColor Green
} else {
    Write-Host "  Session died!" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Integration Test Complete ===" -ForegroundColor Cyan
