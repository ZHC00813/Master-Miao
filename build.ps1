$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$swApi = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
$output = Join-Path $projectRoot 'build'
$macroSource = Join-Path $projectRoot 'macro\StepMacro.cs'
$iconSource = Join-Path $projectRoot 'assets\MasterMiao-logo.png'
$iconOutput = Join-Path $output 'MasterMiao.ico'

if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
if (-not (Test-Path -LiteralPath (Join-Path $swApi 'SolidWorks.Interop.sldworks.dll'))) { throw 'SolidWorks API interop assemblies were not found.' }
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

$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    ('/out:' + (Join-Path $output 'MasterMiao.exe')),
    ('/win32icon:' + $iconOutput),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    ('/reference:' + (Join-Path $swApi 'SolidWorks.Interop.sldworks.dll')),
    ('/reference:' + (Join-Path $swApi 'SolidWorks.Interop.swconst.dll'))
) + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE."
}
Copy-Item -LiteralPath (Join-Path $swApi 'SolidWorks.Interop.sldworks.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $swApi 'SolidWorks.Interop.swconst.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'MasterMiao.exe.config') -Destination $output -Force
Write-Host "Build completed: $(Join-Path $output 'MasterMiao.exe')"
