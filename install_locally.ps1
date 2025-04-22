<#
.SYNOPSIS
    Pack a .NET solution (or current folder), generate a LocalOnly.NuGet.Config
    inside the feed directory (so it won't affect the solution), and install
    (or reinstall) the Uno.Check global tool from that feed.

.PARAMETER Solution
    Optional path to a .sln file or project folder.
    If omitted or empty, `dotnet pack` will run on the current directory.

.PARAMETER LocalFeed
    Optional directory to emit nupkg files into.
    If omitted or empty, defaults to `LocalFeed` in the script folder.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Solution = "",

    [Parameter(Position = 1)]
    [string]$LocalFeed = ""
)

# Fail on any error
$ErrorActionPreference = 'Stop'

$PackageId = 'Uno.Check'

# Determine script directory and set defaults
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

if (-not $Solution) {
    Write-Host "No solution specified → packing current directory"
    $packTarget = "."
} else {
    $packTarget = $Solution
}

if (-not $LocalFeed) {
    $LocalFeed = "LocalFeed"
    Write-Host "No feed directory specified → using default '$LocalFeed'"
}

# Ensure feed directory exists
if (-not (Test-Path $LocalFeed)) {
    Write-Host "Creating feed directory: $LocalFeed"
    New-Item -ItemType Directory -Path $LocalFeed | Out-Null
}

Write-Host "Packing '$packTarget' to '$LocalFeed'..."
dotnet pack $packTarget -c Release -o $LocalFeed

# Place NuGet.Config inside the feed directory
$configFile = Join-Path $LocalFeed 'LocalOnly.NuGet.Config'
$feedUri    = (Resolve-Path -Path $LocalFeed).ProviderPath.TrimEnd('\','/')

Write-Host "Generating NuGet.Config at '$configFile'..."
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <!-- only your local folder -->
    <add key="LocalFeed" value="$feedUri" />
  </packageSources>
</configuration>
"@ | Out-File -FilePath $configFile -Encoding utf8

# If Uno.Check is already installed, uninstall it first
$installed = dotnet tool list --global | Select-String -Pattern "^\s*$PackageId\s"
if ($installed) {
    Write-Host "Detected existing installation of '$PackageId'. Uninstalling..."
    dotnet tool uninstall --global $PackageId
}

Write-Host "Installing tool '$PackageId' using only '$configFile'..."
dotnet tool install --global $PackageId `
    --configfile $configFile `
    --ignore-failed-sources

Write-Host "`nDone. '$PackageId' is installed from your local feed."
Pop-Location

# Prevent the script window from closing immediately
Write-Host "`nPress Enter to exit..."
Read-Host | Out-Null
