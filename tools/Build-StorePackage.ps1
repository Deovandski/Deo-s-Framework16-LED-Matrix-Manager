<#
.SYNOPSIS
  Builds the Microsoft Store package (.msix) for Deo's LED Matrix Manager.

.DESCRIPTION
  1. dotnet publish (Release, self-contained win-x64) into artifacts\store\layout
  2. Copies the logos and writes AppxManifest.xml from packaging\AppxManifest.template.xml
  3. makepri (resources.pri) and makeappx -> artifacts\store\DeoLedMatrixManager_<version>_x64.msix

  The .msix is not signed: the Store signs it after certification, so upload it as is.
  To try the packaged app on this PC, use -Register (needs Developer Mode). It installs the
  unpacked layout under the identity in use, without signing.

  Store identity (Partner Center > your app > Product identity), first match wins:
    - environment variables STORE_IDENTITY_NAME, STORE_PUBLISHER, STORE_PUBLISHER_DISPLAY_NAME
      and optionally STORE_DISPLAY_NAME (CI)
    - packaging\store-identity.json, which is git-ignored (copy store-identity.example.json)
    - a local-test identity, fine for -Register but rejected by the Store

  Needs the .NET 10 SDK and the Windows 10/11 SDK (makeappx.exe, makepri.exe).

.EXAMPLE
  .\tools\Build-StorePackage.ps1
  Builds the .msix to upload to Partner Center.

.EXAMPLE
  .\tools\Build-StorePackage.ps1 -Register
  Builds, then installs the packaged app on this PC for testing.
#>
[CmdletBinding()]
param(
    # 1.2.3; defaults to <Version> in Directory.Build.props.
    [string]$Version,

    # Install the unpacked layout on this PC (Developer Mode) after building.
    [switch]$Register,

    # Reuse artifacts\store\layout from the previous run instead of publishing again.
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packaging = Join-Path $root 'packaging'
$out = Join-Path $root 'artifacts\store'
$layout = Join-Path $out 'layout'

function Find-SdkTool([string]$name) {
    $bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $bin -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $tool) {
        throw "$name not found. Install a Windows SDK: Visual Studio Installer > Individual components > Windows 11 SDK."
    }
    return $tool
}

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $exe) failed with exit code $LASTEXITCODE" }
}

# --- Version: 1.2.3 -> 1.2.3.0 (the Store requires the fourth part to be 0) ---
if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
    $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
}
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw "Version '$Version' doesn't look like 1.2.3" }
$msixVersion = '{0}.{1}.{2}.0' -f $Matches[1], $Matches[2], $Matches[3]

# --- Store identity (never committed; see the help above) ---
$identityFile = Join-Path $packaging 'store-identity.json'
if ($env:STORE_IDENTITY_NAME) {
    $identity = [pscustomobject]@{
        IdentityName         = $env:STORE_IDENTITY_NAME
        Publisher            = $env:STORE_PUBLISHER
        PublisherDisplayName = $env:STORE_PUBLISHER_DISPLAY_NAME
    }
    $source = 'environment variables'
}
elseif (Test-Path $identityFile) {
    $identity = Get-Content $identityFile -Raw | ConvertFrom-Json
    $source = 'packaging\store-identity.json'
}
else {
    $identity = [pscustomobject]@{
        IdentityName         = 'DeoLedMatrixManager.LocalTest'
        Publisher            = 'CN=DeoLedMatrixManager-LocalTest'
        PublisherDisplayName = 'Local test build'
    }
    $source = 'the local-test identity (the Store will reject this package)'
    Write-Warning 'No Store identity found: building with a local-test identity.'
}
# The app name must match the name reserved in Partner Center (optional "DisplayName" field).
$displayName = if ($env:STORE_DISPLAY_NAME) { $env:STORE_DISPLAY_NAME }
               elseif ($identity.PSObject.Properties['DisplayName'] -and $identity.DisplayName) { $identity.DisplayName }
               else { "Deo's LED Matrix Manager" }
foreach ($field in 'IdentityName', 'Publisher', 'PublisherDisplayName') {
    if (-not $identity.$field) { throw "Store identity from $source is missing '$field'." }
}
Write-Host "Version $msixVersion, identity from $source"

# --- 1. Publish the app ---
if (-not $SkipPublish) {
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    $project = Join-Path $root 'src\LedMatrix.App\LedMatrix.App.csproj'
    Invoke-Tool 'dotnet' @('publish', $project, '-c', 'Release', '-o', $layout, "-p:Version=$Version")
}
elseif (-not (Test-Path (Join-Path $layout 'DeoLedMatrixManager.exe'))) {
    throw 'Nothing to reuse in artifacts\store\layout: run without -SkipPublish.'
}

# --- 2. Logos and manifest ---
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
Copy-Item (Join-Path $packaging 'Assets\*.png') -Destination $assets -Force

$values = @{
    IdentityName         = $identity.IdentityName
    Publisher            = $identity.Publisher
    PublisherDisplayName = $identity.PublisherDisplayName
    DisplayName          = $displayName
    Version              = $msixVersion
}
$manifest = Get-Content (Join-Path $packaging 'AppxManifest.template.xml') -Raw
foreach ($key in $values.Keys) {
    $manifest = $manifest.Replace("{{$key}}", [System.Security.SecurityElement]::Escape([string]$values[$key]))
}
if ($manifest -match '\{\{\w+\}\}') { throw "Unfilled value in the manifest: $($Matches[0])" }
$manifestPath = Join-Path $layout 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

# --- 3. Resource index (picks the right logo size per scale/target size) ---
# Indexed from a small folder with just the logos, so the hundreds of runtime DLLs stay out of it.
$makepri = Find-SdkTool 'makepri.exe'
$priRoot = Join-Path $out 'pri'
if (Test-Path $priRoot) { Remove-Item $priRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $priRoot 'Assets') | Out-Null
Copy-Item (Join-Path $packaging 'Assets\*.png') -Destination (Join-Path $priRoot 'Assets') -Force
Copy-Item $manifestPath -Destination $priRoot
$priConfig = Join-Path $out 'priconfig.xml'
Invoke-Tool $makepri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')
Invoke-Tool $makepri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', (Join-Path $priRoot 'AppxManifest.xml'),
    '/of', (Join-Path $layout 'resources.pri'), '/o')

# --- 4. Pack ---
$makeappx = Find-SdkTool 'makeappx.exe'
$msix = Join-Path $out "DeoLedMatrixManager_${msixVersion}_x64.msix"
Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', $msix, '/o')
Write-Host ''
Write-Host "Package: $msix"

# --- Optional: install the unpacked layout on this PC ---
if ($Register) {
    if ($PSVersionTable.PSEdition -eq 'Core') {
        Import-Module Appx -UseWindowsPowerShell -WarningAction SilentlyContinue
    }
    Get-AppxPackage -Name $identity.IdentityName | Remove-AppxPackage
    Add-AppxPackage -Register $manifestPath
    Write-Host "Installed for testing. Find it in Start; remove it in Settings > Apps."
}
