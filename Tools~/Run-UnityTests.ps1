param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.7.0a6\Editor\Unity.exe',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$KeepProject
)

$ErrorActionPreference = 'Stop'

$resolvedEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$hostProject = Join-Path $temporaryRoot ('AreafinderTests-' + [Guid]::NewGuid().ToString('N'))
$hostProject = [IO.Path]::GetFullPath($hostProject)

if (-not $hostProject.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create a test project outside the system temporary directory: $hostProject"
}

function Invoke-Unity {
    param([string[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedEditor
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unity did not start."
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unity exited with code $($process.ExitCode). Arguments: $($Arguments -join ' ')"
    }
}

function Assert-TestResults {
    param(
        [string]$Path,
        [string]$Platform,
        [int]$MinimumTests
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Platform tests did not produce a result file: $Path"
    }

    [xml]$document = Get-Content -Raw -LiteralPath $Path
    $testRun = $document.'test-run'
    if ($null -eq $testRun -or $testRun.result -ne 'Passed') {
        $failures = @($document.SelectNodes('//test-case[@result="Failed"]') | ForEach-Object {
            $message = $_.failure.message.'#text'
            if ([string]::IsNullOrWhiteSpace($message)) {
                $message = $_.failure.message
            }

            "$($_.fullname): $message"
        })
        $details = if ($failures.Count -gt 0) { $failures -join [Environment]::NewLine } else { 'No failed test-case details were recorded.' }
        throw "$Platform tests failed.$([Environment]::NewLine)$details"
    }

    $total = [int]$testRun.total
    if ($total -lt $MinimumTests) {
        throw "$Platform discovered $total tests; expected at least $MinimumTests."
    }

    return $total
}

try {
    Invoke-Unity @('-batchmode', '-nographics', '-quit', '-createProject', $hostProject, '-logFile', '-')

    $manifestPath = Join-Path $hostProject 'Packages\manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable
    $manifest.dependencies['com.notrealgames.areafinder'] = 'file:' + $resolvedPackage.Replace('\', '/')
    $manifest.dependencies['com.unity.test-framework'] = '1.9.0'
    $manifest.testables = @('com.notrealgames.areafinder')
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    $results = Join-Path $hostProject 'TestResults'
    New-Item -ItemType Directory -Force -Path $results | Out-Null

    $editResults = Join-Path $results 'editmode.xml'
    Invoke-Unity @(
        '-batchmode', '-nographics', '-projectPath', $hostProject,
        '-runTests', '-testPlatform', 'EditMode',
        '-testResults', $editResults,
        '-logFile', (Join-Path $results 'editmode.log')
    )
    $editCount = Assert-TestResults -Path $editResults -Platform 'Edit Mode' -MinimumTests 6

    $playResults = Join-Path $results 'playmode.xml'
    Invoke-Unity @(
        '-batchmode', '-nographics', '-projectPath', $hostProject,
        '-runTests', '-testPlatform', 'PlayMode',
        '-testResults', $playResults,
        '-logFile', (Join-Path $results 'playmode.log')
    )
    $playCount = Assert-TestResults -Path $playResults -Platform 'Play Mode' -MinimumTests 84

    Write-Host "Areafinder tests passed: $editCount Edit Mode, $playCount Play Mode."
    if ($KeepProject) {
        Write-Host "Disposable host retained at $hostProject"
    }
}
finally {
    if (-not $KeepProject -and (Test-Path -LiteralPath $hostProject)) {
        $resolvedHost = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $hostProject).Path)
        if (-not $resolvedHost.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolvedHost)).StartsWith('AreafinderTests-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected test directory: $resolvedHost"
        }

        Remove-Item -LiteralPath $resolvedHost -Recurse -Force
    }
}
