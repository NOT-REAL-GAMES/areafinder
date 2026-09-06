param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$failures = [Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
}

function Read-JsonObject {
    param([string]$Path)

    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable
    }
    catch {
        Add-Failure "Invalid JSON: $Path ($($_.Exception.Message))"
        return $null
    }
}

function Assert-Equal {
    param(
        [object]$Actual,
        [object]$Expected,
        [string]$Label
    )

    if ($Actual -ne $Expected) {
        Add-Failure "$Label expected '$Expected', found '$Actual'."
    }
}

$allowedTopLevel = @(
    '.editorconfig',
    '.git',
    '.gitattributes',
    '.github',
    '.gitignore',
    'CHANGELOG.md',
    'CHANGELOG.md.meta',
    'Documentation~',
    'Editor',
    'Editor.meta',
    'LICENSE.md',
    'LICENSE.md.meta',
    'package.json',
    'package.json.meta',
    'README.md',
    'README.md.meta',
    'Runtime',
    'Runtime.meta',
    'Samples~',
    'Tests',
    'Tests.meta',
    'Tools~'
)
Get-ChildItem -LiteralPath $root -Force |
    Where-Object { $_.Name -notin $allowedTopLevel } |
    ForEach-Object { Add-Failure "Unexpected top-level package entry: $($_.FullName)" }

Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $_.Extension -in '.json', '.asmdef' } |
    ForEach-Object { $null = Read-JsonObject $_.FullName }

$manifest = Read-JsonObject (Join-Path $root 'package.json')
if ($null -ne $manifest) {
    Assert-Equal $manifest.name 'com.notrealgames.areafinder' 'Package name'
    Assert-Equal $manifest.version '0.1.0' 'Package version'
    Assert-Equal $manifest.displayName 'Areafinder' 'Package display name'
    Assert-Equal $manifest.unity '6000.7' 'Unity version'
    Assert-Equal $manifest.unityRelease '0a6' 'Unity release'
    Assert-Equal $manifest.author.name 'Not Real Games' 'Package author'
    Assert-Equal $manifest.license 'Refer to LICENSE.md file' 'Package license'
}

$assemblyExpectations = @(
    @{
        Path = 'Runtime\NotRealGames.Areafinder.asmdef'
        Name = 'NotRealGames.Areafinder'
        Namespace = 'NotRealGames.Areafinder'
        Platforms = @()
        References = @('Unity.Burst', 'Unity.Collections')
        Test = $false
    },
    @{
        Path = 'Editor\NotRealGames.Areafinder.Editor.asmdef'
        Name = 'NotRealGames.Areafinder.Editor'
        Namespace = 'NotRealGames.Areafinder.Editor'
        Platforms = @('Editor')
        References = @('NotRealGames.Areafinder')
        Test = $false
    },
    @{
        Path = 'Tests\Runtime\NotRealGames.Areafinder.Tests.asmdef'
        Name = 'NotRealGames.Areafinder.Tests'
        Namespace = 'NotRealGames.Areafinder.Tests'
        Platforms = @()
        References = @('NotRealGames.Areafinder')
        Test = $true
    },
    @{
        Path = 'Tests\Editor\NotRealGames.Areafinder.Editor.Tests.asmdef'
        Name = 'NotRealGames.Areafinder.Editor.Tests'
        Namespace = 'NotRealGames.Areafinder.Editor.Tests'
        Platforms = @('Editor')
        References = @('NotRealGames.Areafinder', 'NotRealGames.Areafinder.Editor')
        Test = $true
    }
)

foreach ($expectation in $assemblyExpectations) {
    $path = Join-Path $root $expectation.Path
    $definition = Read-JsonObject $path
    if ($null -eq $definition) {
        continue
    }

    Assert-Equal $definition.name $expectation.Name "$($expectation.Path) name"
    Assert-Equal $definition.rootNamespace $expectation.Namespace "$($expectation.Path) root namespace"
    Assert-Equal $definition.allowUnsafeCode $false "$($expectation.Path) unsafe setting"

    $platforms = @($definition.includePlatforms)
    if (Compare-Object $platforms $expectation.Platforms) {
        Add-Failure "$($expectation.Path) has unexpected includePlatforms: $($platforms -join ', ')."
    }

    $references = @($definition.references)
    if (Compare-Object $references $expectation.References) {
        Add-Failure "$($expectation.Path) has unexpected references: $($references -join ', ')."
    }

    $optional = @($definition.optionalUnityReferences)
    $constraints = @($definition.defineConstraints)
    if ($expectation.Test) {
        if ($optional -notcontains 'TestAssemblies' -or $constraints -notcontains 'UNITY_INCLUDE_TESTS') {
            Add-Failure "$($expectation.Path) is missing its Unity test assembly markers."
        }
    }
    elseif ($optional -contains 'TestAssemblies') {
        Add-Failure "$($expectation.Path) must not be a test assembly."
    }
}

$runtimeFiles = Get-ChildItem -LiteralPath (Join-Path $root 'Runtime') -Recurse -File -Filter '*.cs'
foreach ($file in $runtimeFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($text -match '\bUnityEditor\b') {
        Add-Failure "Runtime source references UnityEditor: $($file.FullName)"
    }
}

$forbiddenPattern = '\b(UnityEngine\.AI|Unity\.AI\.Navigation|NavMesh(?:Surface|Path|Query|Obstacle)?|High[ -]?Precision)\b'
$packageSources = @(
    Get-ChildItem -LiteralPath (Join-Path $root 'Runtime') -Recurse -File -Filter '*.cs'
    Get-ChildItem -LiteralPath (Join-Path $root 'Editor') -Recurse -File -Filter '*.cs'
)
foreach ($file in $packageSources) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($text -match $forbiddenPattern) {
        Add-Failure "Forbidden migration dependency in package source: $($file.FullName)"
    }
}

$ignoredTopLevel = @('.git', '.github', 'Documentation~', 'Samples~', 'Tools~')
$visibleItems = Get-ChildItem -LiteralPath $root -Recurse -Force |
    Where-Object {
        $relative = [IO.Path]::GetRelativePath($root, $_.FullName)
        $topLevel = $relative.Split([IO.Path]::DirectorySeparatorChar)[0]
        $topLevel -notin $ignoredTopLevel -and
        -not $_.Name.StartsWith('.') -and
        -not $_.Name.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)
    }

foreach ($item in $visibleItems) {
    $metaPath = $item.FullName + '.meta'
    if (-not (Test-Path -LiteralPath $metaPath -PathType Leaf)) {
        Add-Failure "Missing Unity .meta file: $metaPath"
    }
}

$forbiddenMetaRoots = @('.github', 'Documentation~', 'Samples~', 'Tools~')
foreach ($relativeRoot in $forbiddenMetaRoots) {
    $path = Join-Path $root $relativeRoot
    if (Test-Path -LiteralPath $path) {
        Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.meta' | ForEach-Object {
            Add-Failure "Unexpected .meta beneath excluded path: $($_.FullName)"
        }
    }
}

foreach ($generatedName in @('Library', 'Temp', 'Obj', 'Logs', 'UserSettings', 'Build', 'Builds')) {
    $generatedPath = Join-Path $root $generatedName
    if (Test-Path -LiteralPath $generatedPath) {
        Add-Failure "Unity-generated repository artifact exists: $generatedPath"
    }
}

if ($failures.Count -gt 0) {
    throw "Areafinder package structure validation failed:`n - $($failures -join "`n - ")"
}

Write-Host 'Areafinder package structure validation passed.'
