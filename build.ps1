param(
    [string]$SolidWorksApiPath = $env:MASTER_MIAO_SW_API,
    [string]$OutputDirectory = 'build',
    [string]$DotNetPath = 'dotnet',
    [string]$PackageSource
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$swApi = if ([string]::IsNullOrWhiteSpace($SolidWorksApiPath)) { Join-Path $projectRoot 'modules\HoleTools\dependencies\SolidWorks.Interop' } else { [IO.Path]::GetFullPath($SolidWorksApiPath) }
$output = Join-Path $projectRoot $OutputDirectory
$macroSource = Join-Path $projectRoot 'macro\StepMacro.cs'
$iconSource = Join-Path $projectRoot 'assets\MasterMiao-logo.png'
$iconOutput = Join-Path $output 'MasterMiao.ico'

if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
foreach ($assembly in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
    $assemblyPath = Join-Path $swApi $assembly
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Missing SolidWorks API assembly: $assemblyPath. Use -SolidWorksApiPath '<api\redist folder>' or MASTER_MIAO_SW_API to configure the path."
    }
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $projectRoot 'tools\BuildIcon.ps1') -Source $iconSource -Destination $iconOutput

$macroArguments = @(
    '/nologo', '/target:library', '/platform:x64', '/optimize+',
    ('/out:' + (Join-Path $output 'MasterMiao.StepMacro.dll')),
    '/reference:System.dll', '/reference:System.Core.dll',
    ('/reference:' + (Join-Path $swApi 'SolidWorks.Interop.sldworks.dll')),
    ('/reference:' + (Join-Path $swApi 'SolidWorks.Interop.swconst.dll')),
    $macroSource
)

& $compiler $macroArguments
if ($LASTEXITCODE -ne 0) { throw "Macro build failed with exit code $LASTEXITCODE." }

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:APPDATA = Join-Path $projectRoot 'modules\HoleTools\portable-profile'
$project = Join-Path $projectRoot 'MasterMiao.csproj'
$restore = @('restore', $project, ('--property:SolidWorksInteropDir=' + $swApi), '--property:Platform=x64')
if ($PackageSource) { $restore += @('--source', $PackageSource) }
& $DotNetPath $restore
if ($LASTEXITCODE -ne 0) { throw 'Module restore failed. A .NET SDK and .NET Framework 4.8 reference assemblies are required.' }
& $DotNetPath build $project --no-restore -c Release --property:Platform=x64 "--property:SolidWorksInteropDir=$swApi" "--property:OutputPath=$output\"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
foreach ($assembly in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
    $sourceAssembly = Join-Path $swApi $assembly
    $targetAssembly = Join-Path $output $assembly
    $sameAssembly = (Test-Path -LiteralPath $targetAssembly -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $targetAssembly -Algorithm SHA256).Hash)
    if (-not $sameAssembly) { Copy-Item -LiteralPath $sourceAssembly -Destination $targetAssembly -Force }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'MasterMiao.exe.config') -Destination $output -Force
Write-Host "Build completed: $(Join-Path $output 'MasterMiao.exe')"
