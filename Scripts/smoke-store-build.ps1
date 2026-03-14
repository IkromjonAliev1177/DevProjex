[CmdletBinding()]
param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Release.Common.ps1")

function Get-MsBuildPath() {
    $vswherePath = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        try {
            $installationPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
                $candidate = Join-Path $installationPath.Trim() "MSBuild\Current\Bin\MSBuild.exe"
                if (Test-Path $candidate) {
                    return $candidate
                }
            }
        }
        catch {
            # Fall back to PATH lookup below.
        }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "MSBuild.exe not found. Install Visual Studio Build Tools with Desktop Bridge support."
}

function Invoke-ExternalCommand([string]$filePath, [string[]]$arguments, [string]$failureMessage, [string]$workingDirectory) {
    Push-Location $workingDirectory
    try {
        & $filePath @arguments
    }
    finally {
        Pop-Location
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$failureMessage (exit code: $LASTEXITCODE)"
    }
}

$repoRoot = Get-DevProjexRepoRoot -startPath $PSScriptRoot
$defaultVersionInfo = Get-DefaultReleaseVersionInfo -repoRoot $repoRoot
$resolvedDisplayVersion = if ([string]::IsNullOrWhiteSpace($Version)) { [string]$defaultVersionInfo.DisplayVersion } else { $Version.Trim() }
$storePackageVersion = if ($resolvedDisplayVersion -match '^\d+\.\d+\.\d+\.\d+$') {
    $resolvedDisplayVersion
}
else {
    $versionParts = [System.Collections.Generic.List[string]]::new()
    foreach ($part in $resolvedDisplayVersion.Split('.')) {
        $versionParts.Add($part)
    }

    while ($versionParts.Count -lt 4) {
        $versionParts.Add("0")
    }

    $versionParts -join '.'
}

if ($resolvedDisplayVersion -notmatch '^\d+(\.\d+){0,3}$') {
    throw "Invalid version '$resolvedDisplayVersion'."
}

$versionProperties = Get-BuildVersionProperties -displayVersion $resolvedDisplayVersion -storePackageVersion $storePackageVersion
$projectPath = Join-Path $repoRoot "Packaging\Windows\DevProjex.Store\DevProjex.Store.wapproj"
$appProjectPath = Join-Path $repoRoot "Apps\Avalonia\DevProjex.Avalonia\DevProjex.Avalonia.csproj"
$msbuildPath = Get-MsBuildPath
$outputRoot = Join-Path $env:TEMP ("devprojex-store-smoke-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

try {
    Invoke-ExternalCommand -filePath "dotnet" -arguments (@("restore", $appProjectPath, "/p:Configuration=ReleaseStore") + $versionProperties) -failureMessage "dotnet restore failed for store smoke build" -workingDirectory $repoRoot

    $msbuildArgs = @(
        $projectPath,
        "/p:Configuration=ReleaseStore",
        "/p:Platform=x64",
        "/p:AppxBundle=Always",
        "/p:AppxBundlePlatforms=x64|arm64",
        "/p:UapAppxPackageBuildMode=StoreUpload",
        "/p:GenerateAppInstallerFile=false",
        "/p:AppxPackageDir=$outputRoot\"
    ) + $versionProperties

    Invoke-ExternalCommand -filePath $msbuildPath -arguments $msbuildArgs -failureMessage "MS Store smoke build failed" -workingDirectory $repoRoot

    $artifacts = Get-ChildItem -Path $outputRoot -Recurse -File -Include *.msixupload,*.msixbundle,*.msix -ErrorAction SilentlyContinue
    if ($null -eq $artifacts -or $artifacts.Count -eq 0) {
        throw "Store smoke build did not produce any MSIX artifacts."
    }

    Write-Host "Store smoke build succeeded."
    $artifacts | Sort-Object Name | ForEach-Object { Write-Host "  $($_.FullName)" }
}
finally {
    if (Test-Path $outputRoot) {
        Remove-Item -Path $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
