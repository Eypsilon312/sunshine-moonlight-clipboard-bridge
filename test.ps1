$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'dist\ClipboardBridge.exe'
$resultPath = Join-Path $PSScriptRoot 'dist\self-test-result.txt'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Run build.ps1 first.' }
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$process = Start-Process -FilePath $executable -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Self-test timed out.' }
$process.Refresh()
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Self-test did not produce a result.' }
$result = Get-Content -LiteralPath $resultPath -Raw
if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS:')) { throw $result }
Write-Output $result.Trim()
