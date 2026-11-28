param([Parameter(Mandatory)][string]$BaseInstall,[Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$WorkingDirectory)
$ErrorActionPreference='Stop'
$manifest=Import-Csv -LiteralPath (Join-Path $PSScriptRoot 'payload.csv')
$root=[IO.Path]::GetFullPath($WorkingDirectory)
if(Test-Path -LiteralPath $root){throw 'Use a new build directory.'}
New-Item -ItemType Directory -Path $root|Out-Null
$payload=Join-Path $root 'payload'
Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'components.zip') -DestinationPath $payload
foreach($item in $manifest){
    $path=Join-Path $payload $item.Path
    if(!(Test-Path -LiteralPath $path)){
        New-Item -ItemType Directory -Force -Path (Split-Path $path)|Out-Null
        Copy-Item -LiteralPath (Join-Path $BaseInstall $item.Path) -Destination $path
    }
    if((Get-FileHash -LiteralPath $path).Hash -ne $item.SHA256){throw "Payload mismatch: $($item.Path)"}
    [IO.File]::SetLastWriteTimeUtc($path,[datetime]::new([long]$item.UtcTicks,[DateTimeKind]::Utc))
}
$script=Join-Path $root 'setup.iss'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup.iss') -Destination $script
& $Compiler /Q $script
if($LASTEXITCODE){throw 'ISCC failed.'}
$result=Join-Path $root 'output/MasterMiao-1.6.3-Setup-x64.exe'
$hash=(Get-FileHash -LiteralPath $result).Hash
if($hash -ne 'A7F552EEE77B3E33F2DA4B0789E0E5F866CE2D2080895B9FD9D8C517E2674978'){throw "Installer mismatch: $hash"}
Write-Output "Verified $result"
