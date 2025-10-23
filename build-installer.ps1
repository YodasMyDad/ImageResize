# Build script for ImageResize Context Menu Application
# This script builds the application and creates an installer

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('x64', 'x86', 'ARM64', 'Both')]
    [string]$Platform = 'Both',
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipInnoSetup
)

$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "ImageResize.ContextMenu"
$InstallerDir = Join-Path $ScriptDir "publish\installer"

# Determine which platforms to build
$PlatformsToBuild = @()
if ($Platform -eq 'Both') {
    $PlatformsToBuild = @('x86', 'x64')
} else {
    $PlatformsToBuild = @($Platform)
}

Write-Host "Building ImageResize Context Menu Application..." -ForegroundColor Cyan
Write-Host "Platform(s): $($PlatformsToBuild -join ', ')" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Check if .NET SDK is installed
Write-Host "Checking for .NET SDK..." -ForegroundColor Cyan
try {
    $dotnetVersion = dotnet --version
    Write-Host "Found .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "ERROR: .NET SDK not found. Please install .NET 9.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Clean installer directory
Write-Host ""
Write-Host "Cleaning installer directory..." -ForegroundColor Cyan
if (Test-Path $InstallerDir) {
    Remove-Item -Path "$InstallerDir\*.exe" -Force -ErrorAction SilentlyContinue
    Write-Host "Cleaned installer directory: $InstallerDir" -ForegroundColor Green
}

# Restore NuGet packages once (shared for all platforms)
Write-Host ""
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore "$ProjectDir\ImageResize.ContextMenu.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to restore NuGet packages" -ForegroundColor Red
    exit 1
}

# Build each platform
$BuiltPlatforms = @()
foreach ($CurrentPlatform in $PlatformsToBuild) {
    Write-Host ""
    Write-Host "======================================" -ForegroundColor Magenta
    Write-Host "Building for platform: $CurrentPlatform" -ForegroundColor Magenta
    Write-Host "======================================" -ForegroundColor Magenta
    Write-Host ""
    
    $OutputDir = Join-Path $ScriptDir "publish\$CurrentPlatform"
    
    # Clean project (bin/obj) and previous publish output
    Write-Host "Cleaning project and previous publish output..." -ForegroundColor Cyan
    # dotnet clean clears bin/obj
    dotnet clean "$ProjectDir\ImageResize.ContextMenu.csproj" -c $Configuration "/p:Platform=$CurrentPlatform"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet clean failed for $CurrentPlatform" -ForegroundColor Red
        exit 1
    }
    if (Test-Path $OutputDir) {
        Remove-Item -Path $OutputDir -Recurse -Force
        Write-Host "Cleaned publish output: $OutputDir" -ForegroundColor Green
    }

    # Build context menu project (fail fast before publish)
    Write-Host ""
    Write-Host "Building project..." -ForegroundColor Cyan
    dotnet build "$ProjectDir\ImageResize.ContextMenu.csproj" -c $Configuration "/p:Platform=$CurrentPlatform"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed for $CurrentPlatform" -ForegroundColor Red
        exit 1
    }

    # Build and publish
    Write-Host ""
    Write-Host "Publishing application..." -ForegroundColor Cyan
    $publishArgs = @(
        "publish",
        "$ProjectDir\ImageResize.ContextMenu.csproj",
        "-c", $Configuration,
        "-r", "win-$($CurrentPlatform.ToLower())",
        "-o", $OutputDir,
        "--self-contained", "true",
        "/p:Platform=$CurrentPlatform",
        "/p:PublishSingleFile=false",
        "/p:PublishReadyToRun=false",
        "/p:IncludeNativeLibrariesForSelfExtract=true"
    )

    dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Publish failed for $CurrentPlatform" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "Build completed successfully for $CurrentPlatform!" -ForegroundColor Green
    Write-Host "Output location: $OutputDir" -ForegroundColor Cyan
    
    $BuiltPlatforms += $CurrentPlatform
}

# Build installer with Inno Setup
if (-not $SkipInnoSetup) {
    Write-Host ""
    Write-Host "======================================" -ForegroundColor Magenta
    Write-Host "Building installers with Inno Setup" -ForegroundColor Magenta
    Write-Host "======================================" -ForegroundColor Magenta
    Write-Host ""
    
    # Try to find Inno Setup
    $InnoSetupPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    
    $ISCC = $null
    foreach ($path in $InnoSetupPaths) {
        if (Test-Path $path) {
            $ISCC = $path
            break
        }
    }
    
    if ($null -eq $ISCC) {
        Write-Host "WARNING: Inno Setup not found. Installer not created." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To create an installer:" -ForegroundColor Yellow
        Write-Host "  1. Download Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor White
        Write-Host "  2. Install it (default location is fine)" -ForegroundColor White
        Write-Host "  3. Run this script again" -ForegroundColor White
        Write-Host ""
        Write-Host "Or use the PowerShell scripts:" -ForegroundColor Yellow
        Write-Host "  .\install.ps1 -Platform <platform>" -ForegroundColor White
    } else {
        Write-Host "Found Inno Setup at: $ISCC" -ForegroundColor Green
        Write-Host ""
        
        # Build installer for each platform
        $InnoScript = Join-Path $ProjectDir "Installer.iss"
        $InstallerSuccessCount = 0
        
        foreach ($CurrentPlatform in $BuiltPlatforms) {
            Write-Host "Building installer for $CurrentPlatform..." -ForegroundColor Cyan
            
            # Run Inno Setup compiler with platform define
            & $ISCC "/DPlatform=$CurrentPlatform" $InnoScript
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Installer created successfully for $CurrentPlatform!" -ForegroundColor Green
                $InstallerSuccessCount++
            } else {
                Write-Host "WARNING: Installer build failed for $CurrentPlatform" -ForegroundColor Yellow
            }
            Write-Host ""
        }
        
        if ($InstallerSuccessCount -gt 0) {
            Write-Host ""
            Write-Host "Installer location: $InstallerDir" -ForegroundColor Cyan
            
            # Show installer files
            $installerFiles = Get-ChildItem -Path $InstallerDir -Filter "*.exe"
            if ($installerFiles) {
                Write-Host ""
                Write-Host "Installer file(s):" -ForegroundColor Cyan
                foreach ($file in $installerFiles) {
                    Write-Host "  $($file.FullName)" -ForegroundColor White
                    Write-Host "  Size: $([math]::Round($file.Length / 1MB, 2)) MB" -ForegroundColor Gray
                }
            }
        }
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
if (Test-Path "$InstallerDir\*.exe") {
    Write-Host "  1. Run the installer exe to install the application" -ForegroundColor White
    Write-Host "  2. Right-click any image file to see 'Resize Images...' option" -ForegroundColor White
} else {
    Write-Host "  1. Install Inno Setup and run this script again, OR" -ForegroundColor White
    Write-Host "  2. Run install.ps1 as Administrator to install manually" -ForegroundColor White
}
Write-Host ""

