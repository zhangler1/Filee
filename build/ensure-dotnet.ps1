# Makes a dotnet SDK usable by the Windows build scripts: the one on PATH, or a previous download, or a
# one-time download via Microsoft's official dotnet-install script (no admin rights needed). Dot-source it
# (". (Join-Path $PSScriptRoot 'ensure-dotnet.ps1')") so the PATH update reaches the calling script.
#
# The SDK installs into %LOCALAPPDATA%\Microsoft\dotnet (dotnet-install's default) and is prepended to PATH
# for the current process only; nothing is registered machine-wide. The channel comes from global.json, and
# its rollForward policy accepts any newer feature band of the same major.minor.

$ErrorActionPreference = 'Stop'

if (Get-Command dotnet -ErrorAction SilentlyContinue) { return }

$dotnetHome = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$dotnetExe = Join-Path $dotnetHome 'dotnet.exe'
if (Test-Path -LiteralPath $dotnetExe -PathType Leaf) {
    $env:Path = "$dotnetHome;$env:Path"
    Write-Host "dotnet: $dotnetExe"
    return
}

$sdkVersion = ((Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\global.json') -Raw) | ConvertFrom-Json).sdk.version
$channel = ($sdkVersion -split '\.')[0..1] -join '.'
Write-Host "dotnet not found; downloading the .NET $channel SDK (one-time setup)..."

$installer = Join-Path $env:TEMP ("dotnet-install-" + [Guid]::NewGuid().ToString('N') + '.ps1')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    # dotnet-install is a script, so $LASTEXITCODE says nothing here; it throws on failure, and the
    # dotnet.exe check below confirms the install.
    & $installer -Channel $channel -InstallDir $dotnetHome
}
finally {
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $dotnetExe -PathType Leaf)) { throw "dotnet-install did not produce $dotnetExe" }
$env:Path = "$dotnetHome;$env:Path"
Write-Host "dotnet: $dotnetExe"
