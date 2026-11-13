param([string]$File,[string]$Output)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$methods=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
$generic=$methods | Where-Object { $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
$action=$methods | Where-Object { !$_.IsGenericMethod -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
function Await($op,$type) { $task=$generic.MakeGenericMethod($type).Invoke($null,@($op)); $task.Wait(); $task.Result }
$fileObj=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($File)) ([Windows.Storage.StorageFile])
$pdf=Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($fileObj)) ([Windows.Data.Pdf.PdfDocument])
if ($pdf.PageCount -gt 8) {throw 'Select up to 8 drawing pages.'}
for ($i=0;$i -lt $pdf.PageCount;$i++){
  $page=$pdf.GetPage($i)
  $stream=[Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
  try {
    $opts=[Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime]::new()
    $opts.DestinationWidth=[uint32][Math]::Min(2600,([Math]::Max(1000,$page.Size.Width*2)))
    $task=$action.Invoke($null,@($page.RenderToStreamAsync($stream,$opts))); $task.Wait()
    $stream.Seek(0)
    $reader=[System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
    $dest=[IO.File]::Create((Join-Path $Output ("page-{0}.img" -f $i)))
    try {$reader.CopyTo($dest)} finally {$dest.Dispose();$reader.Dispose()}
  } finally {$stream.Dispose();$page.Dispose()}
}
