param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buildRoot = Join-Path $projectRoot 'build'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$packageRoot = Join-Path $outputRoot 'MasterMiao-v1.2.6-0922-R5'
$archivePath = Join-Path $outputRoot 'MasterMiao-v1.2.6-0922-R5.zip'
if ((Test-Path -LiteralPath $packageRoot) -or (Test-Path -LiteralPath $archivePath)) { throw 'Package target already exists; choose a new output directory. Existing packages are never overwritten.' }
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $buildRoot 'MasterMiao.exe'))
if ($assembly.Version.ToString() -ne '1.2.6.0') { throw 'Build is not V1.2.6.0.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildRoot 'MasterMiao.exe')).FileVersion -ne '1.2.6.5') { throw 'Build is not revision 0922-R5.' }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$runtimeFiles = @('MasterMiao.exe','MasterMiao.exe.config','MasterMiao.StepMacro.dll','MasterMiao.ico','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll')
foreach ($name in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $buildRoot $name) -Destination $packageRoot }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\MasterMiao-logo.png') -Destination $packageRoot
$documents = @('README.md','README.en.md','ARCHITECTURE.md','DEVELOPMENT_HISTORY.md','V1.2.5_COMPATIBILITY_AUDIT.md','V1.2.6_CHANGES.md','R2_FIX_VALIDATION.md','R3_CHANGES_AND_TESTS.md','R4_UI_NOTES.md','R5_FIX_NOTES.md','RELEASE_NOTES_R5.md','VALIDATION.md','ACCEPTANCE_CHECKLIST.md','TEST_RESULT.txt','THIRD_PARTY_NOTICE.md')
foreach ($name in $documents) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $packageRoot }
$sourceRoot = Join-Path $packageRoot 'Source'
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
foreach ($name in @('src','macro','assets','tests','tools')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $sourceRoot -Recurse }
foreach ($name in (@('build.ps1','MasterMiao.exe.config','.gitignore') + $documents)) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $sourceRoot }
$privateFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Extension -match '^\.(SLDPRT|SLDASM|STEP|STP|swbody|log)$' -or $_.Name -like '*.swbody.json' }
if ($privateFiles) { throw 'Private/runtime CAD data detected; package not created.' }
$manifest = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($packageRoot.Length + 1).Replace('\','/') }
[IO.File]::WriteAllLines((Join-Path $packageRoot 'SHA256SUMS.txt'), $manifest, [Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256

