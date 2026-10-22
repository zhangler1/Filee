# Builds the Lite single-file Windows exe for the competition build: everything the app needs is packed
# into one Filee.exe (native Skia and PDFium libraries included, self-extracted on first run).
#
#   build-lite.ps1                  publish the exe into build/Releases-lite
#   build-lite.ps1 -Zip             also wrap it in a zip with the license
#   build-lite.ps1 -NoTrim          skip IL trimming (fallback when a trimmed build misbehaves; ~25 MB bigger)
#
# FileeRelease stays false on purpose: a contest exe registers no Explorer or startup entries, so it leaves
# the judge's machine alone however it is run.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$OutputDir = (Join-Path $PSScriptRoot '../Releases-lite'),
    [switch]$Zip,
    [switch]$NoTrim
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'ensure-dotnet.ps1')
if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw 'Version must be a three-part version with an optional prerelease suffix.'
}
$packageName = "Filee-Lite-$Version-$Runtime"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$publish = Join-Path $OutputDir $packageName
if (Test-Path -LiteralPath $publish) {
    throw "Folder $publish already exists. Use another output folder or version."
}
New-Item -ItemType Directory -Force -Path $publish | Out-Null

function Invoke-Checked([string]$name, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

$trim = if ($NoTrim) { 'false' } else { 'true' }
Invoke-Checked 'Publish Filee.App' {
    dotnet publish (Join-Path $root 'src/Filee.App/Filee.App.csproj') -c Release -r $Runtime --self-contained true `
        "-p:Version=$Version" -p:FileeRelease=false `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        "-p:PublishTrimmed=$trim" -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=false -p:PublishAot=false -o $publish
}

# Symbols and XML docs are no use to a contest exe; a single-file publish should then leave exactly Filee.exe
# (its icon comes from the manifest inside it).
Get-ChildItem -LiteralPath $publish -Recurse -File -Include *.pdb, *.xml | Remove-Item -Force
$leftovers = Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Name -ne 'Filee.exe' }
if ($leftovers) {
    Write-Warning "Unexpected files beside the exe (they stay in the folder): $($leftovers.Name -join ', ')"
}
$exe = Join-Path $publish 'Filee.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Publish did not produce Filee.exe' }

$size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host "Filee.exe: $size MB ($Runtime$(if (-not $NoTrim) { ', trimmed' } else { ', untrimmed' }))"

if ($Zip) {
    $zip = Join-Path $OutputDir "$packageName.zip"
    if (Test-Path -LiteralPath $zip) { throw "Package $packageName.zip already exists." }
    Compress-Archive -LiteralPath $exe -DestinationPath $zip
    if (Test-Path -LiteralPath (Join-Path $root 'LICENSE')) {
        Compress-Archive -LiteralPath (Join-Path $root 'LICENSE') -Update -DestinationPath $zip
    }
    "$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())  $packageName.zip" |
        Set-Content -LiteralPath (Join-Path $OutputDir "$packageName-SHA256SUMS.txt") -Encoding ascii
    Write-Host "Zip: $zip"
}
Write-Host "Output: $publish"
