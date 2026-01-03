#Requires -Version 5.1
<#
.SYNOPSIS
    Installs prerequisites for building Atlas.

.DESCRIPTION
    Checks for required dependencies and installs them if missing.
    Currently checks for .NET 8.0 SDK.

.EXAMPLE
    .\install-prereqs.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Write-Status($Message) {
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Success($Message) {
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-Warning($Message) {
    Write-Host "[!] $Message" -ForegroundColor Yellow
}

function Write-Error($Message) {
    Write-Host "[-] $Message" -ForegroundColor Red
}

function Test-DotNetSdk {
    param([string]$RequiredVersion = "8.0")

    try {
        $sdks = dotnet --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $false
        }

        foreach ($sdk in $sdks) {
            if ($sdk -match "^$RequiredVersion") {
                return $true
            }
        }
        return $false
    }
    catch {
        return $false
    }
}

function Install-DotNetSdk {
    param([string]$Version = "8.0")

    Write-Status "Downloading .NET $Version SDK installer..."

    $installerUrl = "https://dot.net/v1/dotnet-install.ps1"
    $installerPath = Join-Path $env:TEMP "dotnet-install.ps1"

    try {
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing

        Write-Status "Installing .NET $Version SDK..."
        & $installerPath -Channel $Version -InstallDir "$env:ProgramFiles\dotnet"

        # Refresh PATH
        $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"

        Write-Success ".NET $Version SDK installed successfully"
        Write-Warning "You may need to restart your terminal for PATH changes to take effect"
    }
    finally {
        if (Test-Path $installerPath) {
            Remove-Item $installerPath -Force
        }
    }
}

# Main
Write-Host ""
Write-Host "Atlas Prerequisites Installer" -ForegroundColor White
Write-Host "==============================" -ForegroundColor White
Write-Host ""

# Check Windows version
$os = [System.Environment]::OSVersion
if ($os.Platform -ne 'Win32NT') {
    Write-Error "Atlas requires Windows. Current platform: $($os.Platform)"
    exit 1
}

$winVer = [System.Environment]::OSVersion.Version
Write-Status "Windows version: $($winVer.Major).$($winVer.Minor).$($winVer.Build)"

if ($winVer.Major -lt 10) {
    Write-Warning "Atlas is designed for Windows 10/11 or Windows Server 2016+. Your version may not be fully supported."
}

# Check .NET SDK
Write-Status "Checking for .NET 8.0 SDK..."

if (Test-DotNetSdk -RequiredVersion "8.0") {
    $currentSdk = (dotnet --version 2>$null)
    Write-Success ".NET SDK $currentSdk is installed"
}
else {
    Write-Warning ".NET 8.0 SDK not found"

    $install = Read-Host "Would you like to install .NET 8.0 SDK? (y/n)"
    if ($install -eq 'y' -or $install -eq 'Y') {
        Install-DotNetSdk -Version "8.0"
    }
    else {
        Write-Host ""
        Write-Host "To install manually, download from:" -ForegroundColor White
        Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
        exit 1
    }
}

Write-Host ""
Write-Success "All prerequisites satisfied!"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  .\scripts\build.ps1          # Build the project" -ForegroundColor Gray
Write-Host "  .\scripts\build.ps1 -Release # Build for release" -ForegroundColor Gray
Write-Host ""
