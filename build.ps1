$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
& "$PSScriptRoot\create-icon.ps1"
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:"$PSScriptRoot\assets\ClipboardBridge.ico" /out:"$outDir\ClipboardBridge.exe" /reference:System.Security.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$PSScriptRoot\AssemblyInfo.cs" "$PSScriptRoot\ClipboardBridge.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built: $outDir\ClipboardBridge.exe"
