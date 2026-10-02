param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin\win-x64'))
$ErrorActionPreference = 'Stop'
$captureBuildRoot = Join-Path $PSScriptRoot 'work\framework-dependent'
& dotnet publish (Join-Path $PSScriptRoot 'AgentCapture.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:AgentCaptureBuildRoot=$captureBuildRoot" `
    -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
