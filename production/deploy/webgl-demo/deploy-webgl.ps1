#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Ask', 'Never', 'Always')]
    [string]$Upload = 'Ask',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Server,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]*$')]
    [string]$SshUser = 'Administrator'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'webgl-cloud.ps1')
$git = Get-Command git.exe -CommandType Application | Select-Object -First 1
$gitRoot = Split-Path -Parent (Split-Path -Parent $git.Source)
$bash = Join-Path $gitRoot 'bin\bash.exe'
if (-not (Test-Path -LiteralPath $bash -PathType Leaf)) {
    throw 'Git Bash is required. Install Git for Windows and add git.exe to PATH.'
}

$dist = Join-Path $PSScriptRoot 'dist'
$release = 'CoolWebGL-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$archiveName = $release + '.zip'
$archive = Join-Path $dist $archiveName
$launchers = @('setup-once.cmd', 'start-demo.cmd', 'start-static.cmd', 'start-game-server.cmd', 'webgl-cloud.ps1')

New-Item -ItemType Directory -Path $dist -Force | Out-Null
$lock = [IO.File]::Open((Join-Path $dist 'publish.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    Push-Location $PSScriptRoot
    try {
        & $bash './publish-server.sh'
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Check the output from publish-server.sh above.' }

        $requiredFiles = @(
            'dist/site/index.html'
            'dist/site/Build/WebGL.loader.js'
            'dist/site/Build/WebGL.framework.js'
            'dist/site/Build/WebGL.wasm'
            'dist/site/Build/WebGL.data'
            'dist/server/Server3C.exe'
            'dist/server/StaticServer.exe'
            'dist/server/Config/monster_config.json'
            'dist/server/Config/maps.json'
            'dist/server/Config/nav/demoday.navbin'
        ) + $launchers
        foreach ($file in $requiredFiles) {
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing deployment file: $file" }
        }

        Set-Content -LiteralPath 'dist/site/cool-release.txt' -Value $release -Encoding Ascii
        & tar.exe -caf $archive 'dist/server' 'dist/site' @launchers
        if ($LASTEXITCODE -ne 0) { throw "Could not create $archive" }
    }
    finally {
        Pop-Location
    }
}
finally {
    $lock.Dispose()
}

Write-Host "Local package: $archive"
if ($Upload -eq 'Never') { return }
if ($Upload -eq 'Ask' -and (Read-Host 'Upload to Tencent Cloud now? [y/N]') -notmatch '^(y|yes)$') { return }

if (-not $Server) {
    $Server = (Read-Host 'Cloud server public IPv4 or hostname').Trim()
    if ($Server -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$') { throw 'Enter a valid IPv4 address or hostname.' }
}

$destination = "$SshUser@$Server"
$sshOptions = @('-o', 'ConnectTimeout=8', '-o', 'StrictHostKeyChecking=yes', '-o', 'BatchMode=yes')
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
Push-Location $dist
try {
    Write-Host "Uploading $archiveName to $Server..."
    & scp.exe @sshOptions "./$archiveName" "${destination}:$archiveName"
    if ($LASTEXITCODE -ne 0) { throw "Upload failed. ZIP: $archive. Check TCP 22, the SSH key and the trusted host key in the runbook." }
}
finally {
    Pop-Location
}

# Only the generated release name and its SHA-256 are substituted in the remote command.
$install = @'
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    $release = '__RELEASE__'
    $zip = Join-Path $env:USERPROFILE ($release + '.zip')
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '__HASH__') {
        throw 'The uploaded ZIP did not match the local SHA-256.'
    }

    $root = 'C:\CoolDemo\releases'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $target = Join-Path $root $release
    if (Test-Path -LiteralPath $target) { throw "Release already exists: $target" }
    $stage = Join-Path $root ('.' + $release + '-' + [Guid]::NewGuid().ToString('N') + '.partial')
    Expand-Archive -LiteralPath $zip -DestinationPath $stage
    Rename-Item -LiteralPath $stage -NewName $release
    . (Join-Path $target 'webgl-cloud.ps1')
    Start-WebGlRelease -Directory $target -Release $release
    Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue
    Write-Output "Started and checked on the server: $target"
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
exit 0
'@
$install = $install.Replace('__RELEASE__', $release).Replace('__HASH__', $hash)
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($install))
Write-Host 'Installing and starting the cloud services...'
& ssh.exe @sshOptions $destination powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded
if ($LASTEXITCODE -ne 0) { throw "Cloud installation failed. The local ZIP is available at $archive." }

Write-Host 'Checking the public web and game endpoints...'
try {
    Wait-WebGlHealth -Server $Server -Release $release
}
catch {
    throw "The release is running on the server, but the public check failed. Check Tencent Cloud TCP 8080 and 8000. $($_.Exception.Message)"
}
Write-Host "Deployment complete: $release"
Write-Host ('Demo URL: http://' + $Server + ':8080/?host=' + $Server)
