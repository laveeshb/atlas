#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Atlas project.

.DESCRIPTION
    Cleans, restores, and builds the Atlas MCP server.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default is Debug.

.PARAMETER Clean
    Clean build outputs before building.

.PARAMETER Publish
    Create a self-contained publish for distribution.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Configuration Release

.EXAMPLE
    .\build.ps1 -Release -Publish
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter()]
    [switch]$Release,

    [Parameter()]
    [switch]$Clean,

    [Parameter()]
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

# If -Release switch is used, override Configuration
if ($Release) {
    $Configuration = 'Release'
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\Atlas.Server\Atlas.Server.csproj"
$OutputDir = Join-Path $RepoRoot "artifacts\$Configuration"

function Write-Status($Message) {
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Success($Message) {
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-Error($Message) {
    Write-Host "[-] $Message" -ForegroundColor Red
}

# Main
Write-Host ""
Write-Host "Atlas Build Script" -ForegroundColor White
Write-Host "==================" -ForegroundColor White
Write-Host ""
Write-Status "Configuration: $Configuration"
Write-Host ""

# Verify dotnet is available
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK not found"
    }
    Write-Status "Using .NET SDK $dotnetVersion"
}
catch {
    Write-Error ".NET SDK not found. Run .\scripts\install-prereqs.ps1 first."
    exit 1
}

# Clean
if ($Clean) {
    Write-Status "Cleaning..."
    dotnet clean $ProjectPath -c $Configuration --nologo -v q

    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
}

# Restore
Write-Status "Restoring packages..."
dotnet restore $ProjectPath --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Restore failed"
    exit 1
}

# Build
Write-Status "Building..."
dotnet build $ProjectPath -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

# Publish (optional)
if ($Publish) {
    Write-Status "Publishing self-contained executable..."

    $PublishDir = Join-Path $OutputDir "publish"

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $PublishDir `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed"
        exit 1
    }

    Write-Success "Published to: $PublishDir"
}

Write-Host ""
Write-Success "Build completed successfully!"
Write-Host ""

# Show output location
$buildOutput = Join-Path $RepoRoot "src\Atlas.Server\bin\$Configuration\net8.0-windows"
Write-Host "Output: $buildOutput" -ForegroundColor Gray

if ($Publish) {
    Write-Host "Publish: $PublishDir" -ForegroundColor Gray
}

Write-Host ""
