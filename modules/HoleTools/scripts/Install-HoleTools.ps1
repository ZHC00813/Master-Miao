$ErrorActionPreference = 'Stop'
$destination = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'MasterMiao\HoleTools'
$files = @('Blankedge.SWHoleTools.dll','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll')
$hashes = foreach ($name in $files) { (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash }
$sha = [Security.Cryptography.SHA256]::Create()
try { $version = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes(($hashes -join '')))).Replace('-','') }
finally { $sha.Dispose() }
$destination = Join-Path $destination $version
New-Item -ItemType Directory -Path $destination -Force | Out-Null
for ($i=0; $i -lt $files.Count; $i++) {
    $target = Join-Path $destination $files[$i]
    if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne $hashes[$i]) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $files[$i]) -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target).Hash -ne $hashes[$i]) { throw "Copy failed: $target" }
    }
}
$dll = Join-Path $destination $files[0]
& (Join-Path $PSScriptRoot 'Register-Addin.ps1') -AssemblyPath $dll -AcceptScopedRegistryChanges -Confirm:$false
$key = Get-Item 'HKLM:\SOFTWARE\Classes\CLSID\{6D68D92F-6A8C-45A0-9DCD-3B92AA5103D1}\InprocServer32'
if (([Uri]$key.GetValue('CodeBase')).LocalPath -ne $dll) { throw 'Registered path mismatch.' }
