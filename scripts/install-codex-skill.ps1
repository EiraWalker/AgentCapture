param(
    [string]$ToolPath,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
$captureRepository = Split-Path -Parent $PSScriptRoot
$captureSkillSource = Join-Path $captureRepository 'skills\agentcapture'
if (-not $ToolPath) { $ToolPath = Join-Path $captureRepository 'bin\win-x64\AgentCapture.exe' }
if (-not (Test-Path -LiteralPath $ToolPath -PathType Leaf)) { throw 'Build AgentCapture first or pass -ToolPath.' }
$captureExecutable = (Resolve-Path -LiteralPath $ToolPath).Path
if (-not $Destination) {
    $captureCodexRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $Destination = Join-Path $captureCodexRoot 'skills\agentcapture'
}
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Preserve it and choose another destination or update it explicitly.' }
New-Item -ItemType Directory -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $captureSkillSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse
}
@{ executable = $captureExecutable } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'config.local.json') -Encoding utf8
$captureResult = & $captureExecutable version | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $captureResult.ok) { throw 'AgentCapture version verification failed.' }
[pscustomobject]@{ skill = 'agentcapture'; path = [System.IO.Path]::GetFullPath($Destination); executable = $captureExecutable; version = $captureResult.version } | ConvertTo-Json
