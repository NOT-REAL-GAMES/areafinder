param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.7.0a6\Editor\Unity.exe',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BenchmarkReportPath,
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

function Assert-BenchmarkReport {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "The benchmark test did not produce a report: $Path"
    }

    $report = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    if ($report.marker -ne 'AREAFINDER_BENCHMARK_SUCCESS' -or
        [long]$report.idleAllocatedBytes -ne 0 -or
        [double]$report.sameArea16x16.medianRoutesPerSecond -le 0 -or
        [double]$report.threeArea8x8.medianRoutesPerSecond -le 0) {
        throw "The benchmark report is incomplete or invalid: $Path"
    }

    return $report
}

$previousBenchmarkReport = [Environment]::GetEnvironmentVariable('AREAFINDER_BENCHMARK_REPORT', 'Process')

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
    $benchmarkReport = if ([string]::IsNullOrWhiteSpace($BenchmarkReportPath)) {
        Join-Path $results 'benchmark.json'
    }
    elseif ([IO.Path]::IsPathRooted($BenchmarkReportPath)) {
        [IO.Path]::GetFullPath($BenchmarkReportPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $BenchmarkReportPath))
    }
    [Environment]::SetEnvironmentVariable('AREAFINDER_BENCHMARK_REPORT', $benchmarkReport, 'Process')

    $editResults = Join-Path $results 'editmode.xml'
    Invoke-Unity @(
        '-batchmode', '-nographics', '-projectPath', $hostProject,
        '-runTests', '-testPlatform', 'EditMode',
        '-testResults', $editResults,
        '-logFile', (Join-Path $results 'editmode.log')
    )
    $editCount = Assert-TestResults -Path $editResults -Platform 'Edit Mode' -MinimumTests 9

    $playResults = Join-Path $results 'playmode.xml'
    Invoke-Unity @(
        '-batchmode', '-nographics', '-projectPath', $hostProject,
        '-runTests', '-testPlatform', 'PlayMode',
        '-testResults', $playResults,
        '-logFile', (Join-Path $results 'playmode.log')
    )
    $playCount = Assert-TestResults -Path $playResults -Platform 'Play Mode' -MinimumTests 178
    $benchmark = Assert-BenchmarkReport -Path $benchmarkReport

    Write-Host "Areafinder tests passed: $editCount Edit Mode, $playCount Play Mode."
    Write-Host ("Benchmark medians: 16x16 {0:N1} routes/s, {1:N1} B/request; 3x8x8 {2:N1} routes/s, {3:N1} B/request; idle {4} B." -f
        [double]$benchmark.sameArea16x16.medianRoutesPerSecond,
        [double]$benchmark.sameArea16x16.medianAllocatedBytesPerRequest,
        [double]$benchmark.threeArea8x8.medianRoutesPerSecond,
        [double]$benchmark.threeArea8x8.medianAllocatedBytesPerRequest,
        [long]$benchmark.idleAllocatedBytes)
    Write-Host "Benchmark report: $benchmarkReport"
    if ($KeepProject) {
        Write-Host "Disposable host retained at $hostProject"
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'AREAFINDER_BENCHMARK_REPORT',
        $previousBenchmarkReport,
        'Process')
    if (-not $KeepProject -and (Test-Path -LiteralPath $hostProject)) {
        $resolvedHost = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $hostProject).Path)
        if (-not $resolvedHost.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolvedHost)).StartsWith('AreafinderTests-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected test directory: $resolvedHost"
        }

        Remove-Item -LiteralPath $resolvedHost -Recurse -Force
    }
}
