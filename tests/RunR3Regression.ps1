param([Parameter(Mandatory=$true)][string]$OutputDirectory, [string]$BinaryDirectory, [switch]$UiOnly)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $testRoot) { throw 'Use a new test directory; existing data is never overwritten.' }
if (-not $BinaryDirectory) { $BinaryDirectory = Join-Path $sourceRoot 'build' }
$binaryRoot = [IO.Path]::GetFullPath($BinaryDirectory)
$runtime = Join-Path $testRoot 'runtime'
New-Item -ItemType Directory -Path $runtime | Out-Null
foreach ($name in @('MasterMiao.exe','MasterMiao.exe.config','MasterMiao.ico','MasterMiao.StepMacro.dll','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll')) {
    Copy-Item -LiteralPath (Join-Path $binaryRoot $name) -Destination $runtime
}
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$application = Join-Path $runtime 'MasterMiao.exe'
function Compile-Test([string]$name, [string[]]$references, [string[]]$extraSources = @()) {
    $arguments = @('/nologo','/target:exe','/platform:x64',('/out:' + (Join-Path $runtime ($name + '.exe'))), '/reference:System.dll','/reference:System.Core.dll')
    $arguments += $references | ForEach-Object { '/reference:' + $_ }
    & $compiler ($arguments + (Join-Path $sourceRoot ('tests\' + $name + '.cs')) + $extraSources)
    if ($LASTEXITCODE -ne 0) { throw "$name compilation failed" }
}
function Run-Test([string]$name, [string[]]$arguments = @()) {
    & (Join-Path $runtime ($name + '.exe')) $arguments | Tee-Object -FilePath (Join-Path $testRoot ($name + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw "$name failed" }
}
# The initial smoke run must precede tests that deliberately create recovery data.
$startup = Start-Process -FilePath $application -ArgumentList @('--startup-selftest', ('"' + (Join-Path $testRoot 'startup.png') + '"')) -WindowStyle Hidden -PassThru
if (-not $startup.WaitForExit(20000)) { throw "Startup test is still running (PID $($startup.Id)); no process was forcibly closed." }
$startup.Refresh()
if ($startup.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $testRoot 'startup.png'))) { throw 'Startup self-test failed' }
Write-Output 'PASS packaged executable startup and screenshot'
Compile-Test 'VerifyR3Workflow' @($application,'System.Windows.Forms.dll','System.Drawing.dll')
Run-Test 'VerifyR3Workflow' @((Join-Path $testRoot 'workflow'))
Compile-Test 'VerifyEditingPersistence' @('System.Windows.Forms.dll','System.Drawing.dll')
Run-Test 'VerifyEditingPersistence' @($application,(Join-Path $testRoot 'editing'))
if ($UiOnly) { Write-Output 'PASS UI-only startup/workflow/editing regression; no CAD or standalone backend tests.'; return }
Compile-Test 'VerifyStepFolderLayout' @($application)
Run-Test 'VerifyStepFolderLayout'
Compile-Test 'VerifyLocationIdentity' @($application,(Join-Path $runtime 'SolidWorks.Interop.sldworks.dll'),(Join-Path $runtime 'SolidWorks.Interop.swconst.dll'))
Run-Test 'VerifyLocationIdentity'
Compile-Test 'VerifyProjectStorage' @('System.Drawing.dll','System.Web.Extensions.dll') @((Join-Path $sourceRoot 'src\Models.cs'),(Join-Path $sourceRoot 'src\ProjectStore.cs'))
Run-Test 'VerifyProjectStorage' @((Join-Path $testRoot 'storage'))
Compile-Test 'VerifyExcelReport' @($application,'System.Drawing.dll','System.Xml.dll','System.Xml.Linq.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
Run-Test 'VerifyExcelReport' @((Join-Path $testRoot 'reports'))
& (Join-Path $sourceRoot 'tests\VerifyBuildInterop.ps1')
Write-Output 'PASS R3 isolated regression suite. This suite does not connect to SolidWorks or export real CAD.'
