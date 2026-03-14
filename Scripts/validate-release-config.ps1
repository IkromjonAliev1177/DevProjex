[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Release.Common.ps1")

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Assert-VersionFormat([string]$value, [string]$name, [string]$pattern) {
    if ($value -notmatch $pattern) {
        throw "$name has invalid format '$value'."
    }
}

$repoRoot = Get-DevProjexRepoRoot -startPath $PSScriptRoot
$versionInfo = Get-DefaultReleaseVersionInfo -repoRoot $repoRoot

Assert-VersionFormat -value ([string]$versionInfo.DisplayVersion) -name "DevProjexVersion" -pattern '^\d+(\.\d+){0,3}$'
Assert-VersionFormat -value ([string]$versionInfo.AssemblyVersion) -name "DevProjexAssemblyVersion" -pattern '^\d+\.\d+\.\d+\.\d+$'
Assert-VersionFormat -value ([string]$versionInfo.FileVersion) -name "DevProjexFileVersion" -pattern '^\d+\.\d+\.\d+\.\d+$'
Assert-VersionFormat -value ([string]$versionInfo.StorePackageVersion) -name "DevProjexStorePackageVersion" -pattern '^\d+\.\d+\.\d+\.\d+$'
Assert-Condition ($versionInfo.AssemblyVersion -eq $versionInfo.FileVersion) "AssemblyVersion and FileVersion must stay identical."
Assert-Condition ($versionInfo.AssemblyVersion -eq $versionInfo.StorePackageVersion) "AssemblyVersion and StorePackageVersion must stay identical."

$appProjectPath = Join-Path $repoRoot "Apps\Avalonia\DevProjex.Avalonia\DevProjex.Avalonia.csproj"
$appProjectContent = Get-Content -Path $appProjectPath -Raw
foreach ($versionPropertyName in @("Version", "AssemblyVersion", "FileVersion", "InformationalVersion")) {
    Assert-Condition (-not ($appProjectContent -match "<$versionPropertyName>")) "App project must inherit $versionPropertyName from Directory.Build.props: $appProjectPath"
}

$manifestPath = Join-Path $repoRoot "Packaging\Windows\DevProjex.Store\Package.appxmanifest"
[xml]$manifest = Get-Content -Path $manifestPath
$manifestVersion = [string]$manifest.Package.Identity.Version
Assert-Condition ($manifestVersion -eq $versionInfo.StorePackageVersion) "Store manifest version '$manifestVersion' does not match DevProjexStorePackageVersion '$($versionInfo.StorePackageVersion)'."

$wapprojPath = Join-Path $repoRoot "Packaging\Windows\DevProjex.Store\DevProjex.Store.wapproj"
[xml]$wapproj = Get-Content -Path $wapprojPath
$xmlNamespace = New-Object System.Xml.XmlNamespaceManager($wapproj.NameTable)
$xmlNamespace.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
$bundlePlatformsNode = $wapproj.SelectSingleNode("/msb:Project/msb:PropertyGroup/msb:AppxBundlePlatforms", $xmlNamespace)
Assert-Condition ($null -ne $bundlePlatformsNode) "AppxBundlePlatforms was not found in $wapprojPath"
Assert-Condition ([string]$bundlePlatformsNode.InnerText -eq "x64|arm64") "AppxBundlePlatforms must stay x64|arm64."

$listingCsvPath = Join-Path $repoRoot "Packaging\Windows\StoreListing\listing.csv"
$listingHeaders = (Get-Content -Path $listingCsvPath -First 1) -split ','
Assert-Condition (($listingHeaders -contains 'en-us') -and ($listingHeaders -contains 'ru-ru')) "Store listing CSV must include en-us and ru-ru columns."

$releaseAllPath = Join-Path $repoRoot "Scripts\release-all.ps1"
$releaseAllContent = Get-Content -Path $releaseAllPath -Raw
Assert-Condition ($releaseAllContent.Contains('Release.Common.ps1')) "release-all.ps1 must use Release.Common.ps1 for shared release metadata."

$wingetScriptPath = Join-Path $repoRoot "Scripts\winget-update.ps1"
$wingetScriptContent = Get-Content -Path $wingetScriptPath -Raw
Assert-Condition ($wingetScriptContent.Contains('Release.Common.ps1')) "winget-update.ps1 must use Release.Common.ps1 for shared release metadata."
Assert-Condition ($wingetScriptContent.Contains('Get-DefaultReleaseVersionInfo')) "winget-update.ps1 must load default release version info from Release.Common.ps1."
Assert-Condition ($wingetScriptContent.Contains('defaultValue ([string]$defaultReleaseVersionInfo.DisplayVersion)')) "winget-update.ps1 must use shared default display version instead of a hardcoded prompt value."

$macReadmePath = Join-Path $repoRoot "Packaging\MacOS\README.md"
$macReadmeContent = Get-Content -Path $macReadmePath -Raw
Assert-Condition ($macReadmeContent.Contains('YOUR_RELEASE_VERSION')) "Packaging/MacOS/README.md must use a release-version placeholder instead of a stale hardcoded example."

Write-Host "Release configuration is consistent."
Write-Host "  DisplayVersion      : $($versionInfo.DisplayVersion)"
Write-Host "  StorePackageVersion : $($versionInfo.StorePackageVersion)"
Write-Host "  AppxBundlePlatforms : x64|arm64"
