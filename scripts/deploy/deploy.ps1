#Requires -Version 7.0
<#
.SYNOPSIS
    Deploy a Cestius monorepo app to its Linux server over SSH.

.DESCRIPTION
    One reusable orchestrator for every app. It reads the per-app variables from
    scripts/deploy/apps/<App>.ps1, builds the React client and/or .NET backend
    locally, ships the artifacts + the per-app server assets to the VPS, and runs
    the matching server-side bash script (lib/setup.sh | install.sh | config.sh).

    Design: this script only ORCHESTRATES (build -> scp -> invoke). All imperative
    server logic lives in committed, shellcheck-able lib/*.sh files that are
    app-agnostic — every app value reaches them via a generated deploy.env they
    source. Onboarding another app is just a new apps/<App>.ps1; deploy.ps1 and
    lib/*.sh do not change.

    Strategy (mirrors the Blondi/VcSuite reference): framework-dependent publish,
    systemd-managed Kestrel behind nginx. The Showcase app has no database, so
    there is no migration step and no secrets file — all prod config ships in
    appsettings.Production.json.

.PARAMETER App
    Per-app config to load: scripts/deploy/apps/<App>.ps1 (e.g. 'showcase').

.PARAMETER Setup
    First-time provisioning: service user, directories, perms, nginx site, and
    systemd unit. Idempotent — safe to re-run.

.PARAMETER ClientOnly
    Build and deploy only the React frontend.

.PARAMETER ServerOnly
    Build and deploy only the .NET backend.

.PARAMETER ConfigOnly
    Re-push nginx.conf + the systemd unit and reload (no build).

.EXAMPLE
    ./scripts/deploy/deploy.ps1 showcase -Setup    # first-time provisioning
    ./scripts/deploy/deploy.ps1 showcase           # client + server
    ./scripts/deploy/deploy.ps1 showcase -ServerOnly
    ./scripts/deploy/deploy.ps1 showcase -ClientOnly
    ./scripts/deploy/deploy.ps1 showcase -ConfigOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$App,
    [switch]$Setup,
    [switch]$ClientOnly,
    [switch]$ServerOnly,
    [switch]$ConfigOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$DeployDir = $PSScriptRoot
$RepoRoot = Split-Path (Split-Path $DeployDir -Parent) -Parent   # scripts/deploy -> scripts -> repo root
$LibDir = Join-Path $DeployDir 'lib'
$AppConfig = Join-Path $DeployDir "apps/$App.ps1"
$AssetsDir = Join-Path $DeployDir "apps/$App"

# ---------------------------------------------------------------------------
# Load per-app config (variables only)
# ---------------------------------------------------------------------------
if (-not (Test-Path $AppConfig)) {
    $available = (Get-ChildItem (Join-Path $DeployDir 'apps') -Filter '*.ps1' |
        ForEach-Object BaseName) -join ', '
    throw "No config for app '$App' (expected $AppConfig). Available: $available"
}
$cfg = & $AppConfig
foreach ($k in 'AppName', 'SshHost', 'Domain', 'Port', 'ClientDir', 'ClientFilter', 'ServerProj', 'ServerDll') {
    if (-not $cfg.ContainsKey($k)) { throw "apps/$App.ps1 is missing required key '$k'." }
}

$AppName = $cfg.AppName
$SshHost = $cfg.SshHost
$UploadDir = "~/$AppName-upload"
$ClientDist = Join-Path (Join-Path $RepoRoot $cfg.ClientDir) 'dist'
$ServerProj = Join-Path $RepoRoot $cfg.ServerProj
$StageDir = Join-Path $RepoRoot ".deploy-tmp/$AppName"          # local staging (gitignored)
$PublishOut = Join-Path $StageDir 'server'

$deployClient = -not $ServerOnly -and -not $ConfigOnly
$deployServer = -not $ClientOnly -and -not $ConfigOnly

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Assert-Command([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required tool not found on PATH: $name"
    }
}

function Invoke-Scp([string]$local, [string]$remote, [switch]$Recurse) {
    if ($Recurse) { & scp -r $local "${SshHost}:${remote}" }
    else { & scp $local "${SshHost}:${remote}" }
    if ($LASTEXITCODE -ne 0) { throw "SCP failed: $local -> $remote" }
}

function Invoke-Ssh([string]$command) {
    & ssh $SshHost $command
    if ($LASTEXITCODE -ne 0) { throw "Remote command failed: $command (exit $LASTEXITCODE)" }
}

function Invoke-Remote([string]$scriptName) {
    # Run a lib/*.sh on the server with the upload dir as $1. The script sources
    # $1/deploy.env itself, so the invocation stays quoting-free and we don't have
    # to scp the lib scripts.
    #
    # We base64 the script and decode it remotely rather than pipe it over stdin.
    # Piping a string to a native command in PowerShell appends an Environment.NewLine
    # (CRLF) terminator AFTER the content, so even with CRs stripped the server sees a
    # trailing lone $'\r' on a phantom last line and bash dies with "command not found"
    # (exit 127). Passing the payload as one base64 argument sidesteps PowerShell's
    # stdin newline handling entirely and reconstructs the exact LF bytes server-side.
    # $UploadDir is left unquoted so the remote login shell expands its leading ~
    # (it is a bare app name with no spaces). It arrives in the script as $1.
    Write-Step "Running $scriptName on $SshHost"
    $script = (Get-Content (Join-Path $LibDir $scriptName) -Raw) -replace "`r", ''
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
    & ssh $SshHost "echo $b64 | base64 -d | bash -s -- $UploadDir"
    if ($LASTEXITCODE -ne 0) { throw "Remote script failed: $scriptName (exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------------------
# Prerequisites (local)
# ---------------------------------------------------------------------------
Write-Step "Deploying '$AppName' to $SshHost ($($cfg.Domain))"
Assert-Command 'ssh'
Assert-Command 'scp'
if ($deployServer) { Assert-Command 'dotnet' }
if ($deployClient) { Assert-Command 'pnpm' }

# ---------------------------------------------------------------------------
# Build: React client
# ---------------------------------------------------------------------------
if ($deployClient) {
    Write-Step 'Building React client'
    Push-Location $RepoRoot
    try {
        pnpm install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'pnpm install failed' }
        pnpm --filter $cfg.ClientFilter build
        if ($LASTEXITCODE -ne 0) { throw 'client build failed' }
    }
    finally { Pop-Location }
}

# ---------------------------------------------------------------------------
# Build: .NET backend
# ---------------------------------------------------------------------------
if ($deployServer) {
    Write-Step 'Publishing .NET backend'
    if (Test-Path $PublishOut) { Remove-Item $PublishOut -Recurse -Force }
    dotnet publish $ServerProj `
        --configuration Release `
        --output $PublishOut `
        --no-self-contained `
        --runtime linux-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    # Overlay the non-secret prod config; drop the dev one if it slipped into publish.
    Copy-Item (Join-Path $AssetsDir 'appsettings.Production.json') $PublishOut -Force
    Remove-Item (Join-Path $PublishOut 'appsettings.Development.json') -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# Generate deploy.env — the single source of truth the server-side bash reads.
# ---------------------------------------------------------------------------
Write-Step 'Preparing upload'
if (-not (Test-Path $StageDir)) { New-Item -ItemType Directory -Path $StageDir -Force | Out-Null }
$envFile = Join-Path $StageDir 'deploy.env'
# Write LF-only. Set-Content/Out-File append [Environment]::NewLine (CRLF on
# Windows) after the content, so the final line would arrive as e.g.
# "DEPLOY_CLIENT=1\r"; bash then sources DEPLOY_CLIENT="1\r" and `[ "$DEPLOY_CLIENT"
# = "1" ]` fails, silently skipping the client install. Same class of bug as the
# streamed-script CR stripping in Invoke-Remote — .NET WriteAllText emits exactly
# the bytes given (UTF-8, no BOM, no trailing CRLF).
$envLines = @(
    "APP_NAME=$AppName"
    "DOMAIN=$($cfg.Domain)"
    # "PORT=$($cfg.Port)"
    # "SERVER_DLL=$($cfg.ServerDll)"
    "SERVICE_USER=$AppName"
    "DEPLOY_SERVER=$([int][bool]$deployServer)"
    "DEPLOY_CLIENT=$([int][bool]$deployClient)"
)
[System.IO.File]::WriteAllText($envFile, ($envLines -join "`n") + "`n")

# ---------------------------------------------------------------------------
# Transfer to the server staging dir
# ---------------------------------------------------------------------------
Invoke-Ssh "mkdir -p $UploadDir/config"
Invoke-Scp $envFile "$UploadDir/deploy.env"
Invoke-Scp (Join-Path $AssetsDir 'nginx.conf')           "$UploadDir/config/"
Invoke-Scp (Join-Path $AssetsDir "$AppName.service")     "$UploadDir/config/"

if ($deployServer) {
    Write-Host '  server binaries...'
    Invoke-Scp $PublishOut "$UploadDir/" -Recurse        # -> $UploadDir/server
}
if ($deployClient) {
    if (-not (Test-Path $ClientDist)) { throw "Client build output not found: $ClientDist" }
    Write-Host '  client dist...'
    Invoke-Scp $ClientDist "$UploadDir/" -Recurse         # -> $UploadDir/dist
}

# ---------------------------------------------------------------------------
# Run the server-side steps
# ---------------------------------------------------------------------------
if ($Setup) { Invoke-Remote 'setup.sh' }
if ($deployServer -or $deployClient) { Invoke-Remote 'install.sh' }
if ($ConfigOnly) { Invoke-Remote 'config.sh' }

Write-Host "`nDone." -ForegroundColor Green
