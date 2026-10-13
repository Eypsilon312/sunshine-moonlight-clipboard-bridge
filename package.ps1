$ErrorActionPreference = 'Stop'
$version = 'v0.1.0-beta.2'
& "$PSScriptRoot\build.ps1"
& "$PSScriptRoot\test.ps1"
$outDir = Join-Path $PSScriptRoot 'dist'
$executable = Join-Path $outDir 'ClipboardBridge.exe'
$archive = Join-Path $outDir "ClipboardBridge-$version-Windows.zip"
$files = @($executable)
foreach ($name in @('README.md', 'LICENSE', 'CHANGELOG.md', 'TESTING.md', 'PUBLISHING.md')) {
    $target = Join-Path $outDir $name
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $target -Force
    $files += $target
}
$files += (Join-Path $PSScriptRoot 'docs')
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Copy-Item -LiteralPath $archive -Destination (Join-Path $outDir 'ClipboardBridge-Windows.zip') -Force
$lines = foreach ($file in @($archive, $executable)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($file))"
}
[System.IO.File]::WriteAllLines((Join-Path $outDir 'SHA256SUMS.txt'), [string[]]$lines, [System.Text.UTF8Encoding]::new($false))
Write-Output "Packaged: $archive"
