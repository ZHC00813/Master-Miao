[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$AcceptScopedRegistryChanges,

    [string]$AssemblyPath,

    [ValidateSet('Machine', 'CurrentUser')]
    [string]$ComScope = 'Machine'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AcceptScopedRegistryChanges) {
    throw '未执行注册。请在确认会修改一个精确的 HKLM Add-in 项和本插件 HKCU COM 项后，加上 -AcceptScopedRegistryChanges。'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $projectRoot "src\Blankedge.SWHoleTools\bin\x64\$Configuration\net48\Blankedge.SWHoleTools.dll"
}
$assemblyPath = [System.IO.Path]::GetFullPath($AssemblyPath)
$regAsmPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
$addinGuid = '{6D68D92F-6A8C-45A0-9DCD-3B92AA5103D1}'
$temporaryRegFile = Join-Path ([System.IO.Path]::GetTempPath()) ("Blankedge.SWHoleTools-" + [Guid]::NewGuid().ToString('N') + '.reg')

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "找不到 DLL：$assemblyPath。请先编译 $Configuration|x64。"
}
if (-not (Test-Path -LiteralPath $regAsmPath -PathType Leaf)) {
    throw "找不到 64 位 .NET Framework RegAsm：$regAsmPath"
}

$description = "注册 Blankedge.SWHoleTools ($ComScope)：$assemblyPath"
if (-not $PSCmdlet.ShouldProcess('精确的 HKLM\Software\SolidWorks\Addins\{GUID} 与本插件 HKCU 项', $description)) {
    return
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'SOLIDWORKS 官方 Add-in 发现键位于 HKLM。请用“以管理员身份运行”的 64 位 PowerShell 执行此脚本。'
}

try {
    & $regAsmPath $assemblyPath /nologo /codebase "/regfile:$temporaryRegFile"
    if ($LASTEXITCODE -ne 0) {
        throw "RegAsm 生成注册文件失败，退出码：$LASTEXITCODE"
    }

    $registryText = Get-Content -LiteralPath $temporaryRegFile -Raw
    $comRoot = if ($ComScope -eq 'Machine') { 'HKEY_LOCAL_MACHINE\Software\Classes' } else { 'HKEY_CURRENT_USER\Software\Classes' }
    $registryText = $registryText.Replace('HKEY_CLASSES_ROOT', $comRoot)
    Set-Content -LiteralPath $temporaryRegFile -Value $registryText -Encoding Unicode
    & reg.exe import $temporaryRegFile
    if ($LASTEXITCODE -ne 0) {
        throw "导入当前用户 COM 注册信息失败，退出码：$LASTEXITCODE"
    }

    # Keep a previous per-user registration aligned with the machine registration.
    # Otherwise HKCR can resolve a stale DLL for non-elevated SOLIDWORKS sessions.
    if ($ComScope -eq 'Machine' -and (Test-Path "HKCU:\Software\Classes\CLSID\$addinGuid")) {
        $userRegistryText = $registryText.Replace('HKEY_LOCAL_MACHINE\Software\Classes', 'HKEY_CURRENT_USER\Software\Classes')
        Set-Content -LiteralPath $temporaryRegFile -Value $userRegistryText -Encoding Unicode
        & reg.exe import $temporaryRegFile
        if ($LASTEXITCODE -ne 0) { throw "同步已有用户 COM 注册失败：$LASTEXITCODE" }
    }

    $addinPath = "HKLM:\Software\SolidWorks\Addins\$addinGuid"
    $startupPath = "HKCU:\Software\SolidWorks\AddInsStartup\$addinGuid"
    New-Item -Path $addinPath -Force | Out-Null
    New-ItemProperty -LiteralPath $addinPath -Name '(default)' -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $addinPath -Name 'Title' -Value '喵师傅孔标注' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $addinPath -Name 'Description' -Value '孔语义标记与自动工程图孔标注工具' -PropertyType String -Force | Out-Null
    if (-not (Test-Path -LiteralPath $startupPath)) {
        New-Item -Path $startupPath | Out-Null
    }
    if ($null -eq (Get-Item -LiteralPath $startupPath).GetValue('')) {
        New-ItemProperty -LiteralPath $startupPath -Name '(default)' -Value 0 -PropertyType DWord -Force | Out-Null
    }

    Write-Host "注册完成（COM 范围：$ComScope；保留现有启动选择；首次注册默认不自动启动）。"
    Write-Host '请在 SOLIDWORKS：工具 > 插件 中启用 喵师傅孔标注，并按需勾选启动。'
}
finally {
    if (Test-Path -LiteralPath $temporaryRegFile) {
        Remove-Item -LiteralPath $temporaryRegFile -Force
    }
}
