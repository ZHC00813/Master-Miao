[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'Blankedge.SWHoleTools.sln'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$interopPath = Join-Path $projectRoot 'dependencies\SolidWorks.Interop'
$localState = Join-Path $projectRoot '.dev-local'
$portableProfile = Join-Path $projectRoot 'portable-profile'

$requiredFiles = @(
    $solutionPath,
    $nugetConfig,
    (Join-Path $projectRoot 'START-HERE.md'),
    (Join-Path $projectRoot 'PROJECT-CONTINUATION.md'),
    (Join-Path $portableProfile 'NuGet\NuGet.Config'),
    (Join-Path $interopPath 'SolidWorks.Interop.sldworks.dll'),
    (Join-Path $interopPath 'SolidWorks.Interop.swconst.dll'),
    (Join-Path $interopPath 'SolidWorks.Interop.swpublished.dll')
)

$missing = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "Development pack is incomplete. Missing:`n$($missing -join "`n")"
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw '.NET SDK/MSBuild was not found. Install Visual Studio 2022 or Build Tools with the .NET Framework 4.8 targeting pack.'
}

New-Item -ItemType Directory -Path $localState -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $localState 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $localState 'nuget-packages'
# Isolate NuGet's mandatory per-user configuration lookup inside this project.
# The script is normally run in its own powershell.exe process, so this does not
# persistently change the user's APPDATA setting.
$env:APPDATA = $portableProfile
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = 'true'

Write-Host "Project: $projectRoot"
Write-Host "Configuration: $Configuration|x64"
Write-Host "Interop fallback: $interopPath"
Write-Host 'This script does not start SOLIDWORKS or change add-in registration.'

& $dotnet.Source restore $solutionPath --configfile $nugetConfig --property:Platform=x64 --property:SolidWorksInteropDir="$interopPath"
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed with exit code $LASTEXITCODE."
}

& $dotnet.Source build $solutionPath --no-restore --configuration $Configuration --property:Platform=x64 --property:SolidWorksInteropDir="$interopPath"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not $SkipTests) {
    $testExe = Join-Path $projectRoot "tests\Blankedge.SWHoleTools.Tests\bin\x64\$Configuration\net48\Blankedge.SWHoleTools.Tests.exe"
    if (-not (Test-Path -LiteralPath $testExe -PathType Leaf)) {
        throw "Test executable was not produced: $testExe"
    }

    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "Pure-logic tests failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Development pack bootstrap completed successfully.'
