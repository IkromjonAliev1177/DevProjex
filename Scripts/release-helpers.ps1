Set-StrictMode -Version Latest

function Get-DevProjexRepoRoot([string]$startPath) {
    $candidate = Resolve-Path -Path $startPath

    while ($null -ne $candidate) {
        $directory = $candidate.Path
        if (Test-Path (Join-Path $directory "Directory.Build.props")) {
            return $directory
        }

        $parent = Split-Path -Path $directory -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $directory) {
            break
        }

        $candidate = Resolve-Path -Path $parent
    }

    throw "Repository root was not found from '$startPath'."
}

function Get-DefaultReleaseVersionInfo([string]$repoRoot) {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        throw "Directory.Build.props not found: $propsPath"
    }

    [xml]$props = Get-Content -Path $propsPath
    $propertyGroups = @($props.Project.PropertyGroup)

    function Get-PropertyValue([string]$name) {
        foreach ($propertyGroup in $propertyGroups) {
            $node = $propertyGroup.$name
            $value = if ($null -ne $node) { [string]$node.InnerText } else { $null }
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value.Trim()
            }
        }

        return $null
    }

    function Resolve-PropertyValue([string]$value, [hashtable]$propertyValues) {
        $resolved = $value
        for ($attempt = 0; $attempt -lt 8; $attempt++) {
            $updated = [System.Text.RegularExpressions.Regex]::Replace(
                $resolved,
                '\$\(([^)]+)\)',
                {
                    param($match)

                    $propertyName = $match.Groups[1].Value
                    if ($propertyValues.ContainsKey($propertyName) -and -not [string]::IsNullOrWhiteSpace([string]$propertyValues[$propertyName])) {
                        return [string]$propertyValues[$propertyName]
                    }

                    return $match.Value
                })

            if ($updated -eq $resolved) {
                return $updated
            }

            $resolved = $updated
        }

        return $resolved
    }

    $rawValues = @{
        DevProjexVersion              = Get-PropertyValue "DevProjexVersion"
        DevProjexAssemblyVersion      = Get-PropertyValue "DevProjexAssemblyVersion"
        DevProjexFileVersion          = Get-PropertyValue "DevProjexFileVersion"
        DevProjexInformationalVersion = Get-PropertyValue "DevProjexInformationalVersion"
        DevProjexStorePackageVersion  = Get-PropertyValue "DevProjexStorePackageVersion"
    }

    $displayVersion = Resolve-PropertyValue -value ([string]$rawValues.DevProjexVersion) -propertyValues $rawValues
    $assemblyVersion = Resolve-PropertyValue -value ([string]$rawValues.DevProjexAssemblyVersion) -propertyValues $rawValues
    $fileVersion = Resolve-PropertyValue -value ([string]$rawValues.DevProjexFileVersion) -propertyValues $rawValues
    $informationalVersion = Resolve-PropertyValue -value ([string]$rawValues.DevProjexInformationalVersion) -propertyValues $rawValues
    $storePackageVersion = Resolve-PropertyValue -value ([string]$rawValues.DevProjexStorePackageVersion) -propertyValues $rawValues

    foreach ($requiredValue in @(
        @{ Name = "DevProjexVersion"; Value = $displayVersion },
        @{ Name = "DevProjexAssemblyVersion"; Value = $assemblyVersion },
        @{ Name = "DevProjexFileVersion"; Value = $fileVersion },
        @{ Name = "DevProjexInformationalVersion"; Value = $informationalVersion },
        @{ Name = "DevProjexStorePackageVersion"; Value = $storePackageVersion }
    )) {
        if ([string]::IsNullOrWhiteSpace([string]$requiredValue.Value)) {
            throw "Required release property '$($requiredValue.Name)' is missing in $propsPath"
        }
    }

    return [pscustomobject]@{
        DisplayVersion       = $displayVersion
        AssemblyVersion      = $assemblyVersion
        FileVersion          = $fileVersion
        InformationalVersion = $informationalVersion
        StorePackageVersion  = $storePackageVersion
    }
}

function Get-BuildVersionProperties([string]$displayVersion, [string]$storePackageVersion) {
    if ([string]::IsNullOrWhiteSpace($displayVersion)) {
        throw "DisplayVersion is required."
    }

    if ([string]::IsNullOrWhiteSpace($storePackageVersion)) {
        throw "StorePackageVersion is required."
    }

    return @(
        "/p:DevProjexVersion=$displayVersion",
        "/p:DevProjexAssemblyVersion=$storePackageVersion",
        "/p:DevProjexFileVersion=$storePackageVersion",
        "/p:DevProjexInformationalVersion=$displayVersion",
        "/p:DevProjexStorePackageVersion=$storePackageVersion"
    )
}
