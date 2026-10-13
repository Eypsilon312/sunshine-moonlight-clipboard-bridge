param(
    [string]$Owner = 'Eypsilon312',
    [string]$Repository = 'sunshine-moonlight-clipboard-bridge'
)
$ErrorActionPreference = 'Stop'
if ($Owner -notmatch '^[A-Za-z0-9-]+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
$localGh = Join-Path $PSScriptRoot 'dist\tools\gh.exe'
$installedGh = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($installedGh) { $installedGh.Source } elseif (Test-Path -LiteralPath $localGh) { $localGh } else { throw 'Run setup-github.ps1 -Login first.' }
function Invoke-GitChecked {
    # Preserve embedded quotes in credential helpers under Windows PowerShell 5.
    $quoted = foreach ($argument in $args) {
        '"' + [regex]::Replace([regex]::Replace([string]$argument, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = (Get-Command git).Source
    $info.Arguments = $quoted -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    if ($stdout.Result) { Write-Output $stdout.Result.TrimEnd() }
    if ($stderr.Result) { Write-Output $stderr.Result.TrimEnd() }
    $process.Dispose()
    if ($exitCode -ne 0) { throw "git command failed: $($args[0])" }
}
function Invoke-GhChecked {
    & $gh @args
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $($args[0])" }
}
function Invoke-GhOptional {
    # Windows PowerShell 5 treats redirected native stderr as errors under Stop.
    $ErrorActionPreference = 'Continue'
    $output = & $gh @args 2>$null
    [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}
$accountResult = Invoke-GhOptional api user
if ($accountResult.ExitCode -ne 0) { throw 'Sign in first: .\setup-github.ps1 -Login' }
$account = $accountResult.Output | ConvertFrom-Json
if ($account.login -ne $Owner) { throw "Signed in as $($account.login), expected $Owner. Switch to the intended account before publishing." }
& "$PSScriptRoot\package.ps1"
$repo = "$Owner/$Repository"
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.git'))) { Invoke-GitChecked init -b main }
    $branch = & git branch --show-current
    if ($branch -ne 'main') { throw 'Publish from the main branch.' }
    # Use an account-scoped noreply identity, not the user global Git settings.
    Invoke-GitChecked config user.name $account.login
    Invoke-GitChecked config user.email "$($account.id)+$($account.login)@users.noreply.github.com"
    $repoResult = Invoke-GhOptional api "repos/$repo"
    if ($repoResult.ExitCode -ne 0) {
        # Creating an already existing/inaccessible repository fails safely; never overwrite it.
        Invoke-GhChecked repo create $repo --public --description 'Experimental Windows clipboard sharing for Sunshine and Moonlight: text, files, folders, reconnect and startup.'
    } else {
        $info = $repoResult.Output | ConvertFrom-Json
        if ($info.private) { throw 'Repository is private. This script does not change the visibility of an existing repository.' }
    }
    $expectedRemote = "https://github.com/$repo.git"
    $remote = & git config --get remote.origin.url
    if ($LASTEXITCODE -ne 0) { Invoke-GitChecked remote add origin $expectedRemote }
    elseif ($remote -ne $expectedRemote -and $remote -ne "git@github.com:$repo.git") { throw "The origin remote does not match $repo." }
    # Use the authenticated CLI for this repository only. Never print its token.
    $helper = '!"' + ($gh -replace '\\', '/') + '" auth git-credential'
    Invoke-GitChecked add -- .gitignore .gitattributes .github assets docs AssemblyInfo.cs ClipboardBridge.cs LICENSE README.md CHANGELOG.md RELEASE_NOTES.md TESTING.md PUBLISHING.md build.ps1 create-icon.ps1 test.ps1 package.ps1 setup-github.ps1 publish.ps1
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { Invoke-GitChecked commit -m 'Prepare English Clipboard Bridge v0.1.0-beta.3 public preview' }
    elseif ($LASTEXITCODE -ne 0) { throw 'Could not inspect staged changes.' }
    Invoke-GitChecked -c credential.helper= -c "credential.helper=$helper" push -u origin main
    Invoke-GhChecked repo edit $repo --add-topic sunshine --add-topic moonlight --add-topic clipboard --add-topic windows --add-topic file-transfer
    $version = 'v0.1.0-beta.3'
    $existingTag = & git tag --list $version
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect existing tags.' }
    if (-not $existingTag) { Invoke-GitChecked tag -a $version -m 'Clipboard Bridge Windows public preview' }
    $tagCommit = & git rev-list -n 1 $version
    $head = & git rev-parse HEAD
    if ($tagCommit -ne $head) { throw 'The version tag points to a different commit. Existing tags will not be moved.' }
    Invoke-GitChecked -c credential.helper= -c "credential.helper=$helper" push origin $version
    $releaseResult = Invoke-GhOptional release view $version --repo $repo --json url
    if ($releaseResult.ExitCode -ne 0) {
        Invoke-GhChecked release create $version (Join-Path $PSScriptRoot "dist\ClipboardBridge-$version-Windows.zip") (Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt') --repo $repo --verify-tag --prerelease --title 'Clipboard Bridge v0.1.0-beta.3 - Windows public preview' --notes-file (Join-Path $PSScriptRoot 'RELEASE_NOTES.md')
    } else { Write-Output 'The release already exists; assets were not overwritten.' }
    Invoke-GhChecked release view $version --repo $repo --json 'url,isPrerelease,assets'
} finally { Pop-Location }
