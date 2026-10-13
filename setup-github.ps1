param([switch]$Login)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$toolDir = Join-Path $PSScriptRoot 'dist\tools'
$gh = Join-Path $toolDir 'gh.exe'
if (-not (Test-Path -LiteralPath $gh)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cli/cli/releases/latest' -Headers @{ 'User-Agent' = 'ClipboardBridge-release-setup' }
    $asset = $release.assets | Where-Object name -Match '_windows_amd64\.zip$' | Select-Object -First 1
    if (-not $asset) { throw 'Official GitHub CLI Windows x64 asset was not found.' }
    New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
    $archive = Join-Path $toolDir $asset.name
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $archive
    $expected = $null
    if ($asset.digest -match '^sha256:([a-fA-F0-9]{64})$') { $expected = $Matches[1] }
    else {
        $checksums = $release.assets | Where-Object name -Match 'checksums\.txt$' | Select-Object -First 1
        if (-not $checksums) { throw 'Official checksums were not found.' }
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $checksums.browser_download_url).Content
        foreach ($line in ($content -split "`n")) {
            if ($line -match '^([a-fA-F0-9]{64})\s+\*?(.+?)\s*$' -and $Matches[2] -eq $asset.name) { $expected = $Matches[1]; break }
        }
    }
    if (-not $expected -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'GitHub CLI checksum verification failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'bin/gh.exe' -or $_.FullName.EndsWith('/bin/gh.exe') } | Select-Object -First 1
        if (-not $entry) { throw 'gh.exe was not found in the verified archive.' }
        $inputStream = $entry.Open()
        $outputStream = [System.IO.File]::Create($gh)
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
    } finally { $zip.Dispose() }
}
& $gh --version
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI did not start.' }
if ($Login) {
    & $gh auth login --hostname github.com --git-protocol https --web --scopes workflow
    if ($LASTEXITCODE -ne 0) { throw 'GitHub sign-in failed.' }
}
