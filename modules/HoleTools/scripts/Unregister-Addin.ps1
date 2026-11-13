[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$AcceptScopedRegistryChanges
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AcceptScopedRegistryChanges) {
    throw '未执行反注册。请在确认只删除本插件精确注册项后，加上 -AcceptScopedRegistryChanges。'
}

$addinGuid = '{6D68D92F-6A8C-45A0-9DCD-3B92AA5103D1}'
$typeLibGuid = '{D890A2B2-A938-4A39-8660-BE39411DBA61}'
$targets = @(
    "HKLM:\Software\SolidWorks\Addins\$addinGuid",
    "HKCU:\Software\SolidWorks\AddInsStartup\$addinGuid",
    "HKCU:\Software\Classes\CLSID\$addinGuid",
    'HKCU:\Software\Classes\Blankedge.SWHoleTools',
    "HKCU:\Software\Classes\TypeLib\$typeLibGuid"
)

if (-not $PSCmdlet.ShouldProcess('Blankedge.SWHoleTools 的 5 个精确 HKLM/HKCU 注册项', '删除')) {
    return
}


$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '删除 SOLIDWORKS HKLM Add-in 发现键需要管理员权限。请用“以管理员身份运行”的 64 位 PowerShell执行。'
}

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "已删除：$target"
    }
}

Write-Host 'Blankedge.SWHoleTools 的精确注册信息已清除。DLL、源码和其他插件未删除。'
