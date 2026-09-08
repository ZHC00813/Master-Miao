param([Parameter(Mandatory=$true)][string]$BaselineSource)
$ErrorActionPreference = 'Stop'
$current = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
foreach ($file in @('src\SolidWorksWorker.cs','src\AssemblyStepExporter.cs','src\ExportIntegrity.cs','src\Models.cs','src\ProjectStore.cs','src\ExcelReportWriter.cs','src\FolderCanvas.cs','macro\StepMacro.cs','build.ps1')) {
    if ((Get-FileHash -LiteralPath (Join-Path $current $file)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $BaselineSource $file)).Hash) { throw "Protected backend changed: $file" }
    Write-Output "PASS unchanged $file"
}
function MemberText([string]$text, [string]$name) {
    $members = [regex]::Matches($text, '(?m)^        (?:private|public|internal|protected) [^\r\n]+')
    for ($i=0; $i -lt $members.Count; $i++) {
        if ($members[$i].Value -match ('\b' + [regex]::Escape($name) + '\(')) {
            $end = if ($i+1 -lt $members.Count) { $members[$i+1].Index } else { $text.Length }
            return $text.Substring($members[$i].Index, $end-$members[$i].Index).Trim()
        }
    }
    throw "Member not found: $name"
}
$checks = @{
    'src\MainForm.cs' = @('InitializeWorker','StartScan','StartExport','BuildExportPlan','StartWorker','ConfirmSolidWorksTask','RequestCancel','SetBusy','CommitExportNameEdit')
    'src\UiWorkflow.cs' = @('ExportOptionsChanged','RecoverWorkerResult','RetryFailed','MoveReview','FocusReviewIssue')
    'src\V120Features.cs' = @('OpenGuidedMode','Commit','MoveIndex','Locate')
}
foreach ($file in $checks.Keys) {
    $before = Get-Content -LiteralPath (Join-Path $BaselineSource $file) -Raw
    $after = Get-Content -LiteralPath (Join-Path $current $file) -Raw
    foreach ($name in $checks[$file]) {
        if ((MemberText $before $name) -cne (MemberText $after $name)) { throw "Protected operation changed: $file / $name" }
        Write-Output "PASS unchanged $name"
    }
}
Write-Output 'PASS UI-only scope; backend file hashes and protected operation text match R3.'
