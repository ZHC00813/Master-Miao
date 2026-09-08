$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'build.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw $parseErrors[0] }
$copyLoop = $ast.Find({ param($node) $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Extent.Text.Contains('$sameAssembly') }, $true)
if ($null -eq $copyLoop) { throw 'Interop copy loop was not found.' }
$copyAction = [ScriptBlock]::Create($copyLoop.Extent.Text)
$testRoot = Join-Path $projectRoot ('build\interop-copy-test-' + [Guid]::NewGuid().ToString('N'))
$swApi = Join-Path $testRoot 'source'
$output = Join-Path $testRoot 'target'
New-Item -ItemType Directory -Path $swApi, $output | Out-Null
foreach ($name in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
    [IO.File]::WriteAllBytes((Join-Path $swApi $name), [byte[]](1,2,3,4))
    [IO.File]::WriteAllBytes((Join-Path $output $name), [byte[]](1,2,3,4))
}
$lockedPath = Join-Path $output 'SolidWorks.Interop.sldworks.dll'
$before = (Get-Item -LiteralPath $lockedPath).LastWriteTimeUtc
$locked = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    & $copyAction
    if ((Get-Item -LiteralPath $lockedPath).LastWriteTimeUtc -ne $before) { throw 'Identical locked target was copied.' }
    Write-Output 'PASS identical locked interop target is preserved without copying'
    [IO.File]::WriteAllBytes((Join-Path $swApi 'SolidWorks.Interop.sldworks.dll'), [byte[]](5,6,7,8))
    $rejected = $false
    try { & $copyAction } catch { $rejected = $true }
    if (-not $rejected) { throw 'Mismatched locked target did not fail.' }
    Write-Output 'PASS mismatched locked interop target explicitly fails'
} finally { $locked.Dispose() }
& $copyAction
if ((Get-FileHash -LiteralPath $lockedPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $swApi 'SolidWorks.Interop.sldworks.dll')).Hash) { throw 'Mismatched unlocked target was not updated.' }
Write-Output 'PASS mismatched unlocked interop target is copied and matches the selected API'
